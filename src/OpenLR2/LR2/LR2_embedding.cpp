#include "LR2_embedding.h"
#include <DxLib.h>
#include <atomic>
#include <chrono>
#include <stdexcept>
#include <string>
#include <fstream>
#include <algorithm>
#include <iomanip>
#include <cstdlib>
#include <vector>
#ifdef _WIN32
#include <mmsystem.h>
#include <shellapi.h>
#include <Windows/DxGraphicsD3D11.h>
#endif

namespace embedding {
namespace {
std::atomic<std::uintptr_t> target{0};
int frameLimit = 0;
std::string renderProfile = "baseline";
#ifdef _WIN32
std::uint32_t owner = 0;
std::filesystem::path stopFile;
std::chrono::steady_clock::time_point nextStopCheck{};
int previousWidth = 0, previousHeight = 0, previousDrawWidth = 0, previousDrawHeight = 0;
double previousScaleX = 0, previousScaleY = 0;
HWND Window() { return reinterpret_cast<HWND>(target.load()); }
bool Alive() {
    DWORD process = 0;
    return IsWindow(Window()) && GetWindowThreadProcessId(Window(), &process) && process == owner;
}
#endif
}
bool Enabled() { return target.load() != 0; }
std::uintptr_t TargetWindow() { return target.load(); }
void ValidateTarget(const launcher::Request& request) {
#ifdef _WIN32
    const auto window = reinterpret_cast<HWND>(static_cast<std::uintptr_t>(request.embedWindow));
    DWORD process = 0;
    if (request.embedWindow > UINTPTR_MAX || !IsWindow(window) ||
        !GetWindowThreadProcessId(window, &process) || process != request.hostProcess || process == GetCurrentProcessId() ||
        !(GetWindowLongPtrW(window, GWL_STYLE) & WS_CHILD))
        throw std::runtime_error("Invalid or expired embedding target: LAZERRAVE_EMBED_V1");
#else
    (void)request;
    throw std::runtime_error("Embedded presentation requires Windows");
#endif
}
void Prepare(const launcher::Request& request) {
    if (!request.embedWindow) return;
    ValidateTarget(request);
#ifdef _WIN32
    target = static_cast<std::uintptr_t>(request.embedWindow);
    renderProfile = request.renderProfile;
    frameLimit = renderProfile == "uncapped" ? -1 : request.frameLimit;
    owner = request.hostProcess;
    stopFile = request.file;
    stopFile += ".stop";
    // Keep an owned top-level HWND for DirectInput; only presentation uses the host's child HWND.
    if (!SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(Window())))
        throw std::runtime_error("Cannot match the embedding host's DPI context");
    SetWindowVisibleFlag(FALSE);
    SetDoubleStartValidFlag(TRUE);
    SetAlwaysRunFlag(TRUE);
    SetKeyExclusiveCooperativeLevelFlag(FALSE);
    SetUseASyncChangeWindowModeFunction(FALSE, nullptr, nullptr);
    SetUseDirect3DVersion(DX_DIRECT3D_11);
    SetUseDirect3D11SwapEffect(renderProfile == "discard" ? DX_SWAP_EFFECT_DISCARD : DX_SWAP_EFFECT_FLIP_DISCARD);
    SetUseMediaFoundationFlag(renderProfile != "directshow");
    SetMovieUseYUVFormatSurfaceFlag(renderProfile != "rgb-video");
    SetWaitVSyncFlag(UseVSync());
#endif
}
bool UseVSync() { return Enabled() && renderProfile == "vsync"; }
bool BgaEnabled() { return !Enabled() || renderProfile != "no-bga"; }
bool TracingEnabled() {
    static const bool enabled = [] { const auto value = std::getenv("LAZERRAVE_FRAME_TRACE"); return value && std::string_view(value) == "1"; }();
    return enabled;
}
void TraceFrame(double loopStart, double frameStart, double messageEnd, double presentStart, double presentEnd,
    int scene, int phase, bool playing, double playTime, int bgaLayer1, int bgaLayer2, const GameplayTrace& gameplay) {
#ifdef _WIN32
    if (!TracingEnabled()) return;
    struct Row {
        double time, interval, pacing, message, draw, present, outside, playTime;
        int scene, phase, bga1, bga2;
        bool playing, vsync;
        std::uint32_t statisticsStatus, displayedPresent, displayRefresh, submittedPresent;
        long long syncQpc;
        GameplayTrace gameplay;
    };
    struct Trace {
        std::vector<Row> rows;
        double previous = 0;
        unsigned int dropped = 0;
        struct Live { LONG version = 1, sequence = 0; double values[17]{}; };
        HANDLE mapping = nullptr;
        Live* live = nullptr;
        double liveTime = 0, liveMax = 0, lastPlayTime = 0, completeTime = 0;
        unsigned int liveFrames = 0;
        bool everPlaying = false, completed = false, closeRequested = false;
        GameplayTrace lastGameplay{};
        int maxCombo = 0;
        Trace() {
            std::size_t capacity = 180000;
            if (const auto value = std::getenv("LAZERRAVE_TRACE_CAPACITY")) {
                const auto requested = std::strtoul(value, nullptr, 10);
                if (requested >= 180000 && requested <= 2000000) capacity = requested;
            }
            rows.reserve(capacity);
            const auto name = L"Local\\LazerRave-frame-trace-" + std::to_wstring(GetCurrentProcessId());
            mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(Live), name.c_str());
            if (mapping) live = static_cast<Live*>(MapViewOfFile(mapping, FILE_MAP_WRITE, 0, 0, sizeof(Live)));
            if (live) { *live = Live{}; }
        }
        ~Trace() {
            // Keep disk I/O outside the measured gameplay loop.
            std::error_code error;
            std::filesystem::create_directories("logs", error);
            std::ofstream file(std::filesystem::path("logs") / ("engine-trace-" + std::to_string(GetCurrentProcessId()) + ".csv"));
            file << "time_ms,interval_ms,pacing_ms,message_ms,draw_ms,present_ms,outside_ms,play_time_ms,scene,phase,playing,vsync,bga1,bga2,profile,frame_limit,dropped_rows,statistics_status,displayed_present,display_refresh,submitted_present,sync_qpc,combo,judged_notes,total_notes,event_cursor,bpm,chart_time,render_time,song_duration_ms\n";
            file << std::fixed << std::setprecision(4);
            for (const auto& row : rows)
                file << row.time << ',' << row.interval << ',' << row.pacing << ',' << row.message << ',' << row.draw << ',' << row.present
                    << ',' << row.outside << ',' << row.playTime << ',' << row.scene << ',' << row.phase << ',' << row.playing << ',' << row.vsync
                    << ',' << row.bga1 << ',' << row.bga2 << ',' << (Enabled() ? renderProfile : "classic") << ',' << frameLimit << ',' << dropped
                    << ',' << row.statisticsStatus << ',' << row.displayedPresent << ',' << row.displayRefresh << ',' << row.submittedPresent << ',' << row.syncQpc << ',' << row.gameplay.combo << ',' << row.gameplay.judged << ',' << row.gameplay.total
                    << ',' << row.gameplay.eventCursor << ',' << row.gameplay.bpm << ',' << row.gameplay.chartTime
                    << ',' << row.gameplay.renderTime << ',' << row.gameplay.songDuration << '\n';
            std::ofstream session(std::filesystem::path("logs") / ("engine-session-" + std::to_string(GetCurrentProcessId()) + ".csv"));
            session << "completed,total_notes,judged_notes,max_combo,last_play_time_ms,song_duration_ms,dropped_rows\n";
            session << completed << ',' << lastGameplay.total << ',' << lastGameplay.judged << ',' << maxCombo << ','
                << std::fixed << std::setprecision(4) << lastPlayTime << ',' << lastGameplay.songDuration << ',' << dropped << '\n';
            if (live) UnmapViewOfFile(live);
            if (mapping) CloseHandle(mapping);
        }
    };
    static Trace trace;
    HRESULT statisticsStatus = E_NOTIMPL;
    D_DXGI_FRAME_STATISTICS statistics{};
    UINT submitted = 0;
    if (GetUseDirect3DVersion() == DX_DIRECT3D_11) {
        const auto index = DxLib::GraphicsHardDataDirect3D11.Device.Screen.TargetOutputWindow;
        if (index >= 0 && index < DX_D3D11_MAX_OUTPUTWINDOW) {
            const auto chain = DxLib::GraphicsHardDataDirect3D11.Device.Screen.OutputWindowInfo[index].DXGISwapChain;
            if (chain) {
                statisticsStatus = chain->GetFrameStatistics(&statistics);
                chain->GetLastPresentCount(&submitted);
            }
        }
    }
    if (trace.previous > 0) {
        if (trace.rows.size() < trace.rows.capacity())
            trace.rows.push_back({presentEnd, presentEnd - trace.previous, frameStart - loopStart, messageEnd - frameStart,
                presentStart - messageEnd, presentEnd - presentStart, loopStart - trace.previous, playTime,
                scene, phase, bgaLayer1, bgaLayer2, playing, GetWaitVSyncFlag() != 0,
                static_cast<std::uint32_t>(statisticsStatus), statistics.PresentCount, statistics.PresentRefreshCount, submitted, statistics.SyncQPCTime.QuadPart, gameplay});
        else ++trace.dropped;
    }
    if (playing) { trace.everPlaying = true; trace.lastPlayTime = playTime; }
    if (gameplay.total > 0) trace.lastGameplay = gameplay;
    trace.maxCombo = std::max(trace.maxCombo, gameplay.combo);
    if (!trace.completed && trace.everPlaying && !playing && (scene == 5 || phase == 2)
        && gameplay.total > 0 && gameplay.judged >= gameplay.total && trace.lastPlayTime >= gameplay.songDuration - 100) {
        trace.completed = true;
        trace.completeTime = presentEnd;
    }
    trace.liveFrames++;
    if (trace.previous > 0) trace.liveMax = std::max(trace.liveMax, presentEnd - trace.previous);
    if (trace.liveTime == 0) trace.liveTime = presentEnd;
    if (trace.live && presentEnd - trace.liveTime >= 250) {
        double values[] = {presentEnd, playTime, trace.liveFrames * 1000.0 / (presentEnd - trace.liveTime), trace.liveMax,
            double(gameplay.combo), double(gameplay.judged), double(gameplay.total), double(scene), double(phase),
            double(playing), double(trace.completed), gameplay.bpm, gameplay.chartTime, gameplay.renderTime,
            double(trace.dropped), gameplay.songDuration, double(trace.rows.size())};
        InterlockedIncrement(&trace.live->sequence);
        MemoryBarrier();
        std::copy(std::begin(values), std::end(values), std::begin(trace.live->values));
        MemoryBarrier();
        InterlockedIncrement(&trace.live->sequence);
        trace.liveTime = presentEnd; trace.liveFrames = 0; trace.liveMax = 0;
    }
    static const bool fullSong = [] { const auto value = std::getenv("LAZERRAVE_DIAGNOSTIC_FULL_SONG"); return value && std::string_view(value) == "1"; }();
    if (fullSong && trace.completed && !trace.closeRequested && presentEnd - trace.completeTime >= 2000) {
        trace.closeRequested = true;
        PostMessageW(GetMainWindowHandle(), WM_CLOSE, 0, 0);
    }
    trace.previous = presentEnd;
#else
    (void)loopStart; (void)frameStart; (void)messageEnd; (void)presentStart; (void)presentEnd;
    (void)scene; (void)phase; (void)playing; (void)playTime; (void)bgaLayer1; (void)bgaLayer2; (void)gameplay;
#endif
}
double FrameRateLimit() {
    if (!Enabled() || UseVSync() || frameLimit < 0) return 0;
    if (frameLimit > 0) return frameLimit;
#ifdef _WIN32
    static auto nextRefreshCheck = std::chrono::steady_clock::time_point{};
    static double refreshRate = 0;
    const auto now = std::chrono::steady_clock::now();
    if (now >= nextRefreshCheck) {
        nextRefreshCheck = now + std::chrono::milliseconds(250);
        MONITORINFOEXW monitor{};
        monitor.cbSize = sizeof(monitor);
        DEVMODEW mode{};
        mode.dmSize = sizeof(mode);
        if (GetMonitorInfoW(MonitorFromWindow(Window(), MONITOR_DEFAULTTONEAREST), &monitor) &&
            EnumDisplaySettingsW(monitor.szDevice, ENUM_CURRENT_SETTINGS, &mode) && mode.dmDisplayFrequency > 1)
            refreshRate = mode.dmDisplayFrequency;
        else if (refreshRate <= 0) refreshRate = 120;
    }
    return refreshRate;
#else
    return 120;
#endif
}
void RecordFrame(double frameStart, double messageEnd, double presentStart, double presentEnd, int scene, int bgaLayer1, int bgaLayer2) {
#ifdef _WIN32
    if (!Enabled()) return;
    struct Statistics {
        std::ofstream file;
        double start = 0, previous = 0, sum = 0, maximum = 0, messageMax = 0, drawMax = 0, presentMax = 0;
        unsigned int count = 0, slow = 0;
        Statistics() {
            std::error_code error;
            std::filesystem::create_directories("logs", error);
            file.open(std::filesystem::path("logs") / ("engine-frames-" + std::to_string(GetCurrentProcessId()) + ".csv"));
            file << "window_ms,profile,renderer,vsync,frame_limit,scene,frames,fps,average_ms,max_ms,over_16_7_ms,message_max_ms,draw_max_ms,present_max_ms,bga1,bga2\n";
            file << std::fixed << std::setprecision(3);
        }
    };
    static Statistics stats;
    if (!stats.file) return;
    if (stats.start == 0) stats.start = presentEnd;
    if (stats.previous > 0) {
        const double interval = presentEnd - stats.previous;
        ++stats.count;
        stats.sum += interval;
        stats.maximum = (std::max)(stats.maximum, interval);
        if (interval > 1000.0 / 60) ++stats.slow;
        stats.messageMax = (std::max)(stats.messageMax, messageEnd - frameStart);
        stats.drawMax = (std::max)(stats.drawMax, presentStart - messageEnd);
        stats.presentMax = (std::max)(stats.presentMax, presentEnd - presentStart);
    }
    stats.previous = presentEnd;
    const double elapsed = presentEnd - stats.start;
    if (elapsed < 5000 || stats.count == 0) return;
    stats.file << elapsed << ',' << renderProfile << ',' << GetUseDirect3DVersion() << ',' << UseVSync() << ',' << frameLimit
        << ',' << scene << ',' << stats.count << ',' << stats.count * 1000.0 / stats.sum << ',' << stats.sum / stats.count
        << ',' << stats.maximum << ',' << stats.slow << ',' << stats.messageMax << ',' << stats.drawMax << ',' << stats.presentMax
        << ',' << bgaLayer1 << ',' << bgaLayer2 << '\n';
    stats.file.flush();
    stats.start = presentEnd;
    stats.count = stats.slow = 0;
    stats.sum = stats.maximum = stats.messageMax = stats.drawMax = stats.presentMax = 0;
#else
    (void)frameStart; (void)messageEnd; (void)presentStart; (void)presentEnd; (void)scene; (void)bgaLayer1; (void)bgaLayer2;
#endif
}
bool Tick() {
    if (!Enabled()) return true;
#ifdef _WIN32
    if (!Alive()) return false;
    const auto now = std::chrono::steady_clock::now();
    if (now >= nextStopCheck) {
        nextStopCheck = now + std::chrono::milliseconds(100);
        std::error_code error;
        if (std::filesystem::exists(stopFile, error)) return false;
    }
    RECT rect{};
    if (!GetClientRect(Window(), &rect)) return false;
    const int width = rect.right, height = rect.bottom;
    int drawWidth = 0, drawHeight = 0;
    double scaleX = 1, scaleY = 1;
    GetDrawScreenSize(&drawWidth, &drawHeight);
    GetWindowSizeExtendRate(&scaleX, &scaleY);
    if (width <= 0 || height <= 0 || drawWidth <= 0 || drawHeight <= 0 || scaleX <= 0 || scaleY <= 0) return true;
    if (width != previousWidth || height != previousHeight || drawWidth != previousDrawWidth || drawHeight != previousDrawHeight ||
        scaleX != previousScaleX || scaleY != previousScaleY) {
        if (SetScreenFlipTargetWindow(Window(), width / (drawWidth * scaleX), height / (drawHeight * scaleY)) < 0)
            return false;
        previousWidth = width; previousHeight = height;
        previousDrawWidth = drawWidth; previousDrawHeight = drawHeight;
        previousScaleX = scaleX; previousScaleY = scaleY;
    }
#endif
    return true;
}
void Connect(const launcher::Request& request) {
    if (!Enabled()) return;
#ifdef _WIN32
    if (!Tick()) throw std::runtime_error("Cannot connect the embedded viewport");
    launcher::Reply(request, true, "Embedded presentation ready: LAZERRAVE_EMBED_V1",
        reinterpret_cast<std::uintptr_t>(GetMainWindowHandle()));
#endif
}
bool InputActive() {
    if (!Enabled()) return false;
#ifdef _WIN32
    if (!Alive() || !IsWindowVisible(Window())) return false;
    const auto root = GetAncestor(Window(), GA_ROOT);
    return root && !IsIconic(root) && GetForegroundWindow() == root;
#else
    return false;
#endif
}
int MouseButtons() {
#ifdef _WIN32
    if (!InputActive()) return 0;
    POINT point{}; RECT rect{};
    if (!GetCursorPos(&point) || !GetWindowRect(Window(), &rect) || !PtInRect(&rect, point)) return 0;
    return ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) ? 1 : 0) |
        ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) ? 2 : 0) | ((GetAsyncKeyState(VK_MBUTTON) & 0x8000) ? 4 : 0);
#else
    return 0;
#endif
}
int Probe(const launcher::Request& request) {
#ifdef _WIN32
    bool initialized = false;
    try {
        Prepare(request);
        ChangeWindowMode(TRUE);
        SetGraphMode(640, 480, 32);
        SetNotSoundFlag(TRUE);
        SetUseTSFFlag(FALSE);
        SetOutApplicationLogValidFlag(FALSE);
        if (DxLib_Init() < 0) throw std::runtime_error("Embedded Direct3D initialization failed");
        initialized = true;
        SetAlwaysRunFlag(TRUE);
        SetDrawScreen(DX_SCREEN_BACK);
        if (!Tick()) throw std::runtime_error("Embedded viewport sizing failed");
        Connect(request);
        // Allow a second engine to initialise while this diagnostic viewport is still alive.
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
        int frames = 0;
        while (std::chrono::steady_clock::now() < deadline && Tick() && !ProcessMessage()) {
            DrawBox(0, 0, 640, 480, GetColor(30, 90, 120), TRUE);
            if (ScreenFlip() < 0) throw std::runtime_error("Embedded Direct3D presentation failed");
            ++frames;
        }
        RECT finalSize{};
        GetClientRect(Window(), &finalSize);
        launcher::Reply(request, true, "Embedded Direct3D presentation passed; input-active=" + std::to_string(InputActive()) +
            "; frames=" + std::to_string(frames) + "; width=" + std::to_string(finalSize.right) + "; height=" + std::to_string(finalSize.bottom),
            reinterpret_cast<std::uintptr_t>(GetMainWindowHandle()));
        DxLib_End();
        return 0;
    } catch (const std::exception& error) {
        if (initialized) DxLib_End();
        launcher::Reply(request, false, error.what());
        return 2;
    }
#else
    launcher::Reply(request, false, "Embedded presentation requires Windows");
    return 2;
#endif
}
}
