# Windows 构建与运行

所有命令在仓库根目录执行。标准入口同时构建 C# 前端和 C++ 引擎，普通构建生成完整运行目录；构建脚本不启动应用、不执行测试。

统一入口设定英文 .NET/MSVC 诊断及 UTF-8 控制台通信，避免 Windows PowerShell 5.1 错误解码中文工具输出。脚本结束后恢复原有环境变量、控制台编码及界面文化设置。

NuGet 会缓存警告的翻译文本。检测到旧中文警告时，客户端构建先刷新还原缓存；正常增量构建不强制重复还原。

构建期间将 `TEMP`、`TMP` 和 `TMPDIR` 指向仓库内的 `out/build/temp/`，供 .NET、MSBuild 和原生编译工具使用，避免系统临时目录权限异常阻断构建。开始时检查目录可写性，并将路径记录到构建日志；结束后恢复原有环境变量。该目录属于构建缓存，不随运行包分发。

## 环境要求

| 项目 | 要求 |
| --- | --- |
| 系统 | Windows 10/11 x64 |
| C++ 编译器 | Visual Studio 2022，Desktop development with C++、MSVC v143、Windows SDK |
| .NET | SDK 10.0.401，由脚本安装到 `.tools/dotnet/` |
| 构建工具 | CMake 3.29 或以上、Ninja、NASM、Git、Python |
| 脚本 | Windows PowerShell 5.1；依赖构建需要 PowerShell 7 |
| 网络 | 首次下载工具、NuGet 包和 vcpkg 依赖时需要 |
| 运行资源 | `res/runtime/LR2files/`；纯编译不需要私人运行资源 |

当前 C++ 使用 C++23、DxLib、FMOD、SQLite、TinyXML 和 MD5。vcpkg 基线为 `cd61e1e26a038e82d6550a3ebbe0fbbfe7da78e3`。脚本优先读取 `.tools/` 中的工具，本机基线为 CMake 4.3.4、Ninja 1.13.2 和 NASM 3.01。

## 完整客户端

```powershell
.\build.cmd
.\build.cmd -CompileOnly
.\build.cmd -BuildEngine -Jobs 4
.\build.cmd -RuntimeSource 'D:\LR2beta3' -Destination 'D:\LazerRave'
```

默认前端配置为 Release，引擎为 x64 RelWithDebInfo。`-CompileOnly` 只编译；普通构建将 .NET 自包含前端、最新引擎、FMOD 和运行资源复制至 `out/app/`。前端采用托管单文件发布，不启用程序集裁剪；无需另行安装 .NET。引擎源码较新时自动增量构建，`-BuildEngine` 显式调用引擎构建，`-Jobs` 控制 C++ 并行任务数。根目录仅保留 `build.cmd` 作为构建入口；`scripts/` 内的 PowerShell 文件负责调度、前端编译、引擎编译和打包，无需分别执行。

默认资源来源是 `res/runtime/`。首次配置的曲库目录为 EXE 同级的 `BMS/`；设置中可添加多个绝对或相对目录，相对路径以 EXE 所在目录为基准。资源复制补齐目标缺失的文件，保留目标已有配置、皮肤、成绩和回放。目标目录正在运行时停止构建，须退出程序后更新；不自动创建额外运行副本。

运行 `out/app/LazerRave.exe`。移动或分发时保留整个产物目录，不能只复制 EXE。`OpenLR2_x64.exe` 保留经典独立入口。前端设置位于 EXE 同级的 `userdata/settings.toml`，客户端数据库、界面设置及日志也保存在 `userdata/`；可重建缓存位于 `cache/`。运行目录固定为 EXE 所在目录，目录可写时无需管理员权限。详细功能见 [桌面前端](../development/client.md)。

首次正常启动从旧 AppData 位置复制已有前端设置、客户端数据库及持久资源；目标已有文件不被替换，旧数据保留。迁移完成后记录 `userdata/migration-complete.txt`，后续启动使用便携式目录。迁移前关闭其他客户端实例；重新构建保留 `userdata/` 和 `LR2files/`。

全部语言资源集中于 `out/app/Localization/<culture>/`。图形、音频、视频、数据库等第三方原生依赖与 OpenLR2 保持外置，不启用原生库自解压；.NET 运行时由 SDK 自包含发布流程处理。运行目录不包含开发用 PDB、链接库、生成的 API XML 文档和 ExampleIR 示例 DLL；这些文件保留在 `out/build/`。

前端素材程序集外置于 `out/app/Resources/osu.Game.Resources.dll`，发布时按清单移除已退役模式、开场与官方活动专用素材。字体、翻译及通用界面素材保留。裁剪工具随工程构建，用户仍只需运行 `build.cmd`，不增加构建入口；过程与报告见 [资源包](../development/resource-pack.md)。

## 产物目录

| 路径 | 内容 |
| --- | --- |
| `out/build/client/` | 前端中间文件与开发二进制 |
| `out/build/upstream/` | 按项目隔离的上游编译输出 |
| `out/build/tools/resource-pack/` | 资源裁剪工具及其构建依赖 |
| `out/build/temp/` | 构建工具的临时文件 |
| `out/build/engine/windows-vs-x64/` | CMake 生成工程、C++ 中间文件和库目标 |
| `out/build/engine/windows-vs-x64/RelWithDebInfo/` | 引擎 EXE、FMOD、库、调试符号及 ExampleIR 示例 |
| `out/deps/vcpkg/installed/<preset>/` | 按架构与构建预设隔离的依赖安装结果 |
| `out/deps/vcpkg/` | 依赖下载、编译工作目录、包及二进制缓存 |
| `out/app/` | 完整客户端运行目录及玩家数据 |
| `out/app/userdata/` | 前端配置、数据库、持久资源及日志 |
| `out/app/cache/` | 可重建缓存及临时引擎请求 |
| `out/app/Resources/` | 裁剪后的前端素材程序集 |
| `out/logs/` | `build-lazerrave-*.log` 构建日志 |
| `out/reports/` | 性能记录、隔离诊断副本及本机迁移记录 |

完整构建直接将前端发布到 `out/app/`，再提供同目录的原生引擎与资源，不保留额外 publish 或引擎 package 副本。`out/build/client/bin/` 保留 IDE 调试及增量编译输出，因此其中仍有一套前端程序与运行依赖。

发布成功后，构建脚本根据 `out/build/client/obj/<配置>/net10.0-windows/win-x64/package-cleanup.txt` 清理已合入 EXE 的旧文件和原位置的资源程序集。清理不覆盖 `Resources/`、`Localization/`、`LR2files/`、曲库或前端配置；旧语言文件夹含有其他文件时保留文件夹。

清理项目编译缓存时只移除 `out/build/`，保留 `out/deps/` 可避免重复下载及编译 vcpkg 依赖。DxLib、SQLite 和 TinyXML 是同一 CMake 工程内的库目标，其中间文件仍随引擎工程管理。不要把整个 `out/` 当作可删除缓存：`out/app/` 包含玩家配置、成绩、回放及自行添加的曲目，诊断副本也可能包含需要保留的数据。

## 单独构建 C++ 引擎

```powershell
.\build.cmd -EngineOnly
.\build.cmd -EngineOnly -Jobs 4 -Fresh
.\build.cmd -EngineOnly -PrepareRuntime -WindowWidth 1920 -WindowHeight 1080
.\build.cmd -EngineOnly -UpdateRuntime
```

`build.cmd -EngineOnly` 仅构建 C++ 引擎，默认 x64、RelWithDebInfo、4 个并行任务。`-Fresh` 重新生成 CMake 配置，保留依赖下载缓存；`-DirectDownload` 在构建进程中绕过代理。`-Architecture` 可选择 x64/x86，`-Configuration` 可选择 RelWithDebInfo/Release/Debug，其他配置需独立验证。

`-PrepareRuntime` 从 `res/runtime/` 或 `-RuntimeSource` 复制资源，准备 `out/reports/engine-runtime/x64/RelWithDebInfo/`。该操作会覆盖同名文件，重复执行前需备份运行副本中的个人配置和成绩。`-UpdateRuntime` 仅替换该目录已有的游戏二进制，须先关闭目标游戏；两项参数互斥。普通引擎构建不更新已有运行副本。

运行副本包含 `run-openlr2.cmd`、`run-1024x768.cmd`、`run-1920x1080.cmd` 及窗口设置脚本。也可关闭游戏后直接调用：

```powershell
.\scripts\set-window.ps1 -RuntimeDirectory 'D:\LazerRave' -Width 1920 -Height 1080
```

窗口工具保留 CP932 配置编码并创建首次修改备份。窗口尺寸与皮肤内部绘制尺寸分别处理，扩大窗口不会增加原始素材细节。FMOD 必须与 EXE 架构匹配，旧 `fmodex.dll` 不能替代 `fmod.dll`。ExampleIR 是开发示例，仅保留源码与开发编译输出，不随完整客户端打包。

## ZIP 发布包

执行 `.\build.cmd -PackageZip` 完成 Release 编译，并在 `out/releases/` 生成带版本号的 Windows x64 ZIP 和 SHA-256 校验文件。版本由根目录 `version.props` 维护；当前为 Beta 测试阶段。ZIP 包含客户端、引擎、依赖、语言和默认资源，排除个人数据和私人曲库，使用独立默认配置及清洁的本地 Player 档案。打包需要 Python 3.11 或更高版本。

解压整个目录后运行 `LazerRave.exe`，把自己的曲目放入包内的 `BMS/` 或配置其他曲库目录。完整规则见[版本与发布包](../development/versioning-and-release.md)。

## 调试与故障定位

C# 工程入口为根目录 `LazerRave.slnx`。C++ 可通过 Visual Studio 打开 `src/OpenLR2/CMakeLists.txt`，或打开生成的 `out/build/engine/windows-vs-x64/OpenLR2.sln`。Debug 使用独立目录，当前入口等待调试器连接。

| 现象 | 检查项 |
| --- | --- |
| 下载或还原失败 | 网络、代理、下载源及本地工具版本 |
| CMake 配置失败 | VS C++ 组件、Windows SDK、工具链及旧缓存路径 |
| 缺少依赖或资源 | EXE 所在目录是否保留完整运行包 |
| 启动崩溃 | `userdata/logs/lazer-startup-error.txt`、引擎日志和具体异常；无法写入时尝试运行目录的同名文件 |
| 设置不生效 | 设置页保存状态及 `userdata/logs/settings-save-error.txt` |
| BGA 抖动 | [渲染对比预设与日志](../development/bga-diagnostics.md) |

编译成功不代表实际演奏通过验收。输入、音频、BGA、皮肤、窗口切换和完整演奏由本机人工校验；标准见 [兼容性与质量](../compatibility/quality.md)。
