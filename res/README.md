# 资源

| 目录 | 内容 | Git |
| --- | --- | --- |
| `branding/` | SVG 标识、生成的 ICO 与 PNG | 纳入 |
| `licenses/` | 上游、字体、原生依赖及素材的许可证和声明 | 纳入 |
| `release/` | 发布包默认前端及 OpenLR2 配置 | 纳入 |
| `runtime/` | 本机 LR2 皮肤、配置与运行素材 | 忽略 |
| `library/BMS/` | 可选的本机曲库来源 | 忽略 |

普通构建补齐 `out/app/` 缺失的资源并保留已有玩家数据。ZIP 发布使用 `release/` 中的默认配置，不复制个人账号、成绩或曲库；共享及网站曲包安装到 EXE 同级的 `BMS/Shared/`。

第三方身份和原始许可文件名保持不变，来源与限制见 `licenses/`。构建与运行说明统一维护于 [正式文档](../docs/getting-started/build-and-run.md)，发布内容见 [版本与发布包](../docs/development/versioning-and-release.md)。
