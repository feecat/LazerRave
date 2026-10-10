# LazerRave.com 部署

初始服务已于 2026-10-09 部署至 [lazerrave.com](https://lazerrave.com)。主机地址为 `64.176.63.104`，系统为 Ubuntu 26.04.1 LTS x86-64，规格为 1 vCPU、约 950 MiB 可用物理内存、32 GB NVMe，已配置约 3 GiB 交换空间。部署目录为 `/opt/lazerrave/`，Compose 项目名为 `lazerrave`。部署包面向 x86-64 Linux；32 位 x86 不适用。

HTTPS 由 Caddy 使用 Let's Encrypt ACME 签发和自动续期，不配置证书通知邮箱。证书与 ACME 账户保存在持久化 `caddy_data` 卷，需保持 80/443 端口和正确的 DNS 解析。首次部署验证了 HTTPS 首页、注册路由、健康检查、公开曲包和房间读取、未登录管理员请求拒绝以及 www 重定向；当前数据库迁移已包含版本 1–6。版本 5 添加可空的普通 SCORE 与统一索引；版本 6 将 IR 与曲包发布分离，并增加排行范围、私密成员和成绩撤销状态。未进行公网 16 人负载或实际曲包传输验收。

## 初始预算

使用单实例 ASP.NET Core、PostgreSQL 17 与 Caddy。前端与服务在开发设备编译，服务器只构建运行时容器镜像，不安装 Node.js 或 .NET SDK。

| 服务 | 容器内存上限 |
| --- | --- |
| API、房间与 ZIP 校验 | 320 MiB |
| PostgreSQL | 256 MiB |
| Caddy | 64 MiB |

上述为限制值，未代表实测峰值或并发承诺；其余内存供系统、Docker 和文件缓存使用。首版最多四个房间、每房 16 人、总计最多 80 个实时连接；1 GiB 主机需要从一个房间的小规模测试开始，不将配置上限视为容量保证。

曲包采用服务器磁盘内容卷，单次上传并发为 1，压缩包最多 128 MiB、展开读取最多 512 MiB、10,000 个条目，总内容配额 8 GiB。校验不把 ZIP 解包到服务器目录，逐个读取内容并检查路径与 CRC；拒绝 ZIP64、分卷、加密、链接及可执行扩展。上传过程中存在原始表单暂存与校验副本，应预留至少 512 MiB 空闲磁盘。

本地内容存储为首版选择，S3/CDN 后续接入。曲包流量会占用主机带宽和出口额度；开放测试前需测量上传、视频曲包下载与实时通信并发。Docker 日志自动轮换，数据库 WAL 有初始预算；仍需监测磁盘增长。

## 部署步骤

1. 开发设备执行 `build.cmd -Cloud`，将整个 `out/cloud/` 上传到服务器，例如 `/opt/lazerrave/`。
2. 按 [Docker Ubuntu 安装文档](https://docs.docker.com/engine/install/ubuntu/) 安装 Docker Engine 与 Compose 插件。已有 Ubuntu 版本须处于受支持范围。
3. 为 `lazerrave.com` 和 `www.lazerrave.com` 设置指向主机的 DNS A 记录；只有服务器实际支持 IPv6 时才配置 AAAA。允许外网访问 TCP 80、443，SSH 按运维来源限制；数据库和 API 不开放公网端口。
4. 在服务器复制配置并填写数据库随机密码：

```bash
cd /opt/lazerrave
cp .env.example .env
chmod 600 .env
nano .env
docker compose config --quiet
docker compose up -d --build
docker compose logs --tail=50 api proxy
curl --fail https://lazerrave.com/api/health
```

数据库密码建议由 `openssl rand -hex 32` 生成，不保留示例占位值。`.env` 不纳入 Git，也不传入网站资源。

Caddy 为根域名和 www 申请 Let's Encrypt 证书，并将 www 重定向至根域名。容器网络使用 `172.30.86.0/24`，代理、API、数据库和初始化容器分别固定为 `.2`、`.3`、`.4` 和 `.5`，避免动态分配占用代理地址。API 只信任该代理的转发头。该网段与既有网络冲突时，须同步修改 Compose 网段、所有固定地址和 `ReverseProxyAddress`，不能直接信任任意代理。

数据库连接在容器内部使用密码认证，不采用 Kerberos；`GSS Encryption Mode=Disable` 避免 Npgsql 10 尝试加载未安装的 GSSAPI 库。数据库与 API 不映射宿主机公网端口。

`storage-init` 为内容卷设置应用 UID 1654 的权限，API 使用非 root 用户运行。更新应保留 Compose 项目名和数据卷，不能运行 `docker compose down -v`。

## 管理员初始化

通过网站注册自己的账号，然后在服务器执行：

```bash
docker compose exec api dotnet Cloud.dll --grant-admin YOUR_USERNAME
```

返回网站刷新即可出现 Admin 导航。该命令只提升已经存在的账户，不创建默认密码；生产环境应仅向必要运维人员提供服务器及 Docker 权限。

## 更新与迁移

先备份，再上传新的 `server/`、Dockerfile 与所需配置；保留 `.env` 和数据卷，执行 `docker compose up -d --build api`。服务启动时自动执行版本化 SQL 迁移，迁移失败时不开放 HTTP 服务。当前活动房间驻留内存，更新会中断房间；安排维护时段并通知玩家。

网站静态文件由 API 容器提供，更新时保留整套发布包。生产镜像目前固定主要版本，部署时在服务器的 `deployment-record.txt` 记录所用镜像摘要和数据库迁移版本。Docker 安装、服务启动和证书签发已在购买的服务器验证；空闲时 API、PostgreSQL 与 Caddy 容器合计约占 74 MiB 内存，上传和多人场景峰值仍须测量。未来实际自动续期尚未到触发时间，当前已配置自动管理与持久化证书存储。

### 独立 IR 更新

2026-10-10 已部署迁移 6 和对应 React 网站。迁移保留旧谱面 UUID 和成绩，已发布曲包的范围保持公开；其余旧内容保持隐藏。管理员主动隐藏的范围使用独立状态，不因再次发布曲包而恢复。

本次更新前备份位于 `/opt/lazerrave/backups/ir-20261010-011334-3437c747/`，包括数据库自定义格式导出、旧服务文件、当前网站静态资源与旧镜像标识。备份目录仅允许 root 访问；部署记录追加到 `deployment-record.txt`。公网检查确认健康接口、公开目录、前端资源与本地构建一致、网页直达路由和未登录管理员接口拒绝。真实谱面提交及私密访问的写入检查仅在隔离数据库执行。

迁移 6 为成绩新增必填 `board_id`。旧客户端可以继续访问当前 API 的兼容提交入口，但已升级的数据库不能仅通过替换旧 API 程序来回退，因为旧程序不会写入该字段。回退须配套处理数据库版本和更新后产生的数据，不能直接覆盖正式库；本次未执行数据库恢复。

## 代理配置

客户端下载页通过 GitHub Releases API 获取公开版本，CSP 只额外允许 `https://api.github.com`。更新 `Caddyfile` 时先在代理容器验证配置，再应用。单文件绑定挂载可能在 SCP 替换宿主文件后继续指向旧文件；此时执行 `docker compose up -d --no-deps --force-recreate proxy` 重新挂载配置。该操作保留证书数据卷和 API、数据库容器，但现有代理连接需要重新连接。

## 备份

数据库和内容卷需要成对备份。以下为数据库导出示例，备份目录应放在容器之外并限制访问：

```bash
mkdir -p backups
chmod 700 backups
docker compose exec -T db pg_dump -U lazerrave -d lazerrave -Fc > backups/lazerrave.dump
```

内容卷同时包含 ZIP 和头像。备份时暂停上传或停止 API，使用受控工具归档内容卷，再恢复服务；Caddy 的证书卷也可单独备份。每日副本需转移到另一故障域，不能仅保存于同一块 32 GB 磁盘。定期在独立数据库及内容卷演练恢复。

## 开放测试前的工作

- 接入邮件验证、密码找回和管理员多因素认证。
- 验收桌面账号和共享内容传输，接入游戏加载确认与实时统计。
- 在受控游戏后端完成回放校验，建立可信排名入口。
- 验证一个 16 人房间、曲包上传下载、数据库恢复与容器重启；测量内存、延迟、磁盘和出口预算。
- 在生产环境验收房主临时上传、成员授权、2 小时清理与公开曲包权限流程。

完整接口与实现边界见 [云端网站与服务](../development/cloud.md)。更大容量规划见 [服务器容量](server-capacity.md)。

### 自动 IR 提交更新

2026-10-10 已部署迁移 7。公开目录默认自动展示普通登记，私人及审核隐藏范围保留；服务器接收并区分真实演奏时间与上传时间。更新前确认没有活动房间并完成数据库、服务文件和镜像备份，备份目录为 `/opt/lazerrave/backups/ir-auto-20261010-014440-f7eed24a/`。公网只读检查通过公开目录、六种目录排序、难度过滤、三种排行排序、时间字段、无效排序拒绝及静态文件哈希一致性。迁移不可逆向套用旧程序；回退需配套处理数据库及更新后的成绩。
