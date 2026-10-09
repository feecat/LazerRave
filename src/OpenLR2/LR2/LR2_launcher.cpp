#include "LR2_launcher.h"
#include "LR2_embedding.h"
#include "LR2_songmanage.h"
#include "LR2_configsave.h"
#include "LR2_statlong.h"
#include "En_fileutil.h"
#include <tinyxml.h>
#include <fstream>
#include <memory>
#include <stdexcept>
#include <unordered_map>
#include <charconv>
#include <limits>
#include <algorithm>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <chrono>

namespace launcher {
namespace {
constexpr auto rootsFile = "LR2files/Config/lazerrave-roots.xml";
struct PlayOptionDefinition {
    const char* name;
    int minimum;
    int maximum;
    void (*apply)(game&, int);
    int (*read)(const game&);
};
const PlayOptionDefinition playOptions[] = {
#define LR2_PLAY_OPTION(name, minimum, maximum, field) \
    {name, minimum, maximum, [](game& state, int value) { state.field = value; }, \
        [](const game& state) { return static_cast<int>(state.field); }},
#include "LR2_launcher_options.inc"
#undef LR2_PLAY_OPTION
};
const PlayOptionDefinition& FindPlayOption(const std::string& name) {
    const auto found = std::find_if(std::begin(playOptions), std::end(playOptions),
        [&](const auto& option) { return name == option.name; });
    if (found == std::end(playOptions)) throw std::runtime_error("Unknown gameplay option: LAZERRAVE_PLAY_OPTIONS_V1: " + name);
    return *found;
}
unsigned int EncodingCodepage(const std::string& encoding) {
    if (encoding == "auto") return 0;
    if (encoding == "utf-8") return 65001;
    if (encoding == "cp932") return 932;
    if (encoding == "gb18030") return 54936;
    throw std::runtime_error("Unsupported chart encoding");
}
std::string Utf8Path(const std::filesystem::path& path) {
    const auto text = path.u8string();
    return {text.begin(), text.end()};
}
std::string Normalize(const std::string& path) {
    auto result = Utf8Path(std::filesystem::absolute(std::filesystem::u8path(path)).lexically_normal());
    if (result.back() != std::filesystem::path::preferred_separator)
        result += std::filesystem::path::preferred_separator;
    return result;
}
void AddRoot(CONFIG_JUKEBOX& box, const std::string& path) {
    auto normalized = Normalize(path);
    for (int i = 0; i < box.numOfPath; ++i) {
        if (Normalize(box.path[i].body) == normalized) return;
    }
    // Classic mode appends custom and rival folders to this fixed array.
    if (box.numOfPath >= static_cast<int>(std::size(box.path)) - 32)
        throw std::runtime_error("Too many library roots (reserve required for classic folders)");
    box.path[box.numOfPath++].assign(normalized.c_str());
}
bool InLibrary(const std::string& path, const CONFIG_JUKEBOX& box) {
    const auto file = std::filesystem::u8path(Normalize(path));
    for (int i = 0; i < box.numOfPath; ++i) {
        const auto root = std::filesystem::u8path(Normalize(box.path[i].body));
#ifdef _WIN32
        const auto full = file.wstring();
        const auto prefix = root.wstring();
        if (full.size() >= prefix.size() && CompareStringOrdinal(full.data(), static_cast<int>(prefix.size()),
            prefix.data(), static_cast<int>(prefix.size()), TRUE) == CSTR_EQUAL) return true;
#else
        if (file.string().starts_with(root.string())) return true;
#endif
    }
    return false;
}
void SaveXml(const TiXmlDocument& doc, const std::filesystem::path& path, bool durable = true) {
    TiXmlPrinter printer;
    doc.Accept(&printer);
    auto temporary = path;
    temporary += ".tmp";
    {
        std::ofstream file(temporary, std::ios::binary | std::ios::trunc);
        file << printer.CStr() << '\n';
        if (!file) throw std::runtime_error("Cannot write launcher response");
    }
#ifdef _WIN32
    if (!MoveFileExW(temporary.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | (durable ? MOVEFILE_WRITE_THROUGH : 0)))
        throw std::runtime_error("Cannot replace launcher response");
#else
    std::filesystem::rename(temporary, path);
#endif
}
TiXmlElement* Child(TiXmlElement& parent, const char* name, const std::string& text) {
    auto element = new TiXmlElement(name);
    element->LinkEndChild(new TiXmlText(text.c_str()));
    parent.LinkEndChild(element);
    return element;
}
void CheckSql(int code, sqlite3* db) {
    if (code != SQLITE_OK) throw std::runtime_error(sqlite3_errmsg(db));
}
std::unordered_map<std::string, int> ReadScores(game& state, TiXmlElement& response) {
    std::unordered_map<std::string, int> scores;
    try {
        const std::string id = state.config.player.id.body;
        if (id.empty() || id == "." || id == ".." || id.find_first_of("/\\:") != std::string::npos)
            throw std::runtime_error("Invalid classic player ID");
        auto path = std::filesystem::path("LR2files/Database/Score") / std::filesystem::u8path(id + ".db");
        if (!std::filesystem::exists(path)) return scores;
        sqlite3* raw = nullptr;
        auto code = sqlite3_open_v2(Utf8Path(path).c_str(), &raw, SQLITE_OPEN_READONLY, nullptr);
        std::unique_ptr<sqlite3, decltype(&sqlite3_close)> db(raw, sqlite3_close);
        CheckSql(code, raw);
        sqlite3_busy_timeout(raw, 3000);
        sqlite3_stmt* statement = nullptr;
        CheckSql(sqlite3_prepare_v2(raw, "SELECT hash,clear,perfect,great,good,bad,poor,totalnotes,maxcombo,minbp,playcount,clearcount,failcount,rank,rate,clear_db,op_history,scorehash FROM score", -1, &statement, nullptr), raw);
        std::unique_ptr<sqlite3_stmt, decltype(&sqlite3_finalize)> rows(statement, sqlite3_finalize);
        int result;
        while ((result = sqlite3_step(statement)) == SQLITE_ROW) {
            STATUS stat{};
            int* values[] = {&stat.clear, &stat.stat_pgreat, &stat.stat_great, &stat.stat_good,
                &stat.stat_bad, &stat.stat_poor, &stat.total_notes, &stat.stat_maxcombo, &stat.minbp,
                &stat.playcount, &stat.clearcount, &stat.failcount, &stat.rank, &stat.rate,
                &stat.clear_db, &stat.op_history};
            bool valid = true;
            for (int i = 0; i < 16; ++i) {
                const auto value = sqlite3_column_int64(statement, i + 1);
                if (value < (i == 8 ? -1 : 0) || value > 100000000) valid = false;
                *values[i] = static_cast<int>(value);
            }
            if (!valid) continue;
            CSTR hash = SQL_GetColumn(0, statement);
            CSTR checksum = SQL_GetColumn(17, statement);
            if (stat.rank > 8) stat.rank = 8;
            if (stat.rank == 0 && (stat.stat_pgreat + stat.stat_great > 0)) stat.rank = 1;
            if (stat.playcount > 0 && isSameScoreHash(&stat, &state.config.player.passMD5, &hash, &checksum))
                scores.emplace(hash.body, stat.stat_pgreat * 2 + stat.stat_great);
        }
        if (result != SQLITE_DONE) CheckSql(result, raw);
    } catch (const std::exception& error) {
        Child(response, "warning", std::string("Cannot read classic scores: ") + error.what());
        scores.clear();
    }
    return scores;
}
}

#include "LR2_launcher_scores.inc"

Request ReadRequest(int argc, char** argv) {
    Request request;
    for (int i = 1; i < argc; ++i) {
        if (std::string_view(argv[i]) != "--lazerrave-request") continue;
        if (argc != 3 || i != 1) throw std::runtime_error("Expected --lazerrave-request <absolute XML path>");
        request.active = true;
        request.file = std::filesystem::u8path(argv[2]);
        if (!request.file.is_absolute() || std::filesystem::file_size(request.file) > 1024 * 1024)
            throw std::runtime_error("Invalid launcher request path or size");
        TiXmlDocument doc;
        std::ifstream file(request.file, std::ios::binary);
        std::string xml((std::istreambuf_iterator<char>(file)), {});
        doc.Parse(xml.c_str(), nullptr, TIXML_ENCODING_UTF8);
        auto root = doc.RootElement();
        if (doc.Error() || !root || std::string_view(root->Value()) != "lazerrave" ||
            !root->Attribute("version") || std::string_view(root->Attribute("version")) != "1")
            throw std::runtime_error("Unsupported launcher request: LAZERRAVE_BRIDGE_V1");
        auto mode = root->Attribute("mode");
        request.mode = mode ? mode : "";
        if (request.mode != "play" && request.mode != "validate" && request.mode != "sync" && request.mode != "catalog" && request.mode != "refresh" && request.mode != "import" && request.mode != "embed-probe")
            throw std::runtime_error("Invalid launcher mode");
        for (auto element = root->FirstChildElement(); element; element = element->NextSiblingElement()) {
            const std::string name = element->Value();
            const std::string text = element->GetText() ? element->GetText() : "";
            if (name == "telemetry") {
                if (text != "LAZERRAVE_SCORE_STREAM_V1" || request.mode != "play") throw std::runtime_error("Invalid score stream request");
                request.telemetry = true;
            }
            else if (name == "chart") request.chart = text;
            else if (name == "render-profile") request.renderProfile = text;
            else if (name == "encoding") request.encoding = text;
            else if (name == "root") request.roots.push_back(text);
            else if (name == "library-source" && text == "settings") request.settingsRoots = true;
            else if (name == "play-options") {
                for (auto option = element->FirstChildElement(); option; option = option->NextSiblingElement()) {
                    const char* key = option->Attribute("name");
                    const char* textValue = option->Attribute("value");
                    if (std::string_view(option->Value()) != "option" || !key || !textValue)
                        throw std::runtime_error("Invalid gameplay option: LAZERRAVE_PLAY_OPTIONS_V1");
                    const auto& definition = FindPlayOption(key);
                    const std::string valueText(textValue);
                    int value = 0;
                    const auto parsed = std::from_chars(valueText.data(), valueText.data() + valueText.size(), value);
                    if (parsed.ec != std::errc{} || parsed.ptr != valueText.data() + valueText.size() ||
                        value < definition.minimum || value > definition.maximum ||
                        !request.playOptions.emplace(key, value).second)
                        throw std::runtime_error("Invalid or duplicate gameplay option: " + std::string(key));
                }
                const auto minimum = request.playOptions.find("hs_min");
                const auto maximum = request.playOptions.find("hs_max");
                if (minimum != request.playOptions.end() && maximum != request.playOptions.end() && minimum->second > maximum->second)
                    throw std::runtime_error("Minimum speed must not exceed maximum speed");
            }
            else if (name == "embed-window" || name == "host-process") {
                std::uint64_t value = 0;
                const auto parsed = std::from_chars(text.data(), text.data() + text.size(), value);
                if (parsed.ec != std::errc{} || parsed.ptr != text.data() + text.size() || value == 0 ||
                    (name == "host-process" && value > std::numeric_limits<std::uint32_t>::max()))
                    throw std::runtime_error("Invalid embedding option: LAZERRAVE_EMBED_V1");
                if (name == "embed-window") request.embedWindow = value;
                else request.hostProcess = static_cast<std::uint32_t>(value);
            }
            else if (name == "speed" || name == "offset" || name == "arrangement" || name == "frame-limit") {
                size_t count = 0;
                const int value = std::stoi(text, &count);
                if (count != text.size()) throw std::runtime_error("Invalid numeric launch option");
                if (name == "speed") request.speed = value;
                else if (name == "offset") request.offset = value;
                else if (name == "arrangement") request.arrangement = value;
                else request.frameLimit = value;
            } else throw std::runtime_error("Unknown launch option: " + name);
        }
        EncodingCodepage(request.encoding);
        if (request.renderProfile != "baseline" && request.renderProfile != "uncapped" &&
            request.renderProfile != "vsync" && request.renderProfile != "directshow" &&
            request.renderProfile != "discard" && request.renderProfile != "no-bga" && request.renderProfile != "rgb-video")
            throw std::runtime_error("Unsupported rendering profile");
        if (request.embedWindow || request.hostProcess || request.mode == "embed-probe") {
            if ((request.mode != "play" && request.mode != "validate" && request.mode != "embed-probe") ||
                !request.embedWindow || !request.hostProcess)
                throw std::runtime_error("Incomplete embedding target: LAZERRAVE_EMBED_V1");
            embedding::ValidateTarget(request);
        }
        if (request.speed < 50 || request.speed > 1000 || request.offset < -1000 || request.offset > 1000 ||
            request.arrangement < OPTION_RANDOM_OFF || request.arrangement > OPTION_RANDOM_END || request.roots.size() > 100 ||
            request.frameLimit < -1 || request.frameLimit > 1000 || (request.frameLimit > 0 && request.frameLimit < 30))
            throw std::runtime_error("Launch option out of range");
        if (request.mode == "play" || request.mode == "validate") {
            if (request.chart.empty() || !std::filesystem::u8path(request.chart).is_absolute() ||
                !std::filesystem::is_regular_file(std::filesystem::u8path(request.chart)) || !IsBmsFile(request.chart.c_str()))
                throw std::runtime_error("Selected chart is missing or unsupported");
        }
        return request;
    }
    return request;
}

void ApplyLibraryRoots(CONFIG_JUKEBOX& box) {
    TiXmlDocument doc(rootsFile);
    if (!std::filesystem::exists(rootsFile)) return;
    if (!doc.LoadFile(TIXML_ENCODING_UTF8) || !doc.RootElement())
        throw std::runtime_error("Invalid LazerRave library roots");
    auto selected = std::make_unique<CONFIG_JUKEBOX>(box);
    selected->numOfPath = 0;
    if (auto encoding = doc.RootElement()->Attribute("encoding"))
        SetBmsTextCodepage(EncodingCodepage(encoding));
    for (auto root = doc.RootElement()->FirstChildElement("root"); root; root = root->NextSiblingElement("root"))
        if (root->GetText()) AddRoot(*selected, root->GetText());
    box = *selected;
}

void Reply(const Request& request, bool success, const std::string& message, std::uint64_t engineWindow, const game* effectiveState) {
    TiXmlDocument doc;
    auto root = new TiXmlElement("lazerrave");
    root->SetAttribute("version", "1");
    root->SetAttribute("status", success ? "ok" : "error");
    doc.LinkEndChild(root);
    Child(*root, "message", message);
    if (effectiveState) {
        auto options = new TiXmlElement("play-options");
        for (const auto& definition : playOptions) {
            auto option = new TiXmlElement("option");
            option->SetAttribute("name", definition.name);
            option->SetAttribute("value", definition.read(*effectiveState));
            options->LinkEndChild(option);
        }
        root->LinkEndChild(options);
    }
    if (engineWindow) {
        Child(*root, "engine-window", std::to_string(engineWindow));
        Child(*root, "viewport", std::to_string(request.embedWindow));
    }
    auto path = request.file;
    path += ".reply.xml";
    SaveXml(doc, path);
}

void ApplyPlay(const Request& request, game& state) {
    SetBmsTextCodepage(EncodingCodepage(request.encoding));
    state.directoryPath.assign(request.chart.c_str());
    state.cmd_directplay = true;
    state.config.play.hiSpeed[PLAYER_1] = request.speed;
    state.config.play.hiSpeed[PLAYER_2] = request.speed;
    state.config.play.hsfix = OPTION_HSFIX_OFF;
    state.config.play.basespeed = 100;
    state.config.play.judgetiming = request.offset;
    state.config.play.autojudge = 0;
    state.config.play.random[PLAYER_1] = request.arrangement;
    state.config.play.random[PLAYER_2] = request.arrangement;
    state.config.play.battle = OPTION_BATTLE_OFF;
    for (const auto& [name, value] : request.playOptions) FindPlayOption(name).apply(state, value);
    if (state.config.play.hsmin > state.config.play.hsmax)
        throw std::runtime_error("Minimum speed must not exceed maximum speed");
    if (state.cmd_auto) state.cmd_nosave = 1;
    if (request.renderProfile == "no-bga") state.config.play.bga = 0;
}

int RunHeadless(const Request& request, game& state) {
    try {
        const bool sync = request.mode == "sync" || request.mode == "refresh" || request.mode == "import";
        SetBmsTextCodepage(EncodingCodepage(request.encoding));
        if (request.mode == "validate") {
            ApplyPlay(request, state);
            BMSMETA meta;
            if (!ParseBMSMETA(&meta, request.chart.c_str(), 0)) throw std::runtime_error("Cannot parse selected chart");
            Reply(request, true, "chart=" + request.chart + "; keys=" + std::to_string(meta.keymode) +
                "; speed=" + std::to_string(state.config.play.hiSpeed[PLAYER_1]) +
                "; offset=" + std::to_string(state.config.play.judgetiming) +
                "; arrangement=" + std::to_string(state.config.play.random[PLAYER_1]) +
                "; title=" + meta.title.body + "; artist=" + meta.artist.body, 0, &state);
            return 0;
        }
        if (sync || request.settingsRoots) {
            state.config.jukebox.numOfPath = 0;
            for (auto& path : request.roots) {
                if (!std::filesystem::is_directory(std::filesystem::u8path(path)))
                    throw std::runtime_error("Library directory is missing: " + path);
                AddRoot(state.config.jukebox, path);
            }
        } else ApplyLibraryRoots(state.config.jukebox);
        sqlite3* raw = nullptr;
        auto flags = sync ? SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE : SQLITE_OPEN_READONLY;
        auto code = sqlite3_open_v2("LR2files/Database/song.db", &raw, flags, nullptr);
        std::unique_ptr<sqlite3, decltype(&sqlite3_close)> db(raw, sqlite3_close);
        CheckSql(code, raw);
        sqlite3_busy_timeout(raw, 3000);
        if (sync) {
            const char* backupPath = request.mode == "refresh"
                ? "LR2files/Database/song.db.before-metadata-repair.bak"
                : "LR2files/Database/song.db.before-lazerrave.bak";
            if (!std::filesystem::exists(backupPath)) {
                sqlite3* backupDb = nullptr;
                auto backupCode = sqlite3_open(backupPath, &backupDb);
                std::unique_ptr<sqlite3, decltype(&sqlite3_close)> backupConnection(backupDb, sqlite3_close);
                CheckSql(backupCode, backupDb);
                auto backup = sqlite3_backup_init(backupDb, "main", raw, "main");
                if (!backup) throw std::runtime_error(sqlite3_errmsg(backupDb));
                auto result = sqlite3_backup_step(backup, -1);
                sqlite3_backup_finish(backup);
                if (result != SQLITE_DONE) {
                    backupConnection.reset();
                    std::filesystem::remove(backupPath);
                    throw std::runtime_error("Cannot back up song database");
                }
            }
            CheckSql(sqlite3_exec(raw, "BEGIN IMMEDIATE", nullptr, nullptr, nullptr), raw);
            try {
                if (request.mode == "import" && (request.chart.empty() || !InLibrary(request.chart, state.config.jukebox) ||
                    !std::filesystem::is_regular_file(std::filesystem::u8path(request.chart)) || !IsBmsFile(request.chart.c_str())))
                    throw std::runtime_error("Imported chart is missing or outside the library");
                const auto imported = request.mode == "import" ? Utf8Path(std::filesystem::u8path(request.chart).parent_path()) : "";
                SyncSongCatalog(raw, &state.config.jukebox, request.mode == "refresh", imported.empty() ? nullptr : imported.c_str());
                CheckSql(sqlite3_exec(raw, "COMMIT", nullptr, nullptr, nullptr), raw);
            } catch (...) {
                sqlite3_exec(raw, "ROLLBACK", nullptr, nullptr, nullptr);
                throw;
            }
            TiXmlDocument roots;
            auto element = new TiXmlElement("lazerrave-roots");
            element->SetAttribute("encoding", request.encoding.c_str());
            roots.LinkEndChild(element);
            for (auto& path : request.roots) Child(*element, "root", Normalize(path));
            SaveXml(roots, rootsFile);
        }
        TiXmlDocument response;
        auto root = new TiXmlElement("lazerrave");
        root->SetAttribute("version", "1");
        root->SetAttribute("status", "ok");
        response.LinkEndChild(root);
        const auto scores = ReadScores(state, *root);
        for (int i = 0; i < state.config.jukebox.numOfPath; ++i)
            Child(*root, "root", Normalize(state.config.jukebox.path[i].body));
        sqlite3_stmt* statement = nullptr;
        CheckSql(sqlite3_prepare_v2(raw, "SELECT path,title,artist,level,difficulty,mode,maxbpm,karinotes,hash FROM song ORDER BY path", -1, &statement, nullptr), raw);
        std::unique_ptr<sqlite3_stmt, decltype(&sqlite3_finalize)> rows(statement, sqlite3_finalize);
        int result;
        while ((result = sqlite3_step(statement)) == SQLITE_ROW) {
            const auto chartPath = sqlite3_column_text(statement, 0);
            if (!chartPath || !InLibrary(reinterpret_cast<const char*>(chartPath), state.config.jukebox)) continue;
            auto chart = new TiXmlElement("chart");
            for (int i = 0; i < 8; ++i) {
                constexpr const char* names[] = {"path", "title", "artist", "level", "difficulty", "keys", "bpm", "notes"};
                auto value = sqlite3_column_text(statement, i);
                chart->SetAttribute(names[i], value ? reinterpret_cast<const char*>(value) : "");
            }
            const auto hash = sqlite3_column_text(statement, 8);
            const auto score = hash ? scores.find(reinterpret_cast<const char*>(hash)) : scores.end();
            if (score != scores.end()) chart->SetAttribute("score", score->second);
            root->LinkEndChild(chart);
        }
        if (result != SQLITE_DONE) CheckSql(result, raw);
        auto path = request.file;
        path += ".reply.xml";
        SaveXml(response, path);
        return 0;
    } catch (const std::exception& error) {
        Reply(request, false, error.what());
        return 2;
    }
}
}
