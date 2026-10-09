# LazerRave

运行 `LazerRave.exe` 启动客户端，无需另行安装 .NET。`OpenLR2_x64.exe` 保留经典独立入口。移动时保留完整目录，包括原生 DLL、`Resources/`、`Localization/`、`LR2files/` 和许可文件。前端素材位于 `Resources/`，语言资源位于 `Localization/` 下的各语言目录。

## 曲库与演奏

在设置中指定曲库根目录，扫描后按文件夹、键型和难度选择谱面。设置中的速度、偏移和排列随演奏请求传入引擎。同窗口与独立窗口方式可在演奏设置中切换。

前端设置保存在 EXE 同级的 `userdata/settings.toml`；客户端数据库、界面配置及日志保存在 `userdata/`，缓存位于 `cache/`。首次正常启动复制旧 AppData 数据，目标已有文件和旧数据保留。原生演奏配置、成绩、回放和自定义皮肤位于 `LR2files/`。更新程序或清理目录前保留这些数据。

## 故障与诊断

启动错误记录于 `userdata/logs/lazer-startup-error.txt`，设置保存错误记录于同目录的 `settings-save-error.txt`。无法写入日志目录时，启动错误尝试记录到 EXE 同级的同名文件。前端图形帧率与原生演奏帧率分别设置。

渲染预设和逐帧诊断说明见 [BGA 与帧时间](bga-diagnostics.md)。诊断工具不随正常启动自动运行。

## 功能与许可

账号、多人、云端回放、在线排行榜、曲包服务、BMSON 和 BMS 编辑仍待接入。保留的新闻、更新日志及在线页面属于可复用基础，不表示相应服务已启用。

第三方许可位于 `licenses/`。个人曲库与运行皮肤不属于可公开再分发的默认资源。
