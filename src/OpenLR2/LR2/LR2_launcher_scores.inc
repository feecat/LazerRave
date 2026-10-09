namespace {
struct LiveScore {
    int exscore = 0, combo = 0, maxCombo = 0, misses = 0, clear = 0;
    double progress = 0;
    bool finished = false, aborted = false;
};
class ScoreWriter {
    std::mutex gate;
    std::condition_variable changed;
    std::thread worker;
    LiveScore latest;
    bool pending = false, stopping = false, finalSent = false;
    std::chrono::steady_clock::time_point next{};
public:
    ~ScoreWriter() { Stop(); }
    void Publish(const Request& request, const game& state, bool exiting) {
        if (!request.telemetry || finalSent) return;
        const bool result = state.procSelecter == 5 || state.procSelecter == 13;
        const auto now = std::chrono::steady_clock::now();
        if (!exiting && !result && now < next) return;
        next = now + std::chrono::milliseconds(120);
        const auto& player = state.gameplay.player[PLAYER_1];
        LiveScore score;
        score.exscore = std::max(0, player.exscore);
        score.combo = std::max(0, player.now_combo);
        score.maxCombo = std::max(0, player.max_combo);
        score.misses = std::max(0, player.judgecount[1] + player.judgecount[2]);
        score.clear = std::clamp(player.clearType, 0, 9);
        score.finished = result || exiting;
        score.aborted = exiting && !result;
        score.progress = result ? 1.0 : player.totalnotes > 0
            ? std::clamp(static_cast<double>(player.note_current) / player.totalnotes, 0.0, 1.0) : 0.0;
        finalSent = score.finished;
        {
            std::lock_guard lock(gate);
            latest = score; pending = true;
            if (!worker.joinable()) worker = std::thread([this, path = request.file.wstring()] {
                for (;;) {
                    LiveScore value;
                    {
                        std::unique_lock lock(gate);
                        changed.wait(lock, [this] { return pending || stopping; });
                        if (!pending && stopping) break;
                        value = latest; pending = false;
                    }
                    try {
                        TiXmlDocument doc;
                        auto root = new TiXmlElement("score");
                        root->SetAttribute("protocol", "LAZERRAVE_SCORE_STREAM_V1");
                        root->SetAttribute("ex-score", value.exscore);
                        root->SetAttribute("combo", value.combo);
                        root->SetAttribute("max-combo", value.maxCombo);
                        root->SetAttribute("misses", value.misses);
                        root->SetAttribute("clear-type", value.clear);
                        root->SetDoubleAttribute("progress", value.progress);
                        root->SetAttribute("finished", value.finished ? 1 : 0);
                        root->SetAttribute("aborted", value.aborted ? 1 : 0);
                        doc.LinkEndChild(root);
                        SaveXml(doc, std::filesystem::path(path + L".progress.xml"), false);
                    } catch (const std::exception&) {
                        // A score stream failure must not interrupt the audio or render thread.
                    }
                }
            });
        }
        changed.notify_one();
    }
    void Stop() {
        { std::lock_guard lock(gate); stopping = true; }
        changed.notify_one();
        if (worker.joinable()) worker.join();
    }
};
ScoreWriter scoreWriter;
}
void PublishScore(const Request& request, const game& state, bool exiting) { scoreWriter.Publish(request, state, exiting); }
void FlushScores() { scoreWriter.Stop(); }
