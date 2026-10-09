#pragma once

#include "structure.h"

int ScreenCapture(uint iGrHandle, int x, int y);
extern int hBackImage;
int SetBackground(int hImage);

int Resize(game* g, double skinX, double skinY, bool bit16);
void GetConfigResolution(int counter, int* outX, int* outY);
extern int skinSizeX, skinSizeY;
extern int screenSizeX, screenSizeY;
