#include "En_graphic.h"
#include <DxLib.h>

#include "En_dxlibstub.h"
#include "LR2_embedding.h"
#include <algorithm>
#ifdef _WIN32
namespace { HMONITOR startupMonitor = nullptr; }
#endif

void CaptureStartupMonitor() {
#ifdef _WIN32
	startupMonitor = MonitorFromWindow(GetForegroundWindow(), MONITOR_DEFAULTTONEAREST);
#endif
}

#ifdef _WIN32
static void CenterWindow(HMONITOR monitor) {
	const HWND window = GetMainWindowHandle();
	if (embedding::Enabled() || !IsWindow(window) || GetWindowModeFlag() != 1) return;
	const HMONITOR currentMonitor = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
	if (!monitor) monitor = currentMonitor;
	MONITORINFO info = { sizeof(MONITORINFO) };
	RECT outer{}, client{};
	if (!GetMonitorInfoW(monitor, &info) || !GetWindowRect(window, &outer) || !GetClientRect(window, &client)) return;
	if (monitor != currentMonitor) {
		SetWindowPosition(info.rcWork.left + (info.rcWork.right - info.rcWork.left - (outer.right - outer.left)) / 2,
			info.rcWork.top + (info.rcWork.bottom - info.rcWork.top - (outer.bottom - outer.top)) / 2);
		if (!GetWindowRect(window, &outer) || !GetClientRect(window, &client)) return;
	}
	const int width = client.right - client.left, height = client.bottom - client.top;
	if (width <= 0 || height <= 0) return;
	const int availableWidth = info.rcWork.right - info.rcWork.left - (outer.right - outer.left - width);
	const int availableHeight = info.rcWork.bottom - info.rcWork.top - (outer.bottom - outer.top - height);
	if (availableWidth <= 0 || availableHeight <= 0) return;
	const double scale = std::min({ 1.0, (double)availableWidth / width, (double)availableHeight / height });
	if (scale < 1.0) {
		SetWindowSize(std::max(1, (int)(width * scale)), std::max(1, (int)(height * scale)));
		if (!GetWindowRect(window, &outer)) return;
	}
	SetWindowPosition(info.rcWork.left + (info.rcWork.right - info.rcWork.left - (outer.right - outer.left)) / 2,
		info.rcWork.top + (info.rcWork.bottom - info.rcWork.top - (outer.bottom - outer.top)) / 2);
}
#endif

void CenterGameWindow(bool startup) {
#ifdef _WIN32
	CenterWindow(startup ? startupMonitor : nullptr);
#else
	(void)startup;
#endif
}

int ScreenCapture(uint iGrHandle, int x, int y){
	GetDrawScreenGraph(0, 0, x, y, iGrHandle, 1);
	return 0;
}

int hBackImage;
int SetBackground(int hImage) {
	hBackImage = hImage;
	return 1;
}

int screenSizeX, screenSizeY;
int skinSizeX, skinSizeY;

// Maps the global resolution counter (config.system.resolution) to pixels.
void GetConfigResolution(int counter, int* outX, int* outY) {
	switch (counter) {
		case 1:  *outX = 1280; *outY = 720;  break;  // HD
		case 2:  *outX = 1920; *outY = 1080; break;  // FHD (experimental)
		default: *outX = 640;  *outY = 480;  break;  // SD
	}
}

int Resize(game* g, double skinX, double skinY, bool bit16) {
	int oldXpos = 320, oldYpos = 240;
#ifdef _WIN32
	const HMONITOR monitor = MonitorFromWindow(GetMainWindowHandle(), MONITOR_DEFAULTTONEAREST);
#endif

	GetWindowPosition(&oldXpos, &oldYpos);
	SetGraphMode(skinX, skinY, bit16? 16 : 32, GetRefreshRate());
	SetWindowSizeExtendRate((double)g->config.system.windowsize_x / skinX, (double)g->config.system.windowsize_y / skinY);
	SetDrawScreen(DX_SCREEN_BACK);
	SetWindowPosition(oldXpos, oldYpos);
#ifdef _WIN32
	CenterWindow(monitor);
#endif

	skinSizeX = skinX;
	skinSizeY = skinY;
	return 0;
}
