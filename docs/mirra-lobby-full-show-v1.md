# Ready Room / Mirraflow 完整视觉流程契约（本机实验）

2026-09-09。本文记录 `lobby_show` 隔离入口，补充原整场桥接和 NDI 转场契约。声音仍留在 A2 阶段。运行结果见同目录 `mirra-lobby-full-show-validation.md`；设计和测试能力不能当作正式部署能力。

## 节目流程

1. 加载本地真实角色卡与教学视频，预热 NDI、三条 Alpha 特效和教学首帧。
2. Ready Room 从第一阶段开场，角色卡和教学框按原网页时间线入场。教学出现时，由 Mirraflow 开始播放本地教学文件，一次播完。
3. 收到本轮教学的实际 EOS 后，在教学框内播放 `slot` 特效；完全遮挡时，框内视频切为占位 OB NDI。外围 Lobby、边框、人物保持连续。
4. 框内 NDI 展示 1.5 秒，再发独立 `ready.stage3` 显示指令：执行原第三阶段收框、角色卡归位和 VS 动画；进入最终姿态后持续原有呼吸/光效，不重复入场撞击。
5. 第三阶段指令呈现确认后约 7 秒，用另一条 `full` 特效把整个节目切至同一路 NDI。
6. 全屏 NDI 在转场完成后保持 10 秒；后台准备下一轮教学首帧和 Lobby 初始姿态。
7. `return` 特效切回已准备的 Lobby；特效结束后开始第一阶段，保证开场不在全屏 NDI 背后提前跑完。此时才累计完成一轮，然后重复。

三个特效各约 1 秒、60 fps、1280×720 FFV1/MKV Alpha；实际切点 500 ms，400–650 ms 完全遮挡。不同方向/颜色用于识别框内、全屏和返回。它们是测试资产，正式素材另行验收。

## 画面路径与所有权

- Lobby 使用现有 `ReadyRoomPage`、本地资源解析器和共有动画时间线。唯一对正式组件的接口补充是可选 `mediaContent`；不提供时沿用原 video 路径。
- 本机 DEV `av-show.html` 提供两个同步的原网页视图：教学内容平面分别填黑和填白，其余场景一致。物理 GPU atlas 为 7680×2163，其中左右各一张 3840×2160，底部是帧内元数据。
- Mirraflow 原生播放教学，持续接收 NDI。GPU shader 用教学四角的逆投影把媒体放进框内，再以 `black + (white-black) × media` 重建网页前景、透明边缘与遮挡响应。框内特效与媒体使用同一坐标。
- 镜头、边框、人物、地板遮挡归 Lobby；媒体解码、帧选择、特效切点和最终 Program 归 Mirraflow。整个节目最后进入现有 `DxgiPresenter::present_layers`。
- 网页每帧的几何、滤镜参数、帧号和命令号随同一 GPU 纹理传送，不依赖另一路高频 JSON 坐标。GPU 复制完成后才归还 Electron 原纹理；静止/短时缺帧复用最后纹理，独立心跳判断存活。
- 正常 GPU 桥接没有视频编码或像素 readback。教学和当前 NDI 解码仍为 CPU BGRA/UYVY 帧上传，不能称为整条路径零复制。显式截图会 readback，必须排除在性能验收之外。

这是用于当前 Ready Room 单一教学平面的实验合成办法，不是任意网页的通用无损分层导出器。非线性混合、backdrop-filter、复杂透明回放边框、多媒体窗口和 HDR/色彩管理需要另外验证；当前亮度/饱和度变换不能证明任意颜色逐像素一致。两张网页视图也增加 GPU/网页负载。若后续要求严格颜色或更多媒体区域，应升级显式前景/背景/遮罩图层接口。

## MES 语义与本地数据

已核对 `Mirra-Developer/mirra-edge-service` 的 master 提交 `a4d06330d21d34253f868b9a5af02183e5755501`。`scene.ob` 属于 `osc/single`，`params.key` 使用游戏 catalog 的 `internalName`，不使用旧的 `ob_scene`。现有现场绑定可转成 `/composition/columns/.../connect`。实验中把这项意图映射到本机 Mirraflow，不发送现场 OSC，也不发 `game.start`。

本地 fixture 仅提供以下只读、MES 形状的数据接口，沿用 Lobby 正式解析器：

| GET 路径 | 内容 |
|---|---|
| `/v1/internal/runtimes/active/teams` | 两队四位测试玩家及角色资源标识 |
| `/v1/internal/runtimes/active/positioning` | A1/A2/B1/B2 位置 |
| `/v1/internal/lobby/active-runtime/tutorial` | 测试游戏和教学本地 URL |

资源根目录为 `C:\Mirra_Dev\MirraLobbyHubResources`。本轮使用 zh 目录的 Firebird、LowPoly_Panda、Fox_ls、LowPoly_Frog，以及 `videos/gaming/teaching/CN_LaserRoom_HD.mp4`。这些是实际本地素材，玩家队伍和当前游戏是测试 fixture，不是现场活动中的 MES runtime。`Laser Room` 为本地 fixture key，正式联调必须读取现场 catalog 的真实值。

`commands.cjs` 校验 Agent V3 的八字段执行 envelope：`dispatchId, requestId, service, segment, action, params, timeoutMs, sentAt`。先记录接收 ACK，再记录八字段 result：`dispatchId, requestId, service, segment, status, message, data, completedAt`。ACK 不代表转场完成。

| service / segment / action | 本地解释 | 状态 |
|---|---|---|
| `osc / single / scene.ob`，`params.key` 为当前游戏 | 按本地执行上下文 `scope=slot/full` 切至同一路 NDI | 复用已存在 MES 意图 |
| `lobby / single / tutorial` | 准备 Ready Room 第一阶段 | 复用显示意图，隔离入口的执行适配 |
| `lobby / single / ready.play` | 从已准备的开场开始 | 新增的实验显示命令 |
| `lobby / single / ready.stage3` | 开始第三阶段并保留最终循环光效 | 新增的实验显示命令 |

`scope` 和 `run` 是适配器本地上下文，没有向 V3 envelope 私增字段。重复 requestId 执行一次；同 ID 改意图被拒绝。过期命令不执行；执行超时返回 `LOCAL_SHOW_TIMEOUT_UNKNOWN`，必须查询状态再决定下一步，不能自动重发新的 ID。适配器目前在隔离宿主内调用和记录，**尚未连接现场 `/edge` Socket.IO Agent Bus**。本文不表示 MES 已部署两个新显示命令。

## 原生会话约定

沿用 `mirra.lobby.live/1` 的本地认证和 GPU 所有权握手，session.mode 为 `lobby-show`。令牌只保存在会话目录，不提交 Git。

| op | 参数 / 结果 |
|---|---|
| `media` | `action=prepare/play/pause`、`run`。prepare 回到首帧并把框内来源设为 video；play 只接受已准备的同一轮 |
| `take` | `scope=slot/full`、`target=video/ndi/lobby`、`effect=slot/full/return`、`run`；有效组合由合成器校验 |
| `status` | 媒体 ready/playing/ended/PTS/run、NDI 新鲜度、active/slot、转场事件和帧间隔 |
| `snapshot` | 安全文件 key；仅安排 QA 截图，必须等 PNG 实际落盘才算取得证据 |
| `resetStats` | 截图、构建或故障注入结束后重置正常运行测量窗口 |

转场只在素材已准备、目标有新鲜帧时接受。同目标返回 alreadyActive；已有转场时拒绝 busy；旧 run 拒绝。NDI 需收到超过两帧且最新来源帧小于 500 ms。切点跟随实际特效 PTS，错过不透明区间则取消，不能黑切。Program active 仅在 Present 成功后更新。

NDI 在切点前失效时保留原节目；切入后失效时保留最后一帧，并允许转回仍可用的 Lobby。EOS 必须匹配本轮 run。停止会中断等待、阻止下一阶段，并暂停教学；已开始的特效允许完成后停止。停止自动流程不等于关闭服务或冻结所有网页光效。

## 本机启动与查看

工作区：Lobby `C:\Mirra_Dev\mirra-lobby-hub-av-integration`，Mirraflow `C:\Mirra_Dev\Mirraflow`；原工作区及旧 14179–14182 链路不替换。

```powershell
& C:\Mirra_Dev\mirra-lobby-hub-av-integration\scripts\av-integration\local-workspace\Start-FullShow.ps1
```

打开 `http://127.0.0.1:14185/`，点击开始。看标题为 **Mirraflow Full Show** 的原生窗口；控制页不是最终节目画面。内部输出缓冲 3840×2160，窗口以约 1280×720 缩放展示，不能等同实体 4K 大屏扫描验收。NDI 为本机 SDK 的 `Mirra-OB-Placeholder` 彩条/移动标尺信号，1280×720、发送目标 60 fps，非真实 OB 电脑。教学文件约 40 秒、25 fps，不能因输出 60 Hz 宣称教学有 60 个独立画面。

端口：14183 为隔离 Vite，14184 为只读 fixture，14185 为控制页，17712 为原生 IPC。控制按钮使用会话令牌。结果、进程清单、日志、PNG 和素材放在 `C:\Mirra_Dev\mirra-av-integration\verification\full-show`，不进入 Git。

关闭时使用启动输出显示的本轮目录：

```powershell
& C:\Mirra_Dev\mirra-lobby-hub-av-integration\scripts\av-integration\local-workspace\Stop-FullShow.ps1 -RunDirectory 'C:\Mirra_Dev\mirra-av-integration\verification\full-show\本轮目录'
```

脚本只停止核实过的本轮进程及子进程。三个仓库各自管理版本；Sound 本轮只同步视觉契约，声音服务未参与。没有替换正式 Mirraflow Editor 入口、现场 MES、OSC 绑定或 Lobby 已有部署。
