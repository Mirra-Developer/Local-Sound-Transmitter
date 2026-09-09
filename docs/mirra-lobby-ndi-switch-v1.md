# Lobby ↔ NDI：独立全屏转场验证

2026-09-09。本实现先验证完整 Lobby 场景与一路 NDI 的双向全屏转场，把原视觉计划的整场验证提前；框内独立媒体合成仍待后续。音频保持第二阶段。

## 画面与代码所有权

实际 Lobby DEV 动画经 Electron GPU 共享纹理进入原生进程；NDI 经现有 `DirectNdiDecoder` 持续接收；Alpha 视频经现有 `stinger_asset::Loader` 准备。三者调用 **现有 `native_stage::DxgiPresenter::present_layers`** 做 GPU 分层合成，窗口消息在独立线程处理。不是浏览器回传视频，也不是新写一套 NDI 解码器。

隔离入口为 Mirraflow `lobby_switch` example；启动脚本用 `cargo rustc -p mirraflow-server --example lobby_switch --locked -- -C opt-level=2` 优化该原生入口，依赖仍复用 dev 构建缓存。Lobby 沿用 `electron/live-output/host.cjs`，只在 session.mode 为 `lobby-ndi` 时显示新控制页。新实例默认端口 TCP 17711、控制页 14182，旧 14179 / 14180 / 14181 不替换。

共享组件的改动：GPU 帧可以持有原生 COM resource 或原 GStreamer sample；原 GStreamer 构造路径仍保留 sample 生命周期。合成器新增显式 device / 固定输出尺寸入口，旧 `new(hwnd)` 仍使用原默认选择、客户区尺寸和 Present(1)。实验入口明确指定与 Lobby 纹理兼容的 adapter、3840×2160 输出及独立 60 Hz 节奏。改动不是零生产源码变更，须跑原 native-stage 回归；不覆盖已在运行的程序。

## 转场约定

- A/B 来源持续运行，切回 Lobby 不重建 renderer、epoch 或动画时钟。
- Lobby 纹理复制完成状态在后续 tick 查询，不在出画线程忙等；原纹理、复制句柄与宿主纹理保留到 GPU 完成确认。未完成时仍可出上一帧，禁止提前 ACK 回收。
- 控制 `take {target:"ndi"|"lobby"}` 复用现有会话认证；同目标待命请求返回 alreadyActive，转场期间任何新 take 拒绝 busy。
- NDI 至少收到三帧且最新帧年龄小于 500 ms 才可切入；只有发现记录不算就绪。Lobby 需已有画面且独立心跳新鲜。
- 转场视频后台解码，预算上限 512 MiB；后台预计算各帧是否全不透明，禁止在每次 status 或呈现时扫描整张 Alpha 图。
- 配置的 cutMs 对应帧必须完全不透明；实际切点跟随素材帧 PTS，而非点击之后固定 sleep。运行时跳帧错过完整遮挡则取消，保持之前 Program。
- NDI 在切点前变陈旧，取消转场并保留 Lobby；已经切入 NDI 后断源，保留最后 NDI 帧，允许转场返回仍存活的 Lobby。NDI 恢复后可再次切入。
- 底层来源只有在合成器 Present 返回成功后提交为 active，并记录 cut 事件；GPU 设备丢失等显示故障的完整恢复仍不属于此原型验收。
- 正常播放不做像素 readback、PNG 或视频编码；显式 snapshot 只用于 QA，会影响当时节奏，须与性能采样分开。
- status 只返回最近 30 个转场事件，累计 completed 独立计数；测试逐次核对切点。性能分布保留首 7200 个样本，另行累计整个采样时段最大帧间隔，不能把截断样本的最大值当作全程最大值。

这里复用了原转场 Clock 与 Alpha 素材加载器，未恢复 Editor 已移除的旧转场素材库，也未增加正式 Program API。当前注册的是这个受控整场实验的来源组合，尚未支持任意 Editor Clip 选择 Lobby。

## 本机演示素材

默认 NDI 为脚本创建的 **真实 NDI SDK 信号**，1280×720、发送目标 60 fps，带彩条、移动标尺与帧号。它不是来自现场游戏电脑；发送分辨率与 4K 输出分开记录。

转场是验证用 FFV1/MKV 双门动画：1280×720、60 fps、约 1 秒、cutMs=500，400–650 ms 区间完全遮挡。解码约 211 MiB，放大到 4K 合成；这不是原生 4K 转场素材质量验收。正式素材需另行校验 Alpha、切点和预算。

## 运行

环境沿用联合目录 `Enter-LocalAv.ps1`。先启动隔离 Lobby 来源 14179。生成素材（新目录）：

```powershell
. C:\Mirra_Dev\mirra-av-integration\scripts\Enter-LocalAv.ps1
node scripts/av-integration/make-switch-stinger.cjs C:\Mirra_Dev\mirra-av-integration\verification\visual-switch\assets
```

在 Lobby AV 工作区运行 `scripts/av-integration/local-workspace/Start-LobbyNdiSwitch.ps1 -RunName <全新名称>`；可传 `-NdiUri <ndi://地址或名称> -Stinger <文件路径> -CutMs <毫秒>` 选择实际来源与素材。默认测试 sender 最长运行一小时，不创建系统自启动。

打开 http://127.0.0.1:14182/，点击“转场到 NDI”“转场回 Lobby”，观察桌面 **Mirraflow Lobby-NDI** 窗口。固定 4K 渲染缓冲缩放到 1280×720 查看，不能视为物理 4K 屏幕扫描验收。

测试入口 `node scripts/av-integration/test-lobby-ndi-switch.cjs <会话目录> 200`。停止用 `Stop-LobbyNdiSwitch.ps1 -RunDirectory <会话目录>`，核对 PID 归属并先停止 native，再停止宿主及该会话自有 sender；不停止其他 NDI 发送者。

所有 token、日志、执行文件、profile、原始截图和测试报告位于联合目录 `verification/visual-switch`，不提交 Git。新契约以 Lobby docs 为编辑源，同步 Mirraflow/Sound；既有音频契约不变。

## 2026-09-09 本机验证结果

最终样本 `verification/visual-switch/switch-4k60-05/switch-acceptance.json`：100 次往返、200 次切换全部通过，每次恰有一个完整遮挡后的提交切点；拒绝重复 take，Lobby epoch 始终不变。约 209.47 秒中 12569 次 Present，平均 60.003 fps，全程最大帧间隔 25.815 ms，漏过的计划出画时隙为 0。首 7200 个间隔样本 p95 为 18.160 ms。旧 V1 实验窗口和来源在该轮仍同时运行。

Lobby 新纹理接收平均 **55.153 fps**，无新帧时复用最近一帧；不把稳定 60 Hz 呈现表述为每秒 60 张不同网页画面。未测物理显示扫描、整机端到端延迟或现场游戏电脑 NDI。教学框仍是网页占位海报，未完成框内来源切换。

`recovery-acceptance.json` 通过真实 SDK sender 停止/重启验证：切点前断源取消并保留 Lobby；切入后断源持有相同像素（两张完整 4K PNG 哈希一致）；断源时可返回 Lobby，并拒绝再次切入未就绪 NDI；sender 恢复后可重新切入，Lobby renderer epoch 保持不变。

已检查完整 Lobby、恢复后的 NDI、部分覆盖及全覆盖四种实际 GPU 输出截图，尺寸均为 3840×2160。截图与故障测试在性能样本之后单独执行，不纳入上述帧率。

回归：Lobby 55 项相关测试及生产 build 通过（输出另存联合目录）；native-stage 43 项通过、6 项依赖特定旧 fixture 的测试仍跳过；V0/V1 example 6 项通过。原生产构建的 200 个文件 SHA256 全部保持一致。

未通过的早期样本保留：03 为同步等待复制、未优化构建，58.258 fps；04 改为异步完成检查但仍未优化且每次返回完整事件历史，57.894 fps。05 同时采用入口优化构建和有界 status 事件窗口后通过，不能把性能提升全部归因于某一个改动。

## 同日资源复测与结束状态

随后按用户要求，分别测试旧 V1、新 Lobby-NDI 以及两套同时运行，每组 60 个资源样本。新链路持续执行 Alpha 转场；旧 V1 本身不支持 NDI 转场，作为仅播放完整 Lobby 动画的对照。前两轮出现静止页面的样本已排除；有效轮次通过宿主 restart 重新创建循环页面，并在重建后重新识别 Electron 子进程 PID。

下表是链路平均占用，含各自的 Lobby 后台，不含共享开发服务和本机 NDI 测试发送器：

| 条件 | CPU | 私有驻留内存 MiB | NVIDIA GPU | Intel GPU | 专用 GPU 内存 MiB |
|---|---:|---:|---:|---:|---:|
| 旧 V1 单独 | 5.9% | 477.2 | 26.7% | 26.7% | 1577.4 |
| 新 Lobby-NDI 单独 | 8.0% | 780.3 | 25.6% | 28.2% | 1618.1 |
| 两套同时运行 | 20.4% | 1184.3 | 52.8% | 66.2% | 3124.2 |

新链路单独运行：Mirraflow 原生进程平均 CPU 1.8%、私有驻留内存 342.7 MiB；Lobby 后台平均 CPU 6.2%、437.6 MiB。计入测试发送器和开发服务后，本机这套实验进程平均 CPU 10.2%、917.3 MiB。CPU 按整机 24 个逻辑处理器归一化；GPU 按适配器最繁忙物理引擎统计，不把独立引擎相加。GPU 内存采用 Windows 进程归属计数，共享纹理可能重复归属，不等于唯一物理显存。

新链路单独运行完成 71 次切换，Lobby 新帧 59.58 fps，Present 60.01 fps，最大间隔 25.50 ms。两套持续动画同时运行完成 78 次切换，新链路新帧 47.29 fps、Present 59.71 fps、最大间隔 147.34 ms；旧链路 Present 降至 46.12 fps。因此此前 200 次样本不能推导为任意并发负载下均稳定 4K60。

这两轮的全部 149 次切换均有一个有效遮挡切点；没有 GPU 所有权错误。该资源复测仍使用**本机 SDK 生成的真实 NDI 协议信号**，没有接入第三方软件或现场其他电脑的节目源，也未验收跨机网络、第三方兼容性、物理扫描或音频。

完整平均值、峰值、原始 JSON/CSV 和关闭记录保存在联合目录 `verification/resource-comparison-20260909`，不进入 Git。测试结束后，两个原生窗口、各自 Lobby 宿主、本机 SDK sender 和本轮启动的 Vite 服务均已退出；文件保留。后续演示需重新启动相应实验。
