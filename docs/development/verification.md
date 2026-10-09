# 编译与验证范围

## 构建入口

```powershell
.\build.cmd -CompileOnly
```

该入口编译客户端与引擎，不启动应用、自检或测试。完整打包使用 `build.cmd`，日志位于 `out/logs/`。C# 编译与 C++ 编译结果分别记录，任何一项失败均停止打包。

## 独立工具

| 工具 | 范围 |
| --- | --- |
| `scripts/check_docs.py` | MkDocs 配置、导航、本地链接与锚点 |
| `scripts/check.ps1` | 文档检查入口；测试和编译由显式参数启用 |
| `scripts/tests/test_client_package.py` | 打包行为与玩家数据保留 |
| `scripts/tests/test_engine_bridge.py` | 在指定独立运行环境中检查引擎协议 |
| `LazerRave.exe --check report.txt` | BMS 适配、规则集、裁剪模块、解析器及输入策略 |
| `scripts/performance/` | 经明确启动的窗口与帧时间诊断 |

桥接集成测试仅在提供 `LAZERRAVE_TEST_ENGINE` 和 `LAZERRAVE_TEST_FMOD` 时执行引擎请求。性能诊断使用标记的独立运行目录，不以正式成绩数据作为测试输入。构建脚本不调用这些工具。

## 人工验收

设置保存、曲库层级、难度切换、完整演奏、输入、音频、皮肤和窗口切换需要本机验收。图片序列 BGA 与视频 BGA 分别观察，并记录停顿范围与复现条件。

无窗口检查、前端 FPS 或编译结果不能代替实际呈现验收。具体矩阵见 [质量标准](../compatibility/quality.md)，帧时间记录见 [BGA 诊断](bga-diagnostics.md)。

## CI

根仓库工作流使用 Windows 工具链，分别执行文档检查和无私有资源的编译。上传产物用于定位构建结果，远端工作流的实际执行状态另行确认。
