# LazerRave

LazerRave is a BMS rhythm game project combining a desktop client adapted from [osu!lazer](https://github.com/ppy/osu) with the [OpenLR2](https://github.com/GOMazk/OpenLR2) gameplay engine. It brings BMS library management and game settings into the lazer interface while retaining classic LR2 gameplay and skin compatibility.

The project is under active development and currently targets **Windows 10/11 x64**. The C# client manages selection and configuration; a separate native C++ process runs gameplay, either within the client window or in its own window. This client-and-engine architecture is the basis for continued development, with embedded gameplay as the primary integration path.

## Features

- **BMS library browsing:** navigate folder hierarchies, search songs, filter by key mode and difficulty, preview media, and view existing EX SCORE records.
- **Direct gameplay launch:** send the selected chart, speed, timing offset, and arrangement settings to OpenLR2 without going through its song selection menu.
- **Shared library configuration:** use the same library roots in the desktop client and classic OpenLR2 mode.
- **Multiple gameplay windows:** choose embedded or separate-window gameplay, configure preset or custom window dimensions, and retain the classic OpenLR2 entry point.
- **Persistent gameplay settings:** save window, BGA, rendering, and gameplay options between sessions.
- **Reused lazer interface:** retain its menu, song carousel, animations, and settings components. New primary interface text is available in English, Simplified Chinese, and Japanese; complete localization and display-scaling validation remain in progress.
- **Unified Windows build:** compile the client and engine and assemble their dependencies into one runtime directory through a single build entry point.

Current BMS gameplay runs through OpenLR2, rather than the retained Mania ruleset. The client does not import `.osu` charts or osu! replay files. Editor and online-service components are retained as development foundations; BMS editing, accounts, multiplayer, and cloud services are not yet available.

## Roadmap

Development proceeds in stages while keeping local BMS gameplay usable.

| Stage | Planned work |
| --- | --- |
| Desktop foundation | Improve scanning and indexing, settings, localization, high-DPI support, themes, and gameplay presentation. Complete reproducible-build and compatibility baselines. |
| Accounts and content | Add accounts, avatars, signatures, news, changelogs, an online song catalog, and individual song downloads. |
| Replay and courses | Introduce versioned input-event replays, cloud replay storage, course and grade certification, and a two-player prototype. |
| Multiplayer | Support rooms of up to 16 players, an in-game live leaderboard, and host-selected song packaging, upload, and server distribution. |
| Compatibility and audio | Expand BMS and LR2 skin coverage, add loudness analysis and optional audio processing, and improve performance diagnostics. |
| Chart and replay compatibility | Extend the existing OpenLR2 backend with BMSON support and versioned replay validation. |
| Independent distribution | Package the existing client and engine with default resources so an existing LR2 installation is no longer required. |
| Further extensions | Develop BMS editing tools and evaluate optional Steam identity integration. |

These are development goals, not released features or delivery commitments. See the [detailed roadmap](docs/roadmap.md) for dependencies and acceptance criteria.

## Developer

### Requirements

- Windows 10/11 x64.
- Visual Studio 2022 with **Desktop development with C++**, MSVC v143, and a Windows SDK.
- .NET SDK **10.0.401**, installed locally by the build script when absent.
- CMake **3.29 or newer**, Ninja, NASM, Git, and Python.
- Windows PowerShell 5.1; PowerShell 7 is required for dependency builds.
- Network access for initial tool and dependency downloads.

### Build and run

Run commands from the repository root. **`build.cmd` is the only public build entry point.**

```powershell
# Compile and assemble the complete desktop package.
.\build.cmd

# Compile without packaging or requiring private runtime resources.
.\build.cmd -CompileOnly

# Build only the native OpenLR2 engine.
.\build.cmd -EngineOnly
```

Complete packaging requires LR2 runtime resources under `res/runtime/`, including `LR2files/Config/config.xml` and `LR2files/Theme/`. Local songs can be placed under `res/library/BMS/`. Private songs, skins, and player data are not supplied by the source repository. An external runtime can also be provided:

```powershell
.\build.cmd -RuntimeSource 'D:\LR2beta3'
```

After packaging, launch `out/app/LazerRave.exe`. Application assemblies and the managed .NET runtime are bundled into the executable, without trimming. The reduced osu! resource assembly is stored at `Resources/osu.Game.Resources.dll`; translations are stored under `Localization/<culture>/`. Native libraries, `OpenLR2_x64.exe`, and LR2 resources remain external, so keep the entire output directory together. No separate .NET installation is required. Build scripts do not launch the application or run tests.

### Source and documentation

The client stores its settings and database in `userdata/` beside the executable, with logs under `userdata/logs/` and rebuildable caches under `cache/`. On the first normal launch, existing settings and client data are copied from the previous AppData locations without replacing portable files or deleting the originals. Keep `userdata/` and `LR2files/` when updating or moving the application. Normal operation does not require administrator privileges when the application directory is writable.

| Path | Purpose |
| --- | --- |
| `src/LazerRave/` | Application entry point, BMS integration, and engine bridge |
| `src/osu-lazer/` | Reduced and adapted osu!lazer source snapshot |
| `src/OpenLR2/` | Native gameplay engine and its dependencies |
| `res/` | Runtime resources, local library, and third-party notices |
| `res/branding/` | LazerRave logo, Windows icon, and generated branding assets |
| `scripts/` | Internal build, packaging, and maintenance tools |
| `docs/` | Architecture, compatibility, development, and operations documentation |
| `out/build/` | Project build trees, intermediate files, and development binaries |
| `out/deps/` | Third-party dependency build and installation caches |
| `out/app/` | Complete runnable application and local player data |
| `out/logs/`, `out/reports/` | Build logs, diagnostics, and migration records |

Open `LazerRave.slnx` for C# development. See [Build and run](docs/getting-started/build-and-run.md), [Engine bridge](docs/development/engine-bridge.md), and the [documentation index](docs/index.md) for further details. The detailed documentation is currently in Chinese and uses the MkDocs Read the Docs theme.

Full builds publish directly into `out/app/`; there is no separate publish staging copy. Close the application before rebuilding that directory. When clearing project build caches, remove only `out/build/` and retain dependency caches and player data.

The build entry point requests English .NET and MSVC diagnostics and uses UTF-8 for native console communication, including under Windows PowerShell 5.1. The original process environment and console encodings are restored when the script exits.

Debug symbols, link libraries, generated API documentation, and the ExampleIR sample remain in development build outputs and are omitted from the runtime package. After successful publication, the build script removes previous loose copies of bundled files and relocated translations using an MSBuild-generated manifest. Player data is retained.

Publication builds the reduced resource pack from the pinned upstream NuGet assembly using `src/LazerRave/resource-pack/`. Its removal policy targets assets for retired modes and screens while retaining fonts, localization, common UI, editor resources, and ordinary multiplayer-room assets. The upstream package cache and development assemblies remain unchanged. An inventory and size summary are generated under `out/build/client/obj/<configuration>/net10.0-windows/win-x64/resource-pack/`. See [Resource packaging](docs/development/resource-pack.md) for the policy and loading details.

## Thanks

- [ppy and osu! contributors](https://github.com/ppy/osu) for osu!lazer and the interface components adapted by this project.
- [GOMazk and OpenLR2 contributors](https://github.com/GOMazk/OpenLR2) for the native BMS gameplay engine.
- The BMS and LR2 communities for their work on charts, skins, tools, and compatibility knowledge.

LazerRave is an independent project and is not an official osu! or LR2 release. Project contributions are covered by the root [MIT license](LICENSE); upstream code, libraries, fonts, and other assets retain their respective licenses. See the upstream license files and [third-party notices](res/licenses/) for their terms. Songs and skins retain their respective authors' rights.

Two bundled components need attention before any public distribution, and both are recorded under [third-party notices](res/licenses/):

- **FMOD Engine** is commercial, closed-source middleware vendored under `src/OpenLR2/dep/FMOD/`. Firelight Technologies permits storing the headers and libraries in a repository, but shipping a build that contains them requires an FMOD licence.
- **OpenLR2** does not state a licence upstream, so the terms for redistributing `src/OpenLR2/` are not established. This is unresolved and should be settled before the repository is made public.

DX Library (DxLib), also vendored under `src/OpenLR2/dep/`, is free to use but requires the copyright notices reproduced in its notice file to be included with distributed documentation.
