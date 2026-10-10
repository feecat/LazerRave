# Windows 构建与运行

所有命令在仓库根目录执行。`build.cmd` 是唯一公开构建入口，构建不启动应用或执行测试。

## 环境

| 项目 | 要求 |
| --- | --- |
| 系统 | Windows 10/11 x64 |
| C++ | Visual Studio 2022、MSVC v143、Desktop development with C++、Windows SDK |
| .NET | SDK 10.0.401，脚本可准备到 `.tools/dotnet/` |
| 工具 | CMake 3.29+、Ninja、NASM、Git、Python 3.11+ |
| 脚本 | Windows PowerShell 5.1；依赖构建需 PowerShell 7 |
| 网络 | 首次下载工具、NuGet 和 vcpkg 依赖时需要 |
| 资源 | 完整运行包需 LR2 配置与皮肤；纯编译不需要运行资源 |

## 构建命令

```powershell
.\build.cmd
.\build.cmd -CompileOnly
.\build.cmd -PackageZip
.\build.cmd -BuildEngine -Jobs 4
.\build.cmd -RuntimeSource 'D:\LR2beta3' -Destination 'D:\LazerRave'
.\build.cmd -Cloud
```

默认构建 Release 客户端与 x64 RelWithDebInfo 引擎，完整运行目录为 `out/app/`。引擎源码变化时自动增量构建，`-BuildEngine` 显式调用原生构建，`-Jobs` 控制 C++ 并行任务数。`-CompileOnly` 不组装运行包；`-Cloud` 独立构建网站与服务器。

默认资源来源是 `res/runtime/`，需包含 `LR2files/Config/config.xml` 与 `LR2files/Theme/`。普通构建补齐缺失资源，保留已有配置、成绩、回放和曲库。更新前自动关闭目标运行目录中的游戏进程；若资源来源本身正在使用，内部资源复制仍可能拒绝执行。

构建日志位于 `out/logs/`，诊断使用英文与 UTF-8。构建期间将临时目录指向 `out/build/temp/` 并检查可写性，结束后恢复原环境。工具与依赖缓存、开发输出和运行数据的分工见 [仓库结构](../development/project-layout.md)。

## 运行与更新

运行 `out/app/LazerRave.exe`，移动时保留完整目录。程序自带 .NET 运行时；`OpenLR2_x64.exe`、FMOD、其他原生 DLL、`Resources/`、`Localization/` 和 `LR2files/` 必须保留。

默认曲库为 EXE 同级的 `BMS/`，不可从设置移除；可添加绝对或相对路径，相对路径以 EXE 所在目录解析。共享及网站曲包安装到 `BMS/Shared/`，原生 LR2 使用相同曲库设置。默认 Game viewport 为 1024×768、同窗口 E 模式、240 FPS；前端默认 1920×1080 窗口，并适应屏幕工作区。

更新前保留 `userdata/`、`BMS/` 与 `LR2files/`。首次正常启动可复制旧 AppData 持久数据，不覆盖目标已有文件；后续使用 EXE 同级数据。窗口、速度、帧率等已有配置继续保留，细节见 [客户端](../development/client.md)。

ZIP 使用独立默认配置及清洁 Player 档案，排除个人账号、成绩、回放与曲库。执行 `build.cmd -PackageZip` 后，从 `out/releases/` 获取 ZIP 与 SHA-256；发布规则见 [版本与发布包](../development/versioning-and-release.md)。

## 单独构建引擎

```powershell
.\build.cmd -EngineOnly
.\build.cmd -EngineOnly -Jobs 4 -Fresh
.\build.cmd -EngineOnly -PrepareRuntime -WindowWidth 1024 -WindowHeight 768
.\build.cmd -EngineOnly -UpdateRuntime
.\scripts\set-window.ps1 -RuntimeDirectory 'D:\LazerRave' -Width 1024 -Height 768
```

`-Fresh` 重新生成 CMake 配置；`-PrepareRuntime` 准备 `out/reports/engine-runtime/x64/RelWithDebInfo/`，会覆盖同名资源，需先备份其中玩家数据；`-UpdateRuntime` 仅更新已准备副本的二进制，两者互斥。引擎调试副本运行前需关闭正在使用的原生游戏。

`set-window.ps1` 修改原生 LR2 配置，保留 CP932 编码及首次修改备份，不设置 C# 前端窗口。皮肤逻辑画布与窗口尺寸独立，扩大窗口不增加素材细节。FMOD 必须与 EXE 架构匹配；ExampleIR 仅作为开发示例。

## 调试与故障定位

C# 使用 `LazerRave.slnx`；C++ 使用 `src/OpenLR2/CMakeLists.txt` 或生成的 `out/build/engine/windows-vs-x64/OpenLR2.sln`。调试符号与独立开发程序集保留在 `out/build/`。

| 现象 | 检查项 |
| --- | --- |
| 下载或还原失败 | 网络、代理、下载源及本地工具版本 |
| CMake 失败 | VS C++ 组件、Windows SDK、工具链和旧缓存路径 |
| 缺少依赖或资源 | 是否完整解压运行包，资源来源是否齐全 |
| 启动崩溃 | `userdata/logs/lazer-startup-error.txt`；无法写入时尝试 EXE 同级同名文件 |
| 设置保存失败 | `userdata/logs/settings-save-error.txt` 与目录写权限 |
| BGA 抖动 | [BGA 与帧时间诊断](../development/bga-diagnostics.md) |

编译通过不替代输入、音频、BGA、皮肤和完整演奏的人工验收。见 [验证范围](../development/verification.md)。
