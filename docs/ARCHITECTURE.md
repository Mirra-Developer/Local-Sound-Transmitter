# 音频架构与当前限制

## 远程路径

B/C/D/E 的 Windows 默认播放混音 → WASAPI Loopback → 格式归一化 → UDP PCM → A 的 IP 通道绑定 → 通道音量/Mute → 混音输出。

传输为 48000 Hz 双声道浮点 PCM。`IntegratedSenderService` 负责发送，`UdpAudioReceiver` 接收，`ChannelRegistry` 负责路由，`MixerSampleProvider` 混音，`AudioOutputService` 播放。每台发送电脑当前传输整体播放混音，不会按应用拆通道。

关闭自动创建时，只接收绑定来源 IP 的远程音源。发送端每秒发送 Hello，通道超过约 3 秒没有更新时网页显示红色；红色不区分网络故障、服务停止或来源未活动等原因。

## A 本地路径

A 本地应用 → Windows 播放设备 → 音响，原始声音直接播放。

`LocalLoopbackCaptureService` 创建本地通道；`LocalSessionMuteService` 在远程聚焦时静音默认 Multimedia 播放设备上的应用会话，并排除本软件进程。回到本地聚焦时恢复本软件记录过的应用；不会有意解除原本已经静音的应用。该实现按进程 ID 记录，并非精确的会话快照。

本地声音不是先静音再通过混音器重播，因此本地 Output 保持关闭，以免默认播放混音被重复输出形成反馈。

## 已知限制与待验证项

- 本地应用静音是约 250 ms 轮询的开关，不是 Compressor 或音量渐变。远程通道渐变由 `ChannelFadeService` 按约 20 ms 更新。
- 本地通道普通 Mute PATCH 不驱动本地会话静音；需使用 focus-channel。
- 关闭自动创建时，本地采样调用 `PushAudio` 也经过只认来源 IP 的路由，可能被丢弃，导致本地电平/在线指示不正确；本地应用原始播放与此不同。此项待修复。
- 当前混音使用 `Math.Clamp` 限制样本范围，属于硬截幅；没有 Compressor、Limiter 或真正的峰值保护。
- 远程总输出保护无法覆盖直接播放的 A 本地应用。统一保护需要重新设计本地音频路由。
- 同机发给自己、双向回送默认混音可能反馈；没有完整防反馈机制。
- 配置保存会重载服务；并发保存、设备消失、重载失败的恢复行为仍需完善和验证。
- 没有保证应用启动失败或崩溃后自动恢复；启动文件夹仅提供登录启动。

## 源码导航

源码位于 `src/SoundTransportation.Mixer/`：

| 文件 | 职责 |
| --- | --- |
| Program.cs | HTTP API、服务注册、单实例入口 |
| TrayService.cs | 托盘、打开管理页、退出 |
| AppSettingsStore.cs | JSON 配置读写与 DTO |
| ChannelRegistry.cs / AudioChannel.cs | 路由、通道状态与队列 |
| IntegratedSenderService.cs / UdpAudioReceiver.cs | 网络收发 |
| LocalLoopbackCaptureService.cs / LocalSessionMuteService.cs | 本地采样和会话静音 |
| ChannelFadeService.cs | 远程通道渐变 |
| MixerSampleProvider.cs / AudioOutputService.cs | 混音与播放 |
| wwwroot/ | 网页界面 |

`src/SoundTransportation.Shared/` 包含协议与采样格式转换。`SoundTransportation.Sender` 是独立发送项目，常规门店部署使用统一 Mixer 包。
