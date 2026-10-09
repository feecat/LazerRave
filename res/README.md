# 资源

`licenses/` 保存随当前客户端使用的上游资源和字体许可，以及随源码纳入的第三方二进制组件声明，均纳入 Git。字体与 osu! 上游许可沿用原始文件名；`fmod-engine-NOTICE.txt`、`dxlib-NOTICE.txt` 和 `openlr2-NOTICE.txt` 记录 `src/OpenLR2/` 及其依赖的来源与已知限制，其中 OpenLR2 的上游许可尚未声明，FMOD 为需要单独授权的商业中间件。公开发行前需按这些声明逐项确认。

`runtime/` 保存私有 LR2 皮肤、配置和参考资源，`library/BMS/` 保存本机曲库；两者均忽略版本控制。当前客户端与经典 OpenLR2 统一读取个人设置中的曲库目录。已有曲库已迁到 `library/BMS/`，运行包保留独立配置、成绩和回放。

打包由 `scripts/package-client.ps1` 复制缺失资源与曲库，并覆盖程序和依赖。资源来源中的旧 EXE、重复 BMS 副本和历史文件已移到仓库外归档，不再作为运行依赖。默认运行产物为 `out/app/`。

这些本机资源尚未确认可公开分发。合法素材、字体、皮肤和曲目应分别记录来源与许可。

`branding/` 保存 LazerRave 的 SVG 标识，以及由其生成的 Windows 图标和主菜单图片。`runtime-guide.md` 仅为仓库内的使用说明，不复制到运行包。正式开发文档位于 `docs/`。
