# 仓库结构

LazerRave 的活动代码位于本仓库。C# 客户端与 C++ 引擎分别构建，使用同一运行目录和曲库设置。

| 路径 | 职责 | Git |
| --- | --- | --- |
| `src/LazerRave/lazer/` | 桌面入口、BMS 数据适配、设置与文本 | 纳入 |
| `src/LazerRave/engine-bridge/` | XML 协议、进程、媒体与原生视口 | 纳入 |
| `src/LazerRave/resource-pack/` | 发布时资源裁剪工具与移除清单 | 纳入 |
| `src/osu-lazer/` | 上游客户端、Mania、编辑基础和许可 | 纳入 |
| `src/OpenLR2/` | 演奏引擎、CMake、第三方依赖与 ExampleIR | 纳入 |
| `scripts/` | 构建、打包、诊断及独立检查工具 | 纳入 |
| `docs/` | 当前开发文档、接口标准与路线图 | 纳入 |
| `res/licenses/` | 第三方素材、字体许可及随源码纳入的组件声明 | 纳入 |
| `res/runtime/` | 私有 LR2 皮肤与运行资源 | 忽略 |
| `res/library/BMS/` | 本机曲库 | 忽略 |
| `.tools/` | .NET、CMake、NASM、Ninja 与 vcpkg | 忽略 |
| `out/build/client/` | C# 客户端开发输出及中间文件 | 忽略 |
| `out/build/upstream/` | 按项目隔离的上游 C# 输出及中间文件 | 忽略 |
| `out/build/tools/resource-pack/` | 资源裁剪工具的编译输出及中间文件 | 忽略 |
| `out/build/engine/windows-vs-*/` | CMake 工程、C++ 输出与库目标 | 忽略 |
| `out/deps/vcpkg/` | 依赖下载、编译、安装及二进制缓存 | 忽略 |
| `out/app/` | 完整客户端运行目录及玩家数据 | 忽略 |
| `out/app/Localization/` | 按文化名称归档的全部语言卫星程序集 | 忽略 |
| `out/app/Resources/` | 裁剪后的前端素材程序集 | 忽略 |
| `out/logs/` | 构建日志 | 忽略 |
| `out/reports/` | 性能记录、隔离诊断副本及本机迁移记录 | 忽略 |
| `out/site/` | MkDocs 静态输出 | 忽略 |

根目录的 `LazerRave.slnx` 是 C# 开发入口。`build.cmd` 是唯一构建入口，默认构建完整客户端，添加 `-EngineOnly` 可单独构建引擎；具体实现放在 `scripts/` 中。使用方式见 [构建与运行](../getting-started/build-and-run.md)。

完整客户端直接发布到 `out/app/`，开发输出留在 `out/build/`。运行目录被占用时停止更新，不再生成多个自动编号的运行包。清理项目缓存限于 `out/build/`；依赖缓存、玩家数据及诊断记录分别保留。

运行包中的应用代码与一般托管依赖合入 `LazerRave.exe`，素材程序集外置于 `Resources/`，原生 DLL 保持外置。语言资源集中于 `Localization/`。调试符号、API XML 文档与 ExampleIR 示例只保留在开发构建目录中。

## 上游边界

osu!lazer 快照保留菜单、选曲、设置、资料、新闻、更新日志、在线搜谱及未来在线功能所需的通用组件。Mania 提供元数据和编辑工具，BMS 演奏仍由 OpenLR2 执行。

osu!、Taiko、Catch、首次运行向导、皮肤布局编辑器和官方活动界面已裁剪。Kudosu、每日挑战资料、制谱订阅数、supporter 标识及无调用的专用请求已移除。编辑基础已恢复，BMS 写回与官网谱面上传仍未开放。

版本、许可及上游修改记录位于 `src/osu-lazer/LAZERRAVE-UPSTREAM.md`。第三方库保持独立名称与许可，不随应用名称改写其身份。

## 数据与迁移

前端设置为运行目录的 `userdata/settings.toml`；客户端配置、数据库、持久资源及日志统一位于 `userdata/`，可重建缓存位于 `cache/`。首次正常启动复制旧 AppData 数据，保留原文件且不覆盖目标已有文件。演奏配置、成绩与旧回放位于运行目录的 `LR2files/`。

迁移工作目录时，应同时保留私人运行资源、实际使用的运行包数据、前端配置和客户端资料。绝对曲库及头像路径需要映射到新位置；外部磁盘路径保持原值。曲库索引在同步时更新，成绩仍按谱面身份关联。

生成的 CMake 与 .NET 中间文件依赖工作目录，迁移后重新生成。工具下载和已安装原生依赖可以复用。运行包可能包含玩家成绩和自定义皮肤，不能按普通编译缓存直接删除。

旧原型、清理前源码和迁移记录保存在仓库外归档或忽略目录。活动源码及构建不依赖这些归档。

本快照相对上游 OpenLR2 删除了只服务 Linux 开发构建的 `dep/dxlib-for-linux/` 及其在 `CMakeLists.txt` 中的 `LINUX` 分支；项目只面向 Windows 10/11，且未提供 Linux 配置预设。`dep/DxLib/insufficient_include/` 必须保留：`DxUseCLibOgg.h` 在未定义 `DX_NON_OGGVORBIS` 与 `DX_NON_OGGTHEORA` 时会直接包含其中的 `os.h` 与 `misc.h`，vcpkg 的 libvorbis 不提供这两个内部头。上游 tinyxml 的 Doxygen 生成 HTML 与裁剪后遗留的空目录已一并移除。
