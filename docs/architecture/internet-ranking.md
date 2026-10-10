# Internet Ranking 与难度表

数据模型参照 [LR2IR 数据集的实体结构](https://github.com/zkldi/lr2ir-dataset/blob/2cabd8972ebe072bcd7c420af1c9b653e8628e08/archive_parser/migrations/0001_schema.sql) 中的用户、谱面、个人最佳和课程关系，并保留本项目的原始成绩与内容身份。数据库迁移版本为 5。未导入 LR2IR 历史数据库，也未将历史玩家自动注册为本站账户。

## 用户身份

`users.id` 为内部 UUID，继续用于会话、曲包、房间和成绩外键。`users.uid` 为对外展示的正整数，不随用户名变化；初始用户按创建时间和 UUID 排序分配。迁移 4 将序列替换为事务计数器，保留既有 UID，并从当前最大 UID 继续取号。注册的 UID 分配、用户插入和登录会话创建在同一事务中完成，失败会回滚计数器；计数器行锁使并发注册串行取号。删除账户不回收编号。

个人资料、管理员用户列表、排名表和桌面联机面板显示 UID。公开资料可通过 `/players/id/{uid}` 页面及 `/api/players/{uid}` 接口访问，原有用户名链接继续有效。本站 UID 与原 LR2IR 的 player_id 属于不同命名空间，不用于证明历史账户归属。

## 成绩与个人最佳

| 数据对象 | 用途 |
| --- | --- |
| `charts` | 原始谱面 SHA-256 身份与用于难度表匹配的 MD5 |
| `scores` | 每次有效提交的判定、EX SCORE、最大分数、连击、排列、血槽、输入设备和备注 |
| `personal_bests` | 按玩家、谱面、规则、排列、血槽和验证状态生成的最佳记录视图 |
| `courses`、`course_stages`、`course_scores` | 课程、排序谱面及课程成绩的数据库基础；演奏与提交入口尚未接入 |

EX SCORE 由 PGREAT × 2 + GREAT 生成。最高分、最少 BP 和最佳清灯分别聚合，允许来自不同演奏。排行榜同时显示最高分对应的 BP 与历史最少 BP；连击和五类判定来自最高分记录。默认汇总同谱面、同规则下的全部有效排列与血槽，每位玩家显示一条最高分；可按排列、血槽及验证状态缩小范围。同分并列，BP 和 COMBO 不用于破同分；同一玩家同分时选取最早的记录作为明细。

网页提供参与人数、提交次数、通关率及最佳清灯分布，统计与当前筛选一致。每位玩家仅计数一次，EASY 及以上计为通关；同一玩家多次提交不会扩大参与人数。展开成绩详情可查看最高分那次演奏的清灯、普通 SCORE、五类判定和时间；最佳清灯及最低 BP 仍独立展示。谱面标识区展示 MD5 与 SHA-256。取消发布且没有其他已发布曲包引用的谱面不再公开排名及统计。

`GET /api/rankings/{chart}/summary` 返回筛选后的统计；参数 `arrangement`、`gauge`、`verified` 与排行列表相同。统计查询不修改成绩。普通 SCORE 属于可空字段，旧成绩显示空值，排行排序始终采用 EX SCORE。

最大分数尚未提供的旧记录不显示得分率与字母评级，避免从 BAD/POOR 等判定数量推测谱面总音符数。已提供最大分数时按九分制计算 AAA 至 F。客户端提交的最大分数和判定当前仍属于未验证数据，回放重算接入后才能形成可信排名。

## 难度表目录

`difficulty_tables` 保存名称、符号、来源、发布状态和维护者；`difficulty_table_entries` 保存 MD5、等级、标题、作者、来源链接和条目顺序。谱面尚未进入站内曲包目录时也可收录，之后按 MD5 动态匹配已发布谱面。难度表记录不创建虚假的谱面、资源包或成绩。

网页入口为 `/tables`，按表和等级浏览。站内已有谱面提供 Internet Ranking 链接，未入库条目显示其 MD5 与原始来源。所有条目分页展示，每页 50 条。

管理员在 Admin 的 Difficulty tables 选项卡填写表名、符号及来源，并上传标准 BMS 难度表 data 文件。文件应为条目数组，至少包含 md5 和 level，可附带 title、artist、url。支持数字或文字等级；同一张表内 MD5 必须唯一。首次保存为草稿，发布后进入公开目录；编辑时完整替换条目列表，保存使用数据库事务，验证失败不会删除现有目录。

| 接口 | 用途 |
| --- | --- |
| `GET /api/tables` | 已发布难度表 |
| `GET /api/tables/{id}` | 表信息与等级列表 |
| `GET /api/tables/{id}/entries` | 按 level 和 page 查询条目 |
| `GET /api/tables/{id}/header.json`、`data.json` | 标准 BMS 难度表头与条目导出 |
| `POST /api/admin/tables`、`PUT /api/admin/tables/{id}` | 创建与完整替换 |
| `PUT /api/admin/tables/{id}/publication` | 发布及取消发布 |

导入文件最多 3 MiB、10,000 条，API 请求上限为 4 MiB。来源链接只接受 HTTP/HTTPS，服务端不主动抓取来源，避免将远程请求与目录导入绑定。所有管理操作记录审计日志。
