# LazerRave 文档

LazerRave 使用 C# 桌面客户端管理 BMS 曲库与演奏设置，由 C++ OpenLR2 执行游戏。当前平台为 Windows 10/11 x64。文档分别说明现有实现、接口边界和后续交付要求。

## 开始使用

- [构建与运行](getting-started/build-and-run.md)：环境、编译入口、运行包和故障定位。
- [仓库结构](development/project-layout.md)：源码、工具链、个人数据与生成产物。
- [客户端](development/client.md)：曲库、设置、演奏方式和上游组件复用。

## 当前实现与规划

| 能力 | 状态 |
| --- | --- |
| BMS 层级曲库、筛选、预览和成绩读取 | 已接入 |
| 参数传递、直达演奏、同窗口与独立窗口 | 已接入 |
| 普通鼠标键盘、显示设置与多语言资源 | 现有组件保留，兼容矩阵持续完善 |
| 谱面编辑 | 基础框架保留，BMS 读写未接入 |
| 新闻、更新日志、在线搜谱 | 界面和请求基础保留，自有服务未接入 |
| 用户、头像、签名、多人和在线排行榜 | 独立云端网站与服务首版已实现，桌面联网、可信排名与实际同步演奏未接入 |
| 云端回放、BMSON、段位认证、Steam | 规划中 |
| 无既有 LR2 安装的独立发行包 | 默认资源与兼容验收尚未完成 |

## 开发与架构

- [引擎桥接](development/engine-bridge.md)、[BGA 与帧时间](development/bga-diagnostics.md)、[验证范围](development/verification.md)。
- [前端资源包](development/resource-pack.md)：外置路径、裁剪策略及构建记录。
- [云端网站与服务](development/cloud.md)、[LazerRave.com 部署](operations/cloud-deployment.md)。
- [技术架构](architecture/overview.md)、[多人协议与回放](architecture/multiplayer-and-replay.md)。
- [开发路线图](roadmap.md)、[服务器容量](operations/server-capacity.md)。

## 兼容性与维护

- [质量标准](compatibility/quality.md)、[BMS 扩展](compatibility/bms.md)、[LR2 皮肤扩展](compatibility/lr2-skins.md)。
- [文档维护](development/documentation.md)、[文档索引](contents.md)。

编译结果、无窗口数据检查和实际游玩验收分别记录。前端帧率不代表引擎或 BGA 的显示帧率。
