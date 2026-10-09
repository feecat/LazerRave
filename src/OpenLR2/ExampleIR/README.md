# CustomIR API

CustomIR allows sending detailed score data to third-party score-tracking servers, like Bokutachi.

The modules are loaded from `LR2files/CustomIRs/<ModuleFolder>/`.

As a module developer, keep in mind that depending on game build configuration the naming convention for .dll files varies.
File extension must be prefixed with architecture indicator, and file name must be followed by "\_D" for debug configuration.
For example: "ExternalIR.x86.dll", "ExternalIR_D.x64.dll".

Further information can be seen in [ExternalIR.cpp](ExternalIR.cpp) example and its comments.

## Existing modules

- Bokutachi: <https://github.com/MatVeiQaaa/BokutachiIR_OpenLR2>

