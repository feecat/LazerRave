#pragma once
#include "LR2_launcher.h"

namespace embedding {
void ValidateTarget(const launcher::Request& request);
void Prepare(const launcher::Request& request);
void Connect(const launcher::Request& request);
bool Tick();
bool Enabled();
bool InputActive();
double FrameRateLimit();
bool UseVSync();
bool BgaEnabled();
bool TracingEnabled();
struct GameplayTrace {
    int combo, judged, total, eventCursor;
    double bpm, chartTime, renderTime, songDuration;
};
void TraceFrame(double loopStart, double frameStart, double messageEnd, double presentStart, double presentEnd,
    int scene, int phase, bool playing, double playTime, int bgaLayer1, int bgaLayer2, const GameplayTrace& gameplay);
void RecordFrame(double frameStart, double messageEnd, double presentStart, double presentEnd, int scene, int bgaLayer1, int bgaLayer2);
std::uintptr_t TargetWindow();
int MouseButtons();
int Probe(const launcher::Request& request);
}
