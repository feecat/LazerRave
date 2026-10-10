# 仓库结构与维护

`build.cmd` 是唯一公开构建入口，`LazerRave.slnx` 是 C# 开发入口。客户端与原生引擎分别编译，共用运行目录及曲库设置。

## 源码与资源

| 路径 | 职责 |
| --- | --- |
| `src/LazerRave/lazer/` | 桌面入口、界面、设置、曲库与在线适配 |
| `src/LazerRave/engine-bridge/` | 引擎协议、进程、原生视口、音频预览及回放 |
| `src/LazerRave/resource-pack/` | 发布资源裁剪工具与策略 |
| `src/Cloud/server/` | API、账户、IR、房间、内容服务与 SQL 迁移 |
| `src/Cloud/web/` | React 网站及网页测试 |
| `src/Cloud/protocol/` | 两端共用的曲库、内容、传输与曲名解析代码 |
| `src/Cloud/tests/` | 服务端及部分客户端适配测试 |
| `src/osu-lazer/` | 裁剪后的上游界面、Mania 元数据和编辑基础 |
| `src/OpenLR2/` | C++ 演奏引擎、CMake、ExampleIR 及第三方依赖 |
| `deploy/cloud/` | Docker、Caddy 与部署配置示例 |
| `scripts/` | 构建、打包、维护、诊断与检查工具 |
| `docs/` | 使用、开发、架构、兼容性和运维文档 |
| `res/branding/`、`res/licenses/`、`res/release/` | 品牌、许可及发布默认配置 |

以上目录纳入 Git。`res/runtime/` 保存本机 LR2 运行资源，`res/library/BMS/` 可保存本机曲库，均不纳入 Git。

上游来源与本地改动见 `src/osu-lazer/LAZERRAVE-UPSTREAM.md`。第三方名称、许可及必要的编译配置保持原始身份。DxLib 的 `insufficient_include/` 包含 Windows 构建所需的内部头文件，不能作为其他平台残留直接删除。

## 本地输出与数据

| 路径 | 内容与保留原则 |
| --- | --- |
| `.tools/` | 本地工具链；保留可避免重新下载 |
| `out/build/` | 客户端、上游、云端、工具与引擎的中间文件；可重建 |
| `out/deps/` | 原生依赖下载、编译和安装缓存；与项目输出分开管理 |
| `out/app/` | 当前运行包；可能包含曲库、账号、成绩、配置及回放 |
| `out/cloud/` | 网站与服务部署产物 |
| `out/releases/` | 版本 ZIP 与 SHA-256 校验文件 |
| `out/logs/` | 构建日志 |
| `out/checks/`、`out/reports/` | 测试与诊断结果；副本可能包含个人数据 |
| `out/site/` | MkDocs 静态网站；可重建 |

上述目录均忽略版本控制。开发输出保留独立程序集与调试符号；运行包将一般托管依赖合入 EXE，素材外置于 `Resources/`，语言资源位于 `Localization/`。详细发布内容见 [版本与发布包](versioning-and-release.md)。

运行数据以 EXE 所在目录为基准：`userdata/` 保存前端持久数据，`cache/` 保存可重建缓存，`LR2files/` 保存原生配置、成绩、回放及皮肤，`BMS/Shared/` 保存下载曲目。更新和清理前保留持久数据，不能整体删除 `out/`。

迁移仓库后重新生成 CMake 和 .NET 中间文件；工具及依赖缓存可复用。外部曲库的绝对路径需要单独检查。历史副本和一次性部署记录不参与正常构建。

## 文档维护

根 README 提供项目与开发入口，文档首页维护功能状态，`mkdocs.yml` 维护完整导航。正式文档使用 UTF-8 Markdown、小写连字符文件名；实现说明与目标设计分别标注，不保存 JSON、日志或诊断转储。

```powershell
python -m pip install -r requirements-docs.txt
python -X utf8 -B scripts/check_docs.py
python -m mkdocs serve
python -m mkdocs build --strict
```

文档使用 readthedocs 主题，输出到 `out/site/`。重命名或合并页面时同步导航和相对链接；同一目录不同时提供 README 与 index。开发文档不复制到运行包。诊断副本存放于忽略目录，可复用维护工具放入 `scripts/`。
