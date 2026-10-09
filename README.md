# LazerRave

LazerRave is a BMS rhythm game combining a desktop client adapted from [osu!lazer](https://github.com/ppy/osu) with the [OpenLR2](https://github.com/GOMazk/OpenLR2) gameplay engine. It retains classic LR2 gameplay and skin compatibility within the lazer interface.

The project is in **Beta** and targets **Windows 10/11 x64**. The desktop release version is maintained in [version.props](version.props).

[Website](https://lazerrave.com) · [Download](https://lazerrave.com/download) · [GitHub Releases](https://github.com/feecat/LazerRave/releases) · [Documentation](docs/index.md)

## Features

- Browse BMS folder hierarchies, search songs, switch difficulties, and filter by key mode.
- Launch the selected chart directly with speed, timing, gauge, and arrangement settings.
- Choose embedded or separate-window gameplay, with classic OpenLR2 mode available.
- Share library roots and retain existing LR2 scores and skins.
- Access accounts, Internet Ranking, difficulty tables, song packs, rooms, and chat through the cloud service.
- Share host-selected songs with download progress; install them under `Shared/`. Temporary server copies expire after two hours.

Synchronized multiplayer gameplay and replay verification are still in development. BMS gameplay uses OpenLR2; `.osu` charts and osu! replay imports are not supported. See the [roadmap](docs/roadmap.md) for planned work.

## Download and run

Download the Windows ZIP from the [download page](https://lazerrave.com/download), extract the complete folder to a writable location, and run `LazerRave.exe`. Keep all extracted files together; no separate .NET installation is required.

Add your songs to `BMS/` or select another library folder in Settings. Settings and client data are stored in `userdata/` beside the executable. Preserve `userdata/`, `Shared/`, and your LR2 configuration, scores, and replays when updating. Release packages exclude personal data and private songs.

## Development

### Requirements

- Windows 10/11 x64 and Visual Studio 2022 with Desktop development with C++, MSVC v143, and a Windows SDK.
- .NET SDK 10.0.401; the build script prepares a local SDK when needed.
- CMake 3.29+, Ninja, NASM, Git, and Python 3.11+.
- Windows PowerShell 5.1; PowerShell 7 for dependency builds.

Initial builds require network access. Full desktop packaging requires LR2 resources under `res/runtime/`, including `LR2files/Config/config.xml` and `LR2files/Theme/`. These private resources and songs are not included in the repository.

### Build

Run commands from the repository root. **`build.cmd` is the single public build entry point.**

```powershell
# Build the complete desktop application into out/app/.
.\build.cmd

# Compile without assembling the runtime package.
.\build.cmd -CompileOnly

# Build and create a versioned ZIP under out/releases/.
.\build.cmd -PackageZip

# Build the native engine only.
.\build.cmd -EngineOnly

# Build the website and server into out/cloud/.
.\build.cmd -Cloud
```

Use `-RuntimeSource 'D:\LR2beta3'` to supply an external LR2 runtime. After a complete desktop build, run `out/app/LazerRave.exe`. Package updates close running game processes in the destination directory; compile-only builds leave them open. Build commands do not launch the game or run tests.

The cloud module uses React, TypeScript, ASP.NET Core 10, SignalR, and PostgreSQL. Deployment configuration includes Docker Compose and Caddy.

### Source layout

| Path | Purpose |
| --- | --- |
| `src/LazerRave/` | Desktop client and engine bridge |
| `src/osu-lazer/` | Adapted osu!lazer source |
| `src/OpenLR2/` | Native gameplay engine |
| `src/Cloud/` | Website, API, and multiplayer services |
| `res/` | Resources, branding, and third-party notices |
| `scripts/` | Build and maintenance tools |
| `docs/` | Development and deployment documentation |
| `out/` | Application, release packages, and build outputs |

Open `LazerRave.slnx` for C# development. Further details: [Build and run](docs/getting-started/build-and-run.md), [Versioning and releases](docs/development/versioning-and-release.md), [Cloud service](docs/development/cloud.md), and [Deployment](docs/operations/cloud-deployment.md).

## Credits

Thanks to [ppy and osu! contributors](https://github.com/ppy/osu), [GOMazk and OpenLR2 contributors](https://github.com/GOMazk/OpenLR2), and the BMS and LR2 communities.

LazerRave is an independent project. See [LICENSE](LICENSE) and [third-party notices](res/licenses/) for license information.
