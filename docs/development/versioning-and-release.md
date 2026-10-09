# 版本与发布包

## 版本来源

桌面客户端版本由仓库根目录 `version.props` 中的 `Version` 维护，当前为 `0.1.0-beta.1`。客户端工程导入该文件，主页底部、程序版本标识、EXE 产品版本与 ZIP 文件名使用同一版本来源。主页用独立的 **BETA** 标识明确当前测试阶段；版本信息持续显示，不受原“你知道吗”提示开关影响。

Windows 的数字程序集版本和文件版本使用 `MAJOR.MINOR.PATCH.0`。完整预发布编号保留在产品版本与程序集信息版本中；构建系统可能追加 Git 提交信息，主页和发布文件名省略 `+` 后的构建元数据。

## 版本号规则

采用 `MAJOR.MINOR.PATCH`，预发布版附加 `-beta.N` 或 `-rc.N`，其中 `N` 为从 1 开始的整数，不使用前导零。

| 字段 | 更新条件 | 示例 |
| --- | --- | --- |
| MAJOR | 正式版发生不兼容的公开接口或持久化数据格式变更 | `1.4.2` → `2.0.0` |
| MINOR | 新增功能；主版本为 0 时也可用于不兼容的开发变更 | `0.1.0-beta.3` → `0.2.0-beta.1` |
| PATCH | 同一功能版本内的修复 | `0.1.0` → `0.1.1-beta.1` |
| beta.N | 当前功能版本的第 N 次公开测试包 | `0.1.0-beta.1` → `0.1.0-beta.2` |
| rc.N | 功能冻结后的候选发布包 | `0.1.0-beta.5` → `0.1.0-rc.1` |
| 无后缀 | 完成该版本验收的正式发布 | `0.1.0-rc.2` → `0.1.0` |

本地编译不自动递增版本号。对外发布不同内容前必须更新版本，已发布的版本不得以同名替换内容。更新 MINOR 时将 PATCH 归零；更新 MAJOR 时将 MINOR、PATCH 归零。当前保持 Beta 阶段，移除后缀需要完成该版本的功能、兼容性和演奏验收。

产品版本不替代引擎桥接、网络协议和回放格式的独立兼容版本。此版本文件仅控制 LazerRave 桌面客户端，不改写 OpenLR2 或云端服务的版本。

## 构建 ZIP

在仓库根目录执行唯一的公开构建入口：

```powershell
.\build.cmd -PackageZip
```

脚本完成 Release 客户端及所需引擎编译，再调用内部 `scripts/package-release.ps1` 创建 ZIP。`-PackageZip` 不能与 `-CompileOnly`、`-Cloud`、`-EngineOnly` 或 Debug 配置组合。指定 `-Destination` 时从该运行目录打包。

默认产物：

```text
out/releases/
  LazerRave-0.1.0-beta.1-win-x64.zip
  LazerRave-0.1.0-beta.1-win-x64.zip.sha256
```

ZIP 使用 Deflate Optimal 压缩，包含一个同名顶层目录，并生成 `VERSION.txt`。校验文件采用 SHA-256。打包前检查 EXE 产品版本、依赖清单和默认皮肤引用，缺少必要文件或版本不一致时失败，不启动游戏。清洁 OpenLR2 本地档案由 Python 3.11 及以上版本的标准库按引擎源码中的表结构生成，不读取现有成绩库。

## GitHub Releases

将版本对应的源码提交后，以 `v<version>` 标签创建 [GitHub Release](https://github.com/feecat/LazerRave/releases)，上传 ZIP 和同名 `.sha256` 文件；Beta、RC 版本标为 Pre-release。标签必须对应实际构建源码，附件保留脚本生成的文件名，例如 `LazerRave-0.1.0-beta.1-win-x64.zip`。

网站 [下载页](https://lazerrave.com/download) 自动读取公开 Release 和其 Windows x64 ZIP 附件，支持预发布版，并展示最近发布的可下载版本。只有源码归档、草稿或没有匹配 ZIP 的 Release 不显示为客户端下载包。暂无公开附件时，页面显示未发布状态；上传后无需重新部署网页。

## 发布包内容

| 包含 | 来源 |
| --- | --- |
| LazerRave、OpenLR2、FMOD 与原生 DLL | 完整 Release 输出；前端 DLL 由构建依赖清单确定 |
| 前端资源、语言文件 | `Resources/`、`Localization/` |
| LR2 默认皮肤和配套音效、视频、鼠标素材 | 输出目录中的 `LR2files/Theme/LR2/`、`Bgm/`、`Sound/`、`Movie/`、`Mouse/` |
| 默认配置与曲库入口 | `res/release/`；相对目录 `BMS/`、`Shared/` |
| 初始本地 Player 档案 | 重新生成的空成绩库，空密码，不含云端账号 |
| 项目与第三方许可证 | 根目录 LICENSE、osu!lazer LICENCE、`res/licenses/` |

不复制已有 `userdata/`、`cache/`、日志、截图、回放、成绩库、曲库、Shared 下载内容、个人皮肤配置、绝对曲库路径或备份文件。发布包仅新增默认 `userdata/settings.toml` 和清洁的 `LR2files/Database/Score/Player.db`。本机运行目录和现有玩家数据不受 ZIP 打包影响。

解压整个目录到可写位置，双击 `LazerRave.exe`。包内不提供曲目，将合法持有的 BMS 曲目放入 `BMS/`，或在设置中指定其他曲库。第三方素材和中间件继续遵循其许可证；本地生成 ZIP 不改变已有的分发授权条件。
