# BGA 与嵌入演奏帧时间对比

## 使用方法

在 osu!lazer 前端设置中打开 **Game viewport / 演奏窗口**，将 **Game presentation / 演奏显示方式** 设为 **Embedded / 同窗口**，选择 **Embedded rendering preset / 同窗口渲染预设**，等待设置页显示 **Saved / 已保存** 后重新进入曲目。修改停止约 400 ms 后自动保存，进入演奏前也会保存；无需执行曲库扫描。保存失败时设置页显示错误，演奏不会使用旧参数继续启动。预设由新启动的 OpenLR2 会话应用，无需重启前端。预设差异可通过独立副本自动演奏对比，显示效果仍需结合人工验收。

使用相同曲目、相同难度、相同速度、相同窗口尺寸和同一段落比较。前端图形设置保持不变，演奏帧率上限先固定为 `Display`。先比较 A 与 F，再比较 B、D、E、G，最后比较 C。一次只切换一组预设；每组至少持续演奏 30 秒。

| 预设 | 相对 A 的变化 | 排查目的 |
| --- | --- | --- |
| A - Baseline | D3D11、flip-discard、关闭引擎 VSync、使用所选演奏帧率上限 | 当前实现的基准 |
| B - Engine unlimited | 取消引擎软件限帧，忽略演奏帧率上限 | 软件限帧与视频更新节奏 |
| C - Engine VSync | 引擎交换链开启 VSync，取消软件限帧，忽略演奏帧率上限 | 呈现同步；同时留意按键响应是否变慢 |
| D - DirectShow decoder | 禁用 Media Foundation，指定 DirectShow | 视频解码路径；若 A 已回退到 DirectShow，两组可能相同 |
| E - D3D11 blit | 交换链改为 discard | flip 模式与子窗口呈现的相互影响 |
| F - BGA disabled | 本次会话跳过谱面背景素材加载、播放与绘制 | 确认异常是否与 BGA 相关 |
| G - RGB video surface | 禁用 YUV 视频表面，使用 RGB 路径 | 视频颜色转换、纹理上传路径；可能增加 CPU 开销 |

预设保持 D3D11 渲染器；不切换到另一套嵌入窗口实现。A、D、E、F、G 均使用同一个“演奏帧率上限”设置。B 改变的是 OpenLR2 演奏帧率，前端图形设置中的无限制是另一项设置。恢复 A 可恢复当前基准；独立启动 OpenLR2 的经典模式不应用这些预设。

当前曲包中，`[0026]Camel` 使用 `stage.mpg` 视频，`[0011]桜華月` 使用 PNG 图片序列。D、G 主要对视频路径有意义。F 会移除谱面背景，对比时仍需观察音符移动与按键体感。

## 独立窗口对照

设置中将 **Game presentation / 演奏显示方式** 切换为 **Separate window / 独立窗口**，等待已保存后开始同一谱面。该模式保留选曲与演奏参数传递，跳过经典选曲菜单。A～G 预设和同窗口帧率限制不适用于此模式；窗口尺寸可沿用预设或自定义宽高。

比较时保持曲目、难度、速度和尺寸一致，记录卡顿发生的演奏时间。独立模式改善只能说明原生窗口路径更适合当前配置，不能直接认定嵌入本身是唯一原因，因为两条路径同时存在渲染器、同步和窗口呈现方式的差异。

## 日志

每个嵌入会话在运行目录的 `logs/engine-frames-<进程编号>.csv` 中写入帧时间汇总，每约 5 秒一条，不逐帧写文件。文件包含预设、实际渲染器、同步开关、会话帧率上限、场景、平均 FPS、平均与最大帧间隔、超过 16.67 ms 的帧数，以及消息处理、逻辑和绘制、画面提交三个阶段的最大耗时。日志失败不会阻止演奏。

其中 `frame_limit=0` 表示 Display，`-1` 表示 Unlimited；C 的帧率上限字段保留原请求值，实际由 VSync 控制。`scene=4` 为演奏场景，仍可能包含资源加载和场景过渡，比较时优先查看正常演奏中段的数据。`draw_max_ms` 包含场景逻辑、输入处理和绘制；`message_max_ms` 包含 DxLib 消息处理及其中发生的媒体更新。这些数值是 CPU 阶段耗时，不能直接当作 GPU 时间或显示器实际扫描输出，也不能单独证明存在撕裂。

反馈时记录所用预设、曲目难度、异常发生段落，以及音符和背景是否同时停顿。对应 CSV 可用于继续定位；无需依据前端 FPS 推断演奏帧率。


## 自动性能对比

`scripts/performance/run_comparison.py` 创建独立运行副本，使用真实 BMS 自动演奏并限时退出。必须显式传入 `--visible`；该操作会显示测试窗口并播放音频。测试期间关闭正式客户端和其他游戏实例，避免重复启动拦截及资源争用。性能测试不是普通构建的一部分。`--seconds` 是限时会话时长，包含引擎加载和准备；实际有效演奏时长以分析报告为准。需要完整演奏或定位指定 combo 时，使用正式运行包手动游玩，避免限时脚本提前退出。

先编译，再运行一组对比：

```powershell
.\build.cmd -CompileOnly
python -X utf8 -B scripts/performance/run_comparison.py --visible --chart "D:/BMS/Sample/chart.bms" --mode frontend --profiles baseline no-bga discard --seconds 35
```

`frontend` 使用完整 OsuGame 和嵌入演奏；`frontend-standalone` 使用完整 OsuGame 和独立演奏窗口；`host` 使用空白 Win32 承载窗口；`standalone` 使用经典独立路径，两个独立窗口模式只接受 baseline 标签且不应用嵌入渲染预设。默认前端测试窗口为 1280×800、VSync，嵌入引擎上限为 240；可用 `--window-size`、`--frame-sync` 和 `--frame-limit` 明确指定。各项比较应保持曲目、速度、尺寸、前端设置及其他运行条件一致；经典路径使用自身配置，不能据不同 FPS 直接判定其体验更好。

测试仅在带标记的独立运行目录中启用前端自动选曲，配置为只读；引擎启用自动演奏和禁止成绩保存。原始配置、成绩、回放与共享设置在前后计算哈希，结果写入 `protected-files.csv`。外部运行中的程序也可能改变这些文件并触发检查失败，应先检查并行进程，不自动回滚玩家数据。

`--reuse-runtime` 只允许复用 `out/reports/validation/` 下带标记的测试副本，可减少重复复制。正常运行包不接受该用途。测试副本可在保留日志后移除；正式运行包与私人曲库不属于测试清理范围。

## 完整自动演奏记录

`--full-song` 等待自然演奏结束，加载和准备不会缩短演奏。引擎检测到完整判定和正常结束阶段后关闭本次诊断窗口；正常客户端没有该退出规则。默认 600 秒看门狗只用于防止测试挂起，超时会标为失败，不计作完整样本。

```powershell
.\build.cmd -CompileOnly
python -X utf8 -B scripts/performance/run_comparison.py --visible --full-song --chart "D:/BMS/Sample/chart.bms" --mode frontend-standalone
python -X utf8 -B scripts/performance/run_comparison.py --visible --full-song --chart "D:/BMS/Sample/chart.bms" --mode frontend --profiles discard
```

实时 FPS、combo、判定对象数和演奏进度通过只读共享内存监控，每秒采样至 `live-fps.csv`。原生逐帧 CSV 增加 combo、已判定对象数、对象总数、事件游标、BPM、谱面时间、渲染时间及曲目时长。前端绘制与更新 FPS 每秒采样至 `frontend-fps.csv`。引擎正常退出后保存 `engine-session-*.csv` 完成证据，必须满足完成标记、全部对象已判定及零丢弃记录。中途退出、看门狗超时或记录容量不足均不计作完整演奏。

完整模式预留 1200000 条原生帧记录，记录结束后统一写盘。其内存占用高于短时模式；分配在演奏前完成。完整记录分析器以流式方式读取 CSV，生成每秒 FPS、长帧、combo 310 附近数据及曲线：

```powershell
python -X utf8 -B scripts/performance/analyse_full_song.py PATH/TO/frontend-standalone-baseline PATH/TO/frontend-discard --output out/reports/validation/full-song-report
```

曲线生成需要 Python 的 matplotlib。软件提交 FPS 不能当作显示器刷新率；前端每秒采样也不能排除采样间隔内的短暂长帧。自动演奏结果不能替代人工输入延迟验收。

## 逐帧记录

设置进程环境变量 `LAZERRAVE_FRAME_TRACE=1` 可记录 `logs/engine-trace-<PID>.csv`；自动测试入口会自行设置。正常启动默认关闭。短时记录默认最多保留前 180000 帧；完整模式扩大缓冲，`LAZERRAVE_TRACE_CAPACITY` 可在 180000～2000000 范围指定。超限数量写入 `dropped_rows`。诊断采用高精度计时，逐帧数据保存在内存，正常退出时统一写盘，避免每帧日志 I/O 干扰测量；崩溃或强制终止可能无法取得记录。

记录包含软件限帧、消息处理、逻辑与绘制、提交和帧外处理时间，另附场景、阶段、演奏时间、BGA 状态及实际 VSync 标志。D3D11 下尝试读取当前交换链的显示统计，保存返回状态、显示提交 ID、刷新计数及 QPC 时间。接口不支持或返回错误时不能推断实际显示正常。相关字段定义见 [Microsoft DXGI_FRAME_STATISTICS](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ns-dxgi-dxgi_frame_statistics)。

汇总日志目录：

```powershell
python -X utf8 -B scripts/performance/analyse_frames.py out/reports/validation/performance-YYYYMMDD-HHMMSS --output out/reports/validation/performance-summary.md
```

分析器默认以 120 Hz 为预算基准，排除加载及演奏最初 3 秒，报告 P95、P99、P99.9、最大间隔和长帧详情，另输出 CSV。可通过 `--refresh` 和 `--warmup-ms` 调整。自动演奏不验证人工输入延迟；短时测试未复现也不能判定偶发卡顿已解决。
