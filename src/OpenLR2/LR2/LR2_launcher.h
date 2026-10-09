#pragma once

#include <filesystem>
#include <cstdint>
#include <string>
#include <vector>
#include <unordered_map>

struct game;
struct CONFIG_JUKEBOX;

namespace launcher {
struct Request {
    bool active = false;
    bool telemetry = false;
    std::string mode;
    std::filesystem::path file;
    std::string chart;
    int speed = 200;
    int offset = 0;
    int arrangement = 0;
    int frameLimit = 0;
    std::unordered_map<std::string, int> playOptions;
    std::string renderProfile = "baseline";
    std::string encoding = "auto";
    std::vector<std::string> roots;
    bool settingsRoots = false;
    std::uint64_t embedWindow = 0;
    std::uint32_t hostProcess = 0;
};
Request ReadRequest(int argc, char** argv);
void ApplyLibraryRoots(CONFIG_JUKEBOX& jukebox);
int RunHeadless(const Request& request, game& state);
void PublishScore(const Request& request, const game& state, bool exiting = false);
void FlushScores();
void ApplyPlay(const Request& request, game& state);
void Reply(const Request& request, bool success, const std::string& message, std::uint64_t engineWindow = 0, const game* effectiveState = nullptr);
}
