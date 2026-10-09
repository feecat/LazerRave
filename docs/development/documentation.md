# 文档维护

正式文档位于 `docs/`，使用 UTF-8 Markdown。根 README 提供项目与构建入口，文档首页提供功能状态和分类导航，文档索引列出全部维护页面。

## 组织约定

| 目录 | 内容 |
| --- | --- |
| `getting-started/` | 环境与构建运行 |
| `development/` | 客户端、桥接、目录、诊断与维护 |
| `architecture/` | 技术边界及规划协议 |
| `compatibility/` | 格式、皮肤和验收标准 |
| `operations/` | 服务器与容量规划 |

文件名统一使用小写和连字符。实现状态在首页及所属模块维护，路线图定义未来交付门槛。诊断日志、迁移清单和操作记录放在忽略目录或仓库外，不作为正式页面。

## MkDocs

仓库根 `mkdocs.yml` 使用内置 readthedocs 主题，输出到 `out/site/`。安装及使用：

```powershell
python -m pip install -r requirements-docs.txt
python -m mkdocs serve
python -m mkdocs build --strict
```

静态配置与链接检查为 `python -X utf8 -B scripts/check_docs.py`。文档工具独立于游戏构建，不启动游戏。

导航覆盖全部正式页面。重命名页面时同步修改相对链接及 MkDocs 导航。构建脚本不向运行包复制 README 或开发文档。同一目录不同时提供 README 与 index，以免首页路径冲突。

## 表述与许可

文档描述功能、接口、约束和验收条件，区分源码实现与运行验证。规划不能表述为已交付能力。配置和协议以字段表、状态表及文本示例说明，不在文档目录存放 JSON 或诊断转储。

第三方名称、许可证和规范版本保留原始身份。原生引擎、皮肤、曲目、字体和前端资源分别声明来源与发行条件。
