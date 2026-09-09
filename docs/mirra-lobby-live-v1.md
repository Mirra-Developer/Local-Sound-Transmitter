# Mirra Lobby 实时整场输出 V1

日期：2026-09-09。协议 `mirra.lobby.live/1`。这是独立本机诊断实现，不是正式 Program API；不改变原媒体链路、NDI、Sound 路由或 P1 能力声明。

## 本轮实现

Lobby 实际 DEV Ready Room 组件连续演出 → Electron 43.4.0 离屏 BGRA GPU 共享纹理 → Mirraflow 独立 `lobby_live` example → 同 GPU 的私有最新帧纹理 → DXGI swap chain → Windows 原生输出窗口。

正常播放不导出逐帧 PNG，不执行像素 GPU readback、CPU 合成或视频编码。原始页面的动画、CSS、素材和共享时间线不作修改；独立宿主只隐藏查看器控件并操作已有重播、暂停和循环按钮。教学框暂保留网页占位内容。

支持 1920×1080 / 3840×2160，目标 30 / 60 fps。1280×720 CSS 布局使用 1.5 / 3 倍离屏渲染密度直接生成对应尺寸纹理。输出 swap chain 与源纹理等大；原生查看窗口的客户区为 1280×720，DXGI 缩放显示，不将本机窗口观察当作物理 4K 大屏验收。

## 隔离入口

- Lobby：`electron/live-output/host.cjs`，与正式 `electron/main.js` 无导入关系。
- Mirraflow：`cargo build -p mirraflow-server --example lobby_live --locked`，正式 server / native-stage 不导入该 example。
- Sound：仅持有此契约副本，本轮不调用音频功能。
- 本机脚本：`C:\Mirra_Dev\mirra-av-integration\scripts\Start-LobbyLive.ps1`、`Stop-LobbyLive.ps1`。
- 所有 session、日志、独立执行文件、截图和报告位于 `C:\Mirra_Dev\mirra-av-integration\verification\visual-v1\<run>`。使用全新 run 名保存历史证据。

## 连接与资源生命周期

原生程序仅绑定 `127.0.0.1`，默认 TCP 17710；每次启动产生新的随机 token，写入本次 `session.json`。该文件是本地凭据，不复制到文档、浏览器或 Git。每条 JSON 命令都携带 token，单行最多 4096 字节，最多四个连接和 16 条排队命令。未认证连接有读取超时；已认证来源可因暂停而长期不发送画面。仅供同机可信测试宿主，不向局域网开放。

`hello {pid}`：打开实际 Electron 主进程的句柄复制权限，产生新 epoch，并重置该 epoch 的序号；已有画面保持，直到新有效帧到达。新 epoch 首帧前标记 `reconnecting-holding`。不沿用旧 PID 或旧句柄。

`frame {epoch,seq,handle,width,height}`：单调递增 seq，格式限定 BGRA8、单数组层 / 单采样 / 单 mip，尺寸必须与会话一致。拒绝旧 epoch、重复或倒退序号和尺寸错误，再尝试导入句柄。native 通过 DuplicateHandle 和实际可以打开资源的 adapter 选择设备，记录 LUID。

native 将共享资源复制到私有最新帧纹理，用 D3D11 EVENT query 等待 GPU 复制完成后才返回 `gpuCopyComplete` ACK。Electron 收到匹配 epoch / seq 的 ACK 后 release。只有一个在途源帧；native 忙时其他 paint 立即 release，不积累纹理队列。每个源帧重新导入，不把可复用句柄数值当作永久资源身份。

如果超时或 ACK 含义不确定，宿主保留这一张纹理且停止继续提交，不能把超时当作释放许可。安全关闭顺序为先停止本次 native 消费进程、确认退出，再结束宿主。停止脚本核对 PID 的可执行文件 / 命令行归属，不结束其他服务。

## 输出节奏与存活

native 的呈现时钟独立于网页 paint，以目标 30 / 60 fps 调用 Present；无新帧时复用私有最新帧。落后时跳过错过的 deadline，不用突发追赶占满线程。GPU 导入、复制及呈现由同一原生线程按序执行，每轮控制请求有处理上限。Windows 窗口消息在独立线程处理，状态文件由有界队列交给写入线程，窗口标题也使用异步消息更新，避免拖动窗口或磁盘写入直接占用呈现线程。

`heartbeat {epoch}` 走独立于帧 ACK 的控制连接，宿主每约 250 ms 检查 renderer 是否仍响应。只保留一个待完成的页面探测，renderer 挂起时不会无限累积请求。心跳不靠画面改变触发。

- `live`：有当前来源画面且心跳正常。
- `live-no-new-frame`：心跳正常但没有新画面，包括暂停或静止页面；不视为故障。
- `disconnected-holding`：心跳超过 1500 ms 未更新，仍显示最后一帧。
- `waiting-source` / `waiting-frame`：初始等待，不伪造可用节目。
- `reconnecting-holding`：新来源已握手，保留旧图等待其第一帧。

原生输出独立存活，源渲染窗口销毁或宿主连接断开不会清空输出。新宿主可用同一原生会话 token 重新 hello；恢复来源必须收到新 epoch 的实际帧才算完成。当前恢复按钮重建 Lobby 渲染窗口；宿主进程的自动拉起尚不属于正式服务管理。

## 控制与测试

浏览器控制页默认 `127.0.0.1:14181`，无回传视频。状态读取与控制分开；修改必须 POST 并提供本次页面 token，不接受跨域 CORS。支持重播、暂停、播放、断源、恢复、重建 renderer、重置统计和手动证据截图。

`status` / `resetStats`：报告实际来源接收帧率、Present 调用节奏 / p95 / 最大间隔、重复呈现数、错过的 deadline、GPU 导入复制耗时、native 进程的 GPU 本地内存用量，以及源 heartbeat / epoch / seq。统计分布最多保存 7200 个样本，计数继续累计。呈现调用不等于物理屏幕扫描，复制耗时也不等于端到端显示延迟。

`snapshot {key}` 是显式 QA 操作，回读私有输出纹理并保存 PNG；它不是屏幕扫描截图，也不在持续播放性能采样期间使用。执行后统计会受影响，须 resetStats 后再测。`shutdown` 只关闭该独立输出进程。

`testUiBlock {millis}` 仅用于诊断回归：让窗口消息线程停顿 1–3000 ms，禁止重叠执行；状态提供进行中标记和完成计数。测试在持续采样中注入 2000 ms 停顿，要求完成计数增加且最大 Present 间隔小于 250 ms。它验证线程隔离，不等同于覆盖所有真实拖拽、DWM 或驱动行为。

`scripts/av-integration/test-live-output.cjs <run-dir> <seconds>` 实际播放完整循环，测量后检查：呈现调用至少目标 98%、实际来源帧至少目标 95%、非遮挡、GPU copy p95 小于一帧、无源所有权错误；再验证暂停仍存活、断源前后截图哈希相同、新 epoch 恢复和旧 epoch 拒绝。失败与历史报告保留，不扩大门限掩盖问题。

本阶段仍不验收：任意素材编码、教学框独立媒体合成、Alpha 分层、NDI 预热 / 断流、框内或全屏 stinger、音频同步、跨 GPU 桥接、长时间现场播控，以及真实大屏扫描延迟。下一阶段应在实测通过的持续 GPU 基础上接入教学媒体窗口，不能回退为 PNG 或 MJPEG 主链路。

本文件以 Lobby AV 工作区 docs 为编辑源，复制到 Mirraflow、Sound 及本机联调目录核对哈希；不修改 P1 音频契约。

## 2026-09-09 实测记录

RTX 5090 Laptop GPU，同一 adapter。以下均为独立诊断进程的短时测量，非正式节目输出验收。

| 会话 | 采样秒数 | Present 调用/秒 | 来源帧/秒 | 最大呈现间隔 | 结果 |
| --- | ---: | ---: | ---: | ---: | --- |
| live-1080-60-04 | 25.29 | 59.98 | 59.27 | 31.80 ms | 9 项通过；窗口线程分离前的基线 |
| live-4k-30-02 | 35.36 | 30.01 | 29.92 | 38.55 ms | 9 项通过 |
| live-4k-60-01 | 40.40 | 59.80 | 59.06 | 41.57 ms | 10 项通过，含窗口线程停顿测试 |

4K60 的 copy p95 为 2.15 ms，原生进程 GPU 本地用量约 136 MiB；此数不包含 Electron 等其他进程。采样内仍有 9 个错过的 deadline，不能写成零掉帧。来源窗口恢复至新 epoch 的第 6 张以上实际帧约 655 ms。额外完整结束并重启 Electron 主进程后，native PID 保持不变、断源截图哈希完全相同、epoch 2→3 并恢复播放；该测试使用受控重启，没有实现自动进程守护。

保留失败会话 `live-4k-30-01`：曾出现约 2.224 秒呈现间隔，用户同期拖动过窗口，但不能据此唯一归因。线程分离后增加主动停顿测试，不删除旧证据或放宽原帧率门限。

证据位于各会话的 `acceptance.json`；4K60 另有 `host-process-recovery.json`、`restored-live.png`。通过 55 项 Lobby 回归、4 项 native 单元测试及隔离目录生产构建。后续框内 NDI / 视频转场按视觉计划 V1b 继续，不能把本实时整场桥接当成已完成转场。
