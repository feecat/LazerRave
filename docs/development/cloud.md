# 云端网站与服务

云端代码位于 `src/Cloud/`。网站使用 React、TypeScript 和 Vite；服务端使用 ASP.NET Core 10、SignalR 与 PostgreSQL。网站与 API 由同一服务发布，生产域名为 `lazerrave.com`。服务端可在 Linux 运行，与 Windows 桌面及 OpenLR2 的构建相互独立。

## 实现范围

| 模块 | 当前实现 |
| --- | --- |
| 账户 | 注册、登录、登出、可撤销会话、禁用账户；管理员由运维命令授予 |
| 个人页面 | 显示名、头像、签名、个人介绍、最近提交成绩 |
| Internet Ranking | 按原始谱面 SHA-256、规则、排列和判定槽分组；每位玩家保留该分组中的最佳记录；分页查询 |
| 曲包 | 管理员上传 ZIP、草稿与发布切换、谱面目录、摘要、支持范围请求的 ZIP 下载 |
| 管理后台 | 数量概览、曲包管理、禁用与恢复用户、操作记录 |
| 多人 | 最多 16 人房间、房主转移、选曲、内容确认、准备、服务器倒计时、实时统计、结果持久化 |
| 聊天 | 已登录用户的大厅与房间聊天、最近记录、发送限流及七天保留 |

网站使用自有标识与青蓝色配色，采用简化社区导航、资料卡、曲包目录和排行榜布局，不复制 osu! 官方素材或账户业务。

当前代码未部署到购买的服务器。桌面客户端尚未接入云端登录、联网开局、实时分数或云端回放；网页可以管理房间及聊天，不承担 BMS 演奏。浏览器中的准备操作为用户确认，不能证明本地资源已经通过游戏引擎校验。

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

密码使用 ASP.NET Core Identity 的带盐 PBKDF2 哈希，当前迭代次数为 210,000。会话令牌为 256 位随机数，数据库只保存 SHA-256 摘要，默认有效期七天。网页使用 HttpOnly Cookie，桌面 API 使用 Bearer；网页不把会话令牌保存至 localStorage。

修改接口要求 `X-LazerRave: 1`，或使用 Bearer。服务端检查浏览器 Origin，包括实时连接；管理操作另行验证数据库中的管理员身份。数据库与 API 容器不公开端口，Caddy 为唯一公网入口。生产日志不记录认证请求正文或令牌。

| 接口 | 用途 |
| --- | --- |
| `POST /api/auth/register` | 注册并建立网页会话 |
| `POST /api/auth/login`、`POST /api/auth/logout` | 网页登录与撤销当前会话 |
| `POST /api/auth/token` | 已有账号获取桌面 Bearer 会话 |
| `GET /api/me`、`PUT /api/me` | 读取会话与修改本人资料 |
| `POST /api/me/avatar` | PNG/JPEG 头像上传，最多 1 MiB |
| `GET /api/users/{username}` | 公开资料与最近成绩 |
| `GET /api/packs`、`GET /api/packs/{id}` | 已发布曲包与谱面 |
| `GET /api/packs/{id}/download` | 已发布曲包 ZIP，支持 Range |
| `GET /api/charts`、`GET /api/charts/{id}` | 搜索、键型筛选、分页与单谱面身份 |
| `POST /api/scores`、`GET /api/rankings/{chartId}` | 成绩接收与分组排名 |
| `GET /api/rooms`、`GET /api/chat/{channel}` | 房间列表及已授权聊天记录 |
| `/api/admin/*` | 概览、上传、发布、用户状态与操作记录 |

HTTP 数据采用版本明确的结构化接口。客户端需检查状态码与 `error` 字段，不能将网页页面解析作为游戏协议。

邮箱目前用于登录及唯一性检查，尚无邮件验证、密码找回、双因素认证或 Steam 身份接入。开放注册前需要确定邮件投递与管理员安全策略。

## 排名规则

当前规则标识为 `openlr2-v1`，EX SCORE 为 `PERFECT × 2 + GREAT`。同一玩家在相同谱面、规则、排列、判定槽分组内，依次按 EX SCORE、BAD+POOR、最大连击、提交时间和成绩 ID 选出最佳记录；排行榜使用相同顺序，序号稳定。

客户端提交携带唯一 `clientRunId`。相同标识和相同内容的重试返回原成绩，相同标识的不同内容被拒绝。自动演奏、辅助和 MANIAC 扩展不纳入此提交路径。SHA-256 为主身份，MD5 仅为旧 LR2 数据关联保留。

所有新成绩均为未验证提交；客户端不能自行设置 `verified`。`verified=true` 查询只返回未来校验任务确认的记录。当前服务没有回放重算，数值与身份校验不能证明成绩真实性；公网正式排名需要后续接入受控 OpenLR2 回放校验。实时房间快照同样属于临时结果，不自动写入 Internet Ranking。

ZIP 中的谱面头部解析仅用于目录展示，采用 UTF-8/BOM 或 CP932；不实现完整 BMS 判定、随机时间线、BMSON 或资源引用验证。这些内容仍由游戏后端及后续校验任务处理。

## 实时协议

SignalR 入口为 `/hubs/realtime`，协议首版为 1。网页通过 Cookie 认证；原生客户端使用 Authorization Header。用户身份由会话绑定，不接受消息自报的用户 ID。

| 调用 | 参数 |
| --- | --- |
| `CreateRoom` | 房间名称 |
| `JoinRoom`、`LeaveRoom` | 房间 ID；离开无参数 |
| `SelectChart` | 谱面 ID、已发布曲包 ID、房间版本 |
| `SetReady` | 准备状态、谱面 SHA-256、房间版本 |
| `StartRound` | 房间版本，限房主且全员已准备 |
| `ReportProgress`、`FinishRound` | 对局 ID、单调序号、EX SCORE、连击、misses、0–1 进度 |
| `SendChat` | `lobby` 或当前房间 UUID，以及纯文本消息 |
| `Ping` | 客户端时间戳；响应包含服务器毫秒时间及协议版本 |

服务端广播 `RoomsChanged`、`RoomUpdated` 和 `ChatMessage`。房间状态为 lobby、countdown、playing、results；首版约定三秒倒计时。选曲、成员变化使准备状态失效，倒计时断线取消开局，进行中的对局限制新成员加入。断线后重新连接并重新加入房间；首版不保留断线席位。

统计快照约以 10 Hz 接收和广播，序号、对局身份、单调分数与进度均检查。每连接控制调用和聊天分别限流，连接缓冲受限。房间采用单进程内存状态，完成结果进入 PostgreSQL；重启不恢复活动房间。多实例路由、重连续局、规则组合、随机种子和游戏加载确认属于下一阶段。

房主临时曲包上传与仅房间成员下载的内容授权尚未实现。当前选曲使用管理员发布的曲包，不将临时分享内容自动公开。

## 检查

```powershell
.\.tools\dotnet\dotnet.exe test src/Cloud/tests/Cloud.Tests.csproj -c Release
```

测试覆盖权限与版本、16 人容量、准备与倒计时、过期统计、ZIP 路径与配额、成绩资格。网站的 `tests/integration.mjs` 对指定 loopback 地址和 `lazerrave_test` 数据库执行真实 HTTP/SignalR 检查，会创建测试账户、曲包和成绩，禁止用于正式数据库。它需要 `LAZERRAVE_CLOUD_TEST_URL`、`LAZERRAVE_CLOUD_TEST_ZIP`、`LAZERRAVE_CLOUD_TEST_DOTNET`、`LAZERRAVE_CLOUD_TEST_SERVER` 及测试连接环境。网页检查另见 `tests/ui.spec.ts`。

生产部署入口、容量边界及备份见 [云端部署](../operations/cloud-deployment.md)。

## 技术参考

- [ASP.NET Core SignalR 认证](https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz)
- [ASP.NET Core 密码哈希](https://learn.microsoft.com/aspnet/core/security/data-protection/consumer-apis/password-hashing)
- [Npgsql 数据源](https://www.npgsql.org/doc/basic-usage.html)
- [Vite 构建](https://vite.dev/guide/)
