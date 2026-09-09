# Mirraflow / Sound Transportation / LobbyHub 接口契约

版本：`1.0.0-draft.1`　日期：2026-09-09　状态：P1 实施契约，P2/P3 设计边界。

文中 MUST/必须为三方必须遵守的规则，SHOULD/应为可记录理由后调整的建议。功能是否可用由运行时 capabilities 决定，不由这份文档的存在决定。

## 1. 产品目标与部署

同一台 Windows 播控电脑运行 LobbyHub、Mirraflow、Sound Transportation 接收/混音进程。外部游戏 PC 运行 NDI 画面发送端和独立的音频发送端。NDI 协议支持音频，但本项目的默认配置是独立画面与音频通道；不得假定每一路 NDI 含有声音。

用户最终从 Mirraflow 操作一个演出产品。代码、仓库和运行进程可以独立。首期保留 C# 音频引擎与 Rust 视频引擎；不因语言不同重写现有采集、网络与解码功能。

## 2. 唯一职责

| 服务 | 拥有的职责 | 不得拥有的职责 |
|---|---|---|
| 业务后台 | 场次、人员、真实游戏生命周期 | 根据媒体播放结束擅自推断游戏完成 |
| LobbyHub | Ready Room 图形、人物及信息动画、界面音效；提出演出意图 | 绕过 Mirraflow 直接控制音频焦点或另起正式转场计时器 |
| Mirraflow | 本地视频及音轨的媒体播放；视频合成；最终演出状态；画面/音频来源绑定；调度音频转场 | 每 20 ms 通过 HTTP 设置音量；收到音频成功就声称画面已经呈现 |
| Sound Transportation | 网络音频接收、本地音轨接入、逐采样混音与 Crossfade、音频设备输出 | 推进游戏状态；根据音量推断画面可用；集成模式下静音其他 Windows 进程 |

必须只有一个最终节目音频混音器。集成模式下，本地视频原声、Lobby 音效、远端音频及转场音效均进入该混音器；禁止同时直接向音响播放另一份相同音轨。远端音频在小窗口及全屏显示时仍复用同一个 audioTrackId。

## 3. 分阶段能力与禁止假报

| 能力 | P1 | 后续 |
|---|---|---|
| 版本/实例握手、音轨查询、音频采样时钟查询 | 实现 | 持续兼容 |
| 指定 A/B、linear/equalPower、按采样帧执行 Crossfade | 实现；一次一个转场 | 多组并行、限幅器、动态压低背景音乐 |
| Mirraflow 音频 HTTP 网关、Lobby Electron 客户端 | 实现；默认不自动接入正式业务 | 实际演出调度 |
| 音频块生成完成反馈 | 实现，状态名 rendered | 不得等同于声卡已播出或屏幕已显示 |
| 本地视频音轨 → 统一混音器 | 未实现 | P2 必须实现音轨解码、PCM 通道、seek/loop 同步 |
| Lobby 音轨接入及 GPU 画面桥接 | 未实现 | P2/P3；独立进程与纹理生命周期验收 |
| NDI 与独立 UDP 音频精确同步、跨 PC 时钟映射 | 未实现 | P2/P3 校准、抖动缓冲、时钟漂移校正 |
| 音视频原子转场、硬件播放位置反馈 | 未实现 | 完整时间映射和硬件测量后开启 |

旧音频 focus-channel 是旧版单独运行接口，不属于本契约。P1 新接口不能表示 fullProgramTake；任何调用方 MUST 检查 audioOnly，不能用音频转场代替全屏演出完成。

## 4. 传输、访问和版本

音频 API 前缀 `/api/av/v1`，Mirraflow 网关前缀 `/api/v1/av/audio`。所有字段使用 camelCase，JSON；整数采样帧必须在 JavaScript 安全整数范围内。

P1 集成默认关闭。音频进程使用 `AvIntegration:Enabled=true`（环境变量 `AvIntegration__Enabled=true`）和 `MIRRA_AV_TOKEN` 开启；Mirraflow 使用 `MIRRA_AV_ENABLED=1`、`MIRRA_AV_AUDIO_URL=http://127.0.0.1:5080` 和同一 `MIRRA_AV_TOKEN`。URL 必须为 IPv4 loopback HTTP，禁止自动重定向到另一主机。

新接口必须验证 Bearer token。该 token 只存在环境/本地受控配置与 Electron 主进程，不进入 React、URL、源码、日志或示例。Lobby 主进程调用 Mirraflow 网关。网关独立于现场端口可启动验证，不修改现有 Agent Bus V3 的动作白名单。

关闭时新接口返回 `503 INTEGRATION_DISABLED`。集成模式下旧音频管理写接口返回 `409 LEGACY_CONTROL_DISABLED`，防止旧定时器、音量面板、focus、配置重载与新引擎同时改同一音轨。旧读取接口仅用于诊断，不作为集成状态真值。

`protocolVersion` 固定为 `1.0`；不兼容请求返回 `422 UNSUPPORTED_VERSION`。响应允许添加字段，客户端忽略未知响应字段；请求未知字段拒绝。新增破坏性语义升级主版本。上层不得把所有 HTTP 2xx 当成演出完成。

## 5. 标识与来源绑定

- `engineInstanceId`：音频播放时钟的实例 UUID；进程启动及输出设备重建时改变。客户端必须重新握手，旧请求返回 `409 INSTANCE_CHANGED`，禁止自动重放。
- `requestId`：一次音频调度的 UUID；同一实例相同 ID+相同内容返回原结果，不重新执行；相同 ID+不同内容返回 `409 REQUEST_ID_CONFLICT`。P1 保留最多 4096 条，满后拒绝新请求，不清除旧 ID 后重放。
- `audioTrackId`：音轨 UUID，不能只依赖显示名。P1 映射现有 Channel.Id；发送端与来源 IP 的映射由配置确定。
- `sourceId`：Mirraflow 演出来源稳定 ID；后续绑定 `videoSourceId` 与 `audioTrackId`。同一 PC 可以有多路视频，因此不可只用 IP 自动选声音。
- `runtimeId`、`roundId`：真实业务上下文，由 Lobby/业务后台拥有。P1 纯音频网关不接受这两个字段，更不会代替业务校验。
- `runId`（P2）：媒体播放实例；seek、重播、循环边界需要明确时间轴连续性，不可仅按文件路径识别正在播放的音轨。

## 6. P1 音频接口

### 6.1 GET /capabilities

返回 protocolVersion、contractRevision、service=`sound-transmitter`、engineInstanceId、features。features 至少含 `audioCrossfade=true`、`sampleAccurateEnvelope=true`、`videoTransitions=false`、`localPcmIngress=false`、`avSynchronization=false`、`hardwarePresentationFeedback=false`。

采样级音量曲线只保证同一个生成音频块内的计算，不代表 Windows 调度、网络到达或扬声器发声的绝对实时性。

### 6.2 GET /clock

返回 engineInstanceId、sampleRate=48000、channels=2、nextFrame、outputActive、clockKind=`renderedAudioFrames`。nextFrame 是下一块待生成音频的起点，单调递增；不能当成声卡播放位置。无输出设备消费音频时 outputActive=false，不接受新 Crossfade。

### 6.3 GET /tracks

返回 tracks 数组，每项包含 audioTrackId、name、sourceIp、isLocalLoopback、queuedFrames、audioReady、gain。P1 readiness 必须同时有非空 PCM 队列和最近 500 ms 内收到的音频数据；Hello/名称被发现不等于音频就绪。静音音频的零值样本仍是有效数据。

### 6.4 POST /crossfades

```json
{
  "protocolVersion": "1.0",
  "requestId": "27d2c815-54a8-4218-a62c-9631293d53cc",
  "engineInstanceId": "217f41b2-3041-433e-a635-9b8f2e17699b",
  "fromTrackId": "76c8b986-08a6-41a5-9ed1-4fa9ac3cd2f9",
  "toTrackId": "bef03385-b5ae-494d-94be-ea15282a1b66",
  "startFrame": 96000,
  "durationFrames": 48000,
  "curve": "equalPower",
  "toGain": 1.0
}
```

请求字段的机器可读定义见同目录 `crossfade.request.schema.json`。startFrame 使用本实例音频帧时钟，上限为 9007199251860991；至少预留 4800 帧（100 ms），最多预排 240000 帧（5 秒）。durationFrames 范围 1..2880000（60 秒）。A/B 必须不同、均存在且 audioReady，禁止对本机扬声器 loopback 通道调度。A 必须当前可听、B 必须当前静音/零增益；否则返回 `409 INVALID_GAIN_STATE`，避免开始时突变。

toGain 为有限数 0..1。音量余量由混音增益管理；equalPower 不保证相关信号不叠加过载。P1 最终仍用现有幅度钳位，必须用保守素材电平验收，不把它称为限幅器。

请求成功返回 `202` 和操作状态；这里只表示安排成功。A/B 以外的音轨不改变。P1 活跃转场期间新 ID 返回 `409 TRANSITION_BUSY`。不得取消旧请求来偷偷执行新请求。

曲线：p=clamp((frame-startFrame)/durationFrames,0,1)。equalPower 的 A=A0*cos(p*pi/2)，B=toGain*sin(p*pi/2)；linear 的 A=A0*(1-p)，B=toGain*p。两路必须在同一个输出回调内逐帧计算，不能靠 HTTP 或后台 20 ms tick 调整。

开始帧时再次验证目标音频就绪；失败保留 A。执行中若 B 缺帧，P1 失败并保留 A，不继续淡出旧来源；这是保守故障行为，不保证异常恢复瞬间无音量跳变。状态包含 code 和实际失败 frame。A 缺帧时该帧 A 输出静音，并继续向有效 B 过渡；P1 尚未暴露 underrun 计数器。结束后 A 的增益为 0，B 为 toGain；仅关闭 A 的声音，不停止外部发送端。

### 6.5 GET /crossfades/{requestId}?engineInstanceId=...

返回 requestId、engineInstanceId、audioOnly=true、state、startFrame、durationFrames、fromGain、toGain、renderedThroughFrame、code。

state：`scheduled → running → rendered`；`scheduled → cancelled`；准备/执行/输出中断可进入 `failed`。rendered 指过渡音频已写入生成缓冲区，禁止展示成“现场音视频转场完成”。无硬件呈现证据时不能返回 presented/complete。

状态查询要求实例 ID，实例不匹配返回 409。超时后必须原 ID 查询/重试，不能换新 ID 重新执行。P1 没有事件流；状态轮询用于观察，不驱动音量曲线。

### 6.6 POST /crossfades/{requestId}/cancel

请求只有 engineInstanceId；仅 scheduled 可以取消；running 返回 `409 ALREADY_RUNNING`，要求未来显式恢复操作。终态重复取消返回现有状态。重建输出使实例失效，旧实例请求不得继续。

### 6.7 错误

业务错误使用 JSON `{ "protocolVersion":"1.0", "code":"...", "message":"..." }`；框架层拒绝的畸形路径、参数或请求体可能只返回 HTTP 错误，客户端也必须安全处理非 JSON 错误。400 JSON/绑定格式错误；401 未授权；404 音轨/请求不存在；409 实例、状态或 ID 冲突；422 版本、参数错误；503 关闭、无输出、来源未就绪、引擎不可达。Mirraflow 网关超时返回 504 `AUDIO_ENGINE_TIMEOUT`，请求结果不确定，必须查询原 ID。网络失败不假报未执行或自动生成新请求。

## 7. P2 数据面契约设计（未实现）

音频：48 kHz float32 little-endian stereo PCM，音轨独立，携带 engineInstanceId、audioTrackId、runId、sequence、sourcePts、frameCount、discontinuity。通过有界本地数据通道传输（候选 named pipe/shared memory），不将音频块放进 REST JSON。必须有回压、迟到/丢包策略和最大缓存限制。最终传输布局另行发布 `localPcmIngress` 能力版本，未实现前禁止调用方发送。

本地视频播放器拥有其文件音轨的 PTS；pause/seek/loop 要同时更新声音和画面。音频引擎拥有设备样本时钟，Mirraflow 建立媒体 PTS 到设备播放位置的映射。UTC 与自定义 UDP 的打包时间不能直接当采集时间或跨 PC 同步依据。第一步测量并校准固定延迟；然后验证抖动缓冲和采样时钟漂移。音频先到时延迟音频，视频先到时应缓冲视频，不能要求负音频缓冲。

画面：Lobby 输出 renderInstanceId、frameSequence、monotonic timestamp、尺寸、格式、alphaMode、GPU adapter 标识和共享纹理引用。句柄跨进程必须正确复制，帧不能在消费方释放之前回收；禁止把原进程 HANDLE 数字直接当全局句柄。静止页面没有新帧时保留最后有效帧，独立进程心跳检测故障；后台重启需换实例，旧纹理一律失效。

媒体子合成 → Lobby 视频区域 → 完整 Lobby → 最终 Program 是有向无环链路；最终 Program 不回流到子合成。保留原 CSS 3D、人物、布局、遮挡和动画属性单一所有者。

## 8. P3 演出事务（未实现）

演出请求必须指定 sourceId、runtimeId/roundId、转场素材 ID、实际遮挡点、音频 A/B、crossfade offset/duration、失败策略。Mirraflow 先准备音频/视频/特效，再提交执行；目标未就绪时保留旧画面和声音。切换点由素材明确提供，不默认视频中点。声音与画面可以使用不同曲线和持续时间，但共用可映射时间轴。

本地视频、外部 PC、Lobby 音效分别拥有音轨/分组。跨到 PC A 时只修改声明的 A/B，转场音效继续独立播放。手动导播覆盖、cancel、stale round 拒绝必须进入同一调度器，不能有另一个软件私下写主输出状态。先有可审计的 per-service ack 与失败恢复，再宣称整体转场完成；跨进程不承诺天然原子提交。

## 9. 联合验收

P1：三份契约 SHA256 相同；C#/Rust/Node 接口验证；sample ramps/中间重叠/其他音轨保留；重复请求、冲突、实例变化、忙碌、取消、目标断流；关闭模式无现场影响。无声离线 PCM 测试必须标明是软件结果，不等于现场播放。

P2：有声本地视频与一路远端 PC 音轨进入同一混音器；真实输出电平、剪切/爆音、音画误差、暂停/跳转/循环、设备恢复。用闪光+短音录制实际大屏和音响。

P3：完整 Ready Room 与 NDI 的有声全屏转场，再增加内部视频区域；目标分辨率/帧率下 100 次切换及至少 2 小时运行，记录最慢切入、音视频误差分布、丢帧、underrun、显存/内存增长。实际现场时长验收通过前不能声称全天稳定。

## 10. 共同开发规则

三项目独立分支，接口改动同步文档、示例、客户端和测试。禁止以不同项目各自实现的定时器代替共享调度。每次联调记录三个提交/工作区版本及契约哈希；部署为一个版本组合。不开启旧全局静音行为，不改真实业务启动权限，不自动重启现场服务，不覆盖未提交改动。只有实测证据支持的能力才返回 true。
