# LazerRave 文档

LazerRave 面向 Windows 10/11 x64，使用 C# / osu!lazer 桌面界面与 C++ OpenLR2 演奏引擎。当前为 Beta 阶段，Windows 发布包提供完整运行依赖和默认资源。

## 使用与开发

- [下载发布包](https://github.com/feecat/LazerRave/releases)：完整解压后运行 `LazerRave.exe`。
- [构建与运行](getting-started/build-and-run.md)：开发环境、构建命令和故障定位。
- [桌面客户端](development/client.md)：曲库、设置、成绩、回放及演奏方式。
- [版本与发布包](development/versioning-and-release.md)：版本规则、ZIP 内容和发布流程。

## 功能状态

| 范围 | 当前实现 | 后续工作 |
| --- | --- | --- |
| 本地游玩 | 层级曲库、搜索、难度筛选、直达演奏、经典 LR2 | 兼容样本、性能与显示缩放验收 |
| 成绩与回放 | 本地记录、最高 EX SCORE、LR2 判定与回放完整性检查 | 云端回放与重算验证 |
| 在线服务 | 账户、自动登录、IR、难度表、曲包浏览与下载 | 可信成绩、恢复流程与容量验证 |
| 多人游戏 | 16 人房间、选曲与准备、实时排行、结果、房主管理、临时分享 | 游戏加载屏障、同步精度与多机压力测试 |
| 本地化 | 客户端主要文本中英日，网站中英切换 | 完整翻译与 DPI 矩阵 |
| 格式与编辑 | BMS 系列、旧 LR2 皮肤、编辑器基础 | BMSON、BMS 编辑、独立课程认证 |

已接入不等于完成全部场景验收。完整曲目预览的音频行为仍待人工验证；具体边界见 [验证范围](development/verification.md) 和 [路线图](roadmap.md)。

## 技术参考

- [仓库结构](development/project-layout.md)、[技术栈与模块](architecture/overview.md)、[引擎桥接](development/engine-bridge.md)。
- [云端服务](development/cloud.md)、[排名与难度表](architecture/internet-ranking.md)、[多人协议与回放](architecture/multiplayer-and-replay.md)。
- [质量标准](compatibility/quality.md)、[BMS 扩展](compatibility/bms.md)、[LR2 皮肤](compatibility/lr2-skins.md)。
- [云端部署](operations/cloud-deployment.md)、[服务器容量](operations/server-capacity.md)。

全部正式页面由 `mkdocs.yml` 导航维护，文档站使用 readthedocs 主题。
