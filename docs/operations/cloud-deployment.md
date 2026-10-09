# LazerRave.com 部署

当前主机为 Ubuntu、1 vCPU、1 GiB 内存、32 GB NVMe，尚未部署服务。部署包面向 x86-64 Linux；执行 `uname -m` 确认输出为 `x86_64`，32 位 x86 不适用。

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
4. 在服务器复制配置并填写数据库随机密码与证书邮箱：

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

Caddy 为根域名申请证书，并将 www 重定向至根域名。容器网络使用 `172.30.86.0/24`，代理固定为 `172.30.86.2`，API 只信任该代理的转发头。该网段与既有网络冲突时，须同步修改 Compose 网段、代理地址和 `ReverseProxyAddress`，不能直接信任任意代理。

`storage-init` 为内容卷设置应用 UID 1654 的权限，API 使用非 root 用户运行。更新应保留 Compose 项目名和数据卷，不能运行 `docker compose down -v`。

## 管理员初始化

通过网站注册自己的账号，然后在服务器执行：

```bash
docker compose exec api dotnet Cloud.dll --grant-admin YOUR_USERNAME
```

返回网站刷新即可出现 Admin 导航。该命令只提升已经存在的账户，不创建默认密码；生产环境应仅向必要运维人员提供服务器及 Docker 权限。

## 更新与迁移

先备份，再上传新的 `server/`、Dockerfile 与所需配置；保留 `.env` 和数据卷，执行 `docker compose up -d --build api`。服务启动时自动执行版本化 SQL 迁移，迁移失败时不开放 HTTP 服务。当前活动房间驻留内存，更新会中断房间；安排维护时段并通知玩家。

网站静态文件由 API 容器提供，更新时保留整套发布包。生产镜像目前固定主要版本，正式发布应进一步记录镜像摘要与数据库迁移版本。Docker 部署、证书申请及主机限额尚未在购买的服务器验证。

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
- 连接桌面账号、共享内容身份、游戏加载确认与实时统计。
- 在受控游戏后端完成回放校验，建立可信排名入口。
- 验证一个 16 人房间、曲包上传下载、数据库恢复与容器重启；测量内存、延迟、磁盘和出口预算。
- 接入房主临时上传、成员授权、清理与公开曲包权限流程。

完整接口与实现边界见 [云端网站与服务](../development/cloud.md)。更大容量规划见 [服务器容量](server-capacity.md)。
