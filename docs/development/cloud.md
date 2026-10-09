# 云端网站与服务

云端代码位于 `src/Cloud/`。网站使用 React、TypeScript 和 Vite；服务端使用 ASP.NET Core 10、SignalR 与 PostgreSQL。网站与 API 由同一服务发布，生产域名为 `lazerrave.com`。服务端可在 Linux 运行，与 Windows 桌面及 OpenLR2 的构建相互独立。

## 实现范围

| 模块 | 当前实现 |
| --- | --- |
| 账户 | 独立数字 UID、注册、登录、登出、可撤销会话、禁用账户；管理员由运维命令授予 |
| 个人页面 | 显示名、头像、签名、个人介绍、最近提交成绩 |
| Internet Ranking | 按原始谱面 SHA-256、规则、排列和判定槽分组；每位玩家保留该分组中的最佳记录；分页查询 |
| 曲包 | 管理员上传 ZIP、草稿与发布切换、谱面目录、摘要、支持范围请求的 ZIP 下载 |
| 难度表 | 管理员导入标准 BMS data 文件、草稿与发布、按等级浏览、MD5 匹配站内谱面、标准表头与条目导出 |
| 管理后台 | 数量概览、曲包管理、禁用与恢复用户、操作记录 |
| 多人 | 最多 16 人房间、房主转移、已发布及本地谱面选曲、资源确认、准备、服务器倒计时、实时统计、结果持久化 |
| 临时曲目 | 房主 8 MiB 分块上传、ZIP 与资源清单校验、成员授权下载和续传、校验完成后固定 2 小时到期、启动及每分钟自动清理 |
| 聊天 | 已登录用户的大厅与房间聊天、最近记录、发送限流及七天保留 |

网站使用自有标识与青蓝色配色，采用简化社区导航、资料卡、曲包目录和排行榜布局，不复制 osu! 官方素材或账户业务。

初始云端服务已于 2026-10-09 部署到 [lazerrave.com](https://lazerrave.com)，HTTPS、数据库迁移和公开读取接口已验证。桌面客户端已接入登录、房间创建与加入、本地选曲、房主上传、显式下载确认、进度、Shared 安装及增量曲库导入；尚未接入同步演奏开局、游戏实时分数或云端回放。网页可以管理房间及聊天，不承担 BMS 演奏。浏览器中的公开曲包准备操作为用户确认；本地临时谱面的资源确认由桌面客户端执行，网页不能代替。

## 桌面曲目分享

主菜单的多人入口或选曲页面右上角的 Multiplayer 按钮打开联机面板。服务器默认为 `https://lazerrave.com`，支持配置其他 HTTPS 地址；开发时允许 localhost 的 HTTP。域名尚未解析时可使用本机测试服务。服务器地址与用户名保存在 `userdata/settings.toml` 的 cloud 表中，密码及会话令牌不保存。

房主创建房间后，从现有层级曲库选择歌曲与难度，在联机面板点击 Select current song and difficulty。成员自动按谱面及资源内容检查本地文件；有缺曲成员时，房主点击 Upload and share song，后台使用最小体积 ZIP 压缩并上传。已经校验且未到期的同歌内容可以在同一房间复用，不刷新到期时间。

缺曲成员点击 Download song / continue 后才开始下载。面板显示阶段、百分比、大小、速度和预计剩余时间，支持取消后继续；换曲取消旧传输且不自动下载新曲。校验及解包完成后，歌曲按内容摘要安装到 `LazerRave.exe` 同级的 `Shared`，OpenLR2 仅导入该歌曲目录并返回更新后的目录，保留现有曲库及歌曲多难度分组。`.incoming` 暂存目录不进入曲库。服务端到期清理不删除本地已安装曲目。

同一账户当前只允许一条实时连接；使用桌面客户端联机前需关闭网站实时连接。Shared 的自动磁盘回收和游戏后端加载确认仍待完善，本地歌曲可通过文件管理器清理。

## 构建

本机需要已准备的 .NET SDK 10.0.401 和 Node.js 24。服务端依赖使用 NuGet 锁文件，网站使用 `package-lock.json`。

```powershell
.\build.cmd -Cloud
```

`build.cmd` 仍为唯一构建入口，默认行为继续构建桌面客户端。`-Cloud` 构建网站并发布服务端，产物为 `out/cloud/`；不会启动服务、安装 Docker 或修改桌面运行包。只检查编译可使用 `-Cloud -CompileOnly`。

| 路径 | 内容 |
| --- | --- |
| `out/build/cloud/` | 服务端、测试及网站中间产物 |
| `out/cloud/server/` | 依赖 .NET 10 运行时的服务发布包 |
| `out/cloud/server/wwwroot/` | 编译后的 React 网站与应用标识 |
| `out/cloud/compose.yaml` | PostgreSQL、API、Caddy 的部署配置 |
| `out/cloud/.env.example` | 域名、证书邮箱与数据库密码示例 |

运行数据置于数据库及独立内容卷，不放入应用发布目录。构建不会生成测试账户、默认管理员或示例成绩。

## 本地开发

先准备独立 PostgreSQL 17 数据库。以下连接仅为本机示例，用户名与密码应按本机数据库调整：

```powershell
$env:DOTNET_ROOT = "$PWD\.tools\dotnet"
$env:ConnectionStrings__Postgres = 'Host=127.0.0.1;Database=lazerrave_dev;Username=lazerrave;Password=YOUR_LOCAL_PASSWORD;Maximum Pool Size=10'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5080'
$env:Cloud__PublicOrigin = 'http://localhost:5080'
$env:Cloud__SecureCookies = 'false'
$env:Cloud__StoragePath = "$PWD\out\reports\cloud-dev\content"
.\.tools\dotnet\dotnet.exe .\out\cloud\server\Cloud.dll
```

打开 `http://localhost:5080` 查看发布网站。服务启动时执行版本化 SQL 迁移；重启将尚未完成的对局标记为中断。数据库迁移和管理员命令不启动 HTTP 服务，也不打断已有对局。

修改网页时，可在 `src/Cloud/web/` 执行 `npm run dev`，默认 Vite 地址为 `http://localhost:5173`，API 与 SignalR 代理到 5080。此时将服务端 `Cloud__PublicOrigin` 改为 `http://localhost:5173`，保持浏览器来源一致。开发模式的 HTTP Cookie 仅用于本机，生产配置强制 HTTPS 与 Secure Cookie。

## 认证与权限

网站导航栏提供 English / 简体中文语言下拉框。选择后即时更新界面，以 `lazerrave.language` 保存到浏览器 localStorage，刷新、重新打开网页与退出账号后继续生效；同源标签页同步语言选择。默认英语。浏览器禁用本地存储时，当前页面仍可切换语言。界面、常用提示与日期按所选语言显示，曲名、玩家资料、聊天内容与协议参数保持原始数据。

密码使用 ASP.NET Core Identity 的带盐 PBKDF2 哈希，当前迭代次数为 210,000。会话令牌为 256 位随机数，数据库只保存 SHA-256 摘要，默认有效期七天。网页使用 HttpOnly Cookie，桌面 API 使用 Bearer；网页不把会话令牌保存至 localStorage。

修改接口要求 `X-LazerRave: 1`，或使用 Bearer。服务端检查浏览器 Origin，包括实时连接；管理操作另行验证数据库中的管理员身份。数据库与 API 容器不公开端口，Caddy 为唯一公网入口。生产日志不记录认证请求正文或令牌。

| 接口 | 用途 |
| --- | --- |
| `POST /api/auth/register` | 注册并建立网页会话 |
| `POST /api/auth/login`、`POST /api/auth/logout` | 网页登录与撤销当前会话 |
| `POST /api/auth/token` | 已有账号获取桌面 Bearer 会话 |
| `POST /api/auth/password` | 已登录用户提交 `currentPassword` 与 `newPassword`，修改密码并撤销全部现有会话 |
| `GET /api/me`、`PUT /api/me` | 读取会话与修改本人资料 |
| `POST /api/me/avatar` | PNG/JPEG 头像上传，最多 1 MiB |
| `GET /api/users/{username}` | 公开资料与最近成绩 |
| `GET /api/packs`、`GET /api/packs/{id}` | 已发布曲包与谱面 |
| `GET /api/packs/{id}/download` | 已发布曲包 ZIP，支持 Range |
| `GET /api/charts`、`GET /api/charts/{id}` | 搜索、键型筛选、分页与单谱面身份 |
| `POST /api/scores`、`GET /api/rankings/{chartId}` | 成绩接收与分组排名 |
| `GET /api/rooms`、`GET /api/chat/{channel}` | 房间列表及已授权聊天记录 |
| `POST /api/rooms/{id}/content` | 房主创建或恢复绑定当前选曲的临时上传 |
| `GET /api/room-content/{id}` | 房间成员读取上传状态、资源清单与固定到期时间 |
| `PUT /api/room-content/{id}?offset=…` | 房主按当前偏移上传不超过 8 MiB 的原始 ZIP 分块 |
| `POST /api/room-content/{id}/complete` | 校验 ZIP 和全部清单资源、开放下载；重复完成不延长有效期 |
| `GET /api/room-content/{id}/download` | 当前房间成员授权 ZIP 下载，支持 Range，到期返回 410 |
| `/api/admin/*` | 概览、上传、发布、用户状态与操作记录 |

HTTP 数据采用版本明确的结构化接口。客户端需检查状态码与 `error` 字段，不能将网页页面解析作为游戏协议。

数字 UID、个人最佳聚合、LR2IR 风格排名字段与难度表接口详见 [排名与难度表](../architecture/internet-ranking.md)。课程成绩已建立数据库结构，当前没有课程演奏和成绩提交入口。

账号密码正确即可登录，不要求邮件验证。邮箱用于登录及唯一性检查。个人页面的 Change password 入口要求当前密码，新密码需为 12–128 个字符；修改成功后网页 Cookie 与桌面 Bearer 会话全部失效，需要重新登录。密码找回、双因素认证与 Steam 身份接入尚未实现。

## 排名规则

当前规则标识为 `openlr2-v1`，EX SCORE 为 `PERFECT × 2 + GREAT`。同一玩家在相同谱面、规则、排列、判定槽分组内，依次按 EX SCORE、BAD+POOR、最大连击、提交时间和成绩 ID 选出最佳记录；排行榜使用相同顺序，序号稳定。

客户端提交携带唯一 `clientRunId`。相同标识和相同内容的重试返回原成绩，相同标识的不同内容被拒绝。自动演奏、辅助和 MANIAC 扩展不纳入此提交路径。SHA-256 为主身份，MD5 仅为旧 LR2 数据关联保留。

所有新成绩均为未验证提交；客户端不能自行设置 `verified`。`verified=true` 查询只返回未来校验任务确认的记录。当前服务没有回放重算，数值与身份校验不能证明成绩真实性；公网正式排名需要后续接入受控 OpenLR2 回放校验。实时房间快照同样属于临时结果，不自动写入 Internet Ranking。

ZIP 中的谱面头部解析仅用于目录展示，采用 UTF-8/BOM 或 CP932；不实现完整 BMS 判定、随机时间线、BMSON 或资源引用验证。这些内容仍由游戏后端及后续校验任务处理。

## 实时协议

SignalR 入口为 `/hubs/realtime`，协议首版为 1。网页通过 Cookie 认证；原生客户端使用 Authorization Header。用户身份由会话绑定，不接受消息自报的用户 ID。

每个账号最多允许四条实时连接，网站与桌面客户端可同时在线；全服务器最多允许 80 条连接。房间成员仍按账号去重。关闭旁观的网站连接不影响桌面房间成员，建立房间成员的连接断开时则退出房间。

桌面登录后将云端显示名、UID 和头像同步至工具栏与曲库个人信息。头像仅从当前登录服务器的对应用户接口加载，支持 UID 1；退出登录后恢复本地个人信息。

| 调用 | 参数 |
| --- | --- |
| `CreateRoom` | 房间名称 |
| `JoinRoom`、`LeaveRoom` | 房间 ID；离开无参数 |
| `SelectChart` | 谱面 ID、已发布曲包 ID、房间版本 |
| `SelectLocalChart` | 本地谱面及资源摘要、标题、作者、键型与等级，以及房间版本 |
| `ReportContent` | 独立选曲标识、资源清单摘要、missing/downloading/available 状态 |
| `SetReady` | 准备状态、谱面 SHA-256、房间版本 |
| `StartRound` | 房间版本，限房主且全员已准备 |
| `ReportProgress`、`FinishRound` | 对局 ID、单调序号、EX SCORE、连击、misses、0–1 进度 |
| `SendChat` | `lobby` 或当前房间 UUID，以及纯文本消息 |
| `Ping` | 客户端时间戳；响应包含服务器毫秒时间及协议版本 |

服务端广播 `RoomsChanged`、`RoomUpdated` 和 `ChatMessage`。房间状态为 lobby、countdown、playing、results；首版约定三秒倒计时。选曲、成员变化使准备状态失效，倒计时断线取消开局，进行中的对局限制新成员加入。断线后重新连接并重新加入房间；首版不保留断线席位。

统计快照约以 10 Hz 接收和广播，序号、对局身份、单调分数与进度均检查。每连接控制调用和聊天分别限流，连接缓冲受限。房间采用单进程内存状态，完成结果进入 PostgreSQL；重启不恢复活动房间。多实例路由、重连续局、规则组合、随机种子和游戏加载确认属于下一阶段。

房主临时上传与成员授权下载已实现，不将临时分享内容自动公开。上传绑定独立的选曲标识，成员准备和加入引起的普通房间版本变化不打断同曲上传；换曲或失去房主身份会拒绝旧上传。临时 ZIP 从校验完成起固定保留 2 小时，到期关闭下载并自动清理，不因房间引用或服务重启延长；服务器清理不删除客户端已安装曲目。`--cleanup-content` 可显式执行过期清理。完整协议、配额和缓存边界见 [多人游戏与曲目分发](../architecture/multiplayer-and-replay.md)。

## 检查

```powershell
.\.tools\dotnet\dotnet.exe test src/Cloud/tests/Cloud.Tests.csproj -c Release
```

测试覆盖权限与版本、16 人容量、准备与倒计时、过期统计、ZIP 路径与配额、资源清单、歌曲安装及成绩资格。`CloudClientTransferTests` 在设置独立 loopback 测试环境时运行两个实际桌面网络客户端，无图形窗口，检查显式下载、进度、上传与下载取消续传、同歌难度复用和固定到期、未授权访问、服务端过期删除及本地文件保留；未提供测试环境时明确跳过。需要 `LAZERRAVE_CLOUD_TEST_URL`、`LAZERRAVE_CLOUD_TEST_DOTNET`、`LAZERRAVE_CLOUD_TEST_SERVER`、`ConnectionStrings__Postgres` 及独立 `Cloud__StoragePath`，数据库名称必须含 `lazerrave_test`。

网站的 `tests/integration.mjs` 对指定 loopback 地址和 `lazerrave_test` 数据库执行真实 HTTP/SignalR 检查，会创建测试账户、曲包和成绩，禁止用于正式数据库。它另外需要 `LAZERRAVE_CLOUD_TEST_ZIP`。网页检查另见 `tests/ui.spec.ts`。

生产部署入口、容量边界及备份见 [云端部署](../operations/cloud-deployment.md)。

## 技术参考

- [ASP.NET Core SignalR 认证](https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz)
- [ASP.NET Core 密码哈希](https://learn.microsoft.com/aspnet/core/security/data-protection/consumer-apis/password-hashing)
- [Npgsql 数据源](https://www.npgsql.org/doc/basic-usage.html)
- [Vite 构建](https://vite.dev/guide/)
