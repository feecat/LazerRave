# 前端资源包

发行包将前端素材程序集放置于 `Resources/osu.Game.Resources.dll`，应用代码和一般托管依赖仍合入 `LazerRave.exe`。全部语言卫星程序集继续位于 `Localization/<culture>/`。移动或分发时必须保留整个运行目录。

## 构建来源

输入为固定版本的 `ppy.osu.Game.Resources` 2026.918.0，对应 [osu-resources](https://github.com/ppy/osu-resources) 提交 `d481bc98f2603348ee81c15174cd72604286310d`。该包由现有客户端 NuGet 依赖还原，不额外引入完整资源源码或二进制副本到 Git。

`src/LazerRave/resource-pack/` 是构建工具，使用 Mono.Cecil 0.11.6 按 `removals.tsv` 删除指定嵌入素材。程序集名称、版本、代码接口、剩余资源名称及内容保持不变。NuGet 缓存及 IDE 开发输出使用原始资源包；裁剪只作用于发布副本。该工具及 Mono.Cecil 不进入客户端运行包。

标准入口仍为 `build.cmd`。发布过程自动构建工具、生成裁剪包、更新依赖路径并复制到运行目录。`-CompileOnly` 编译工程和工具，不生成裁剪运行包。

## 裁剪范围

| 范围 | 处理 |
| --- | --- |
| 开场与结束音效、Circles/Triangles 开场图片及欢迎文字 | 移除 |
| 内置 osu 曲包与官方排位背景音乐 | 移除；当前客户端不导入这些曲包 |
| 每日挑战与官方 matchmaking 专用音效 | 移除 |
| osu、Taiko、Catch 专用演奏图片 | 移除 |
| Legacy/Retro 中的 spinner、taiko、fruit 专用素材 | 按明确前缀或名称移除 |
| 字体、全部翻译、着色器 | 保留 |
| 主菜单、选曲、设置及通用界面素材与音效 | 保留 |
| Mania、共享编辑器及通用演奏音效 | 保留 |
| 普通多人房间的加入、离开、准备、倒计时与队伍音效 | 保留 |
| 欢迎背景图片 | 保留，旧配置的菜单背景回退仍可能引用 |

首次裁剪移除 164 项素材，资源程序集从 125.31 MiB 降至 87.03 MiB。数值会随上游版本或清单变更而变化，以本次构建报告为准。外置资源减少 EXE 的体积；完整运行包的实际缩减来自被移除的素材。

策略采用制表符分隔的三列：匹配方式、完整资源名或前缀、移除原因。匹配方式为 `exact` 或 `prefix`。工具拒绝针对字体、着色器、本地化及代码的规则；每条规则都必须匹配输入资源，避免上游升级后继续使用失效清单。扩展规则前应检查保留代码的显式及动态资源引用，不能仅按文件名称或所属游戏模式推断用途。

## 加载与依赖路径

发布时保留程序集身份，将裁剪程序集排除出单文件捆绑。构建工具为生成的依赖清单设置 .NET 10 支持的本地路径：主资源包指向 `Resources/`，语言资源指向 `Localization/`。这些元数据随后合入 EXE，不需要手工维护配置文件。路径规则依据 [.NET 10 依赖解析实现](https://github.com/dotnet/runtime/blob/v10.0.0/src/native/corehost/hostpolicy/deps_entry.cpp)。

`RuntimeResources` 在模块初始化时注册程序集解析器，仅处理已知资源程序集；缺少文化资源时保留标准父文化及中性资源回退。开发构建仍能使用常规输出目录中的完整资源程序集。

## 构建记录

工具输出位于 `out/build/tools/resource-pack/`。裁剪副本和报告位于 `out/build/client/obj/<配置>/net10.0-windows/win-x64/resource-pack/`：

| 文件 | 内容 |
| --- | --- |
| `osu.Game.Resources.dll` | 用于发布的裁剪资源包 |
| `osu.Game.Resources.inventory.tsv` | 每项资源的名称、字节数、保留或移除结果及原因 |
| `osu.Game.Resources.summary.txt` | 输入版本、程序集身份、哈希、体积与资源数量 |
| `osu.Game.Resources.fingerprint.txt` | 输入、清单、构建工具与输出的增量缓存指纹 |

输入和清单不变时复用已生成资源包。修改规则或工具、更新依赖版本、输出内容不匹配时重新生成。清理 `out/build/` 后由标准构建恢复，不依赖本机迁移归档。

构建成功仅确认编译与发布完成。菜单、设置、语言切换、谱面预览及进入演奏仍需人工运行验收。
