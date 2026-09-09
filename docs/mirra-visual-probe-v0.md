# Mirra 三方视觉验证契约 V0

版本：`mirra.visual.probe/0.1`，2026-09-09。状态：独立诊断协议，不是正式节目 API。

本文件约束 LobbyHub、Mirraflow 与 Sound 对本轮 V0 实验的共同理解。它补充并取代旧 P1 文档中 P2/P3 尚未实施的“双向视频往返”首选设计；P1 音频接口、版本、Schema、鉴权与能力握手保持不变。正式媒体链路仍以 `visual-first-integration-plan.md` 为目标：Lobby 提供 UI，Mirraflow 直接解码媒体并完成最终合成。

## 隔离边界

- Lobby：仅独立 `electron/visual-probe/host.cjs` 会加载 `page.cjs`，要求 loopback 的 DEV Ready Room 时间线。不修改正式 Electron main、启动解析器、React 组件、视频恢复、业务 Socket 或原 CSS 文件。
- Mirraflow：仅 `cargo build -p mirraflow-server --example lobby_visual_probe` 生成实验执行文件。主服务及 native-stage 不导入该实现，不新增监听端口，不读写节目场景、输出、转场或 NDI 配置。
- Sound：本轮不调用任何音频 API，不改变声卡、混音和来源绑定。只保存本契约副本；正式 `lobbyTextureIngress` / `avSynchronization` 能力仍为 false。
- 当前诊断的 CPU 合成与 PNG 留证不能作为生产画面传输，也不构成 60 fps 或实际大屏验收。后续不得把实验结果直接接入节目。

## 实验数据与生命周期

Host 启动自己的 native child，以标准输入/输出私有管道传递逐行 JSON。native 进程启动参数固定 owning Electron PID 和本次证据目录，不接受网络请求；NDI、音频及正式场景不参与。单条命令上限 64 KiB，导入纹理上限 3840×2160，解码 fixture 仍限制在 2048×2048 内，最多暂存八张诊断图。

`capture` 字段：`key`（本地文件安全标识）、`handle`（十六进制 NT HANDLE）、`width` / `height`（codedSize）。Host 仅接受 Electron BGRA frame；native 使用 `DuplicateHandle` 从启动时指定的进程复制句柄，在实际能够打开资源的 DXGI adapter 上导入，并记录 adapter LUID。不能用适配器序号或 GPU 名称替代共享资源兼容检查。

native 将纹理复制到 staging 并等待 Map 完成，释放导入资源与复制句柄后返回 ACK；Host 此后才调用 `texture.release()`。只有一个在途命令。闲置且未选中的纹理立即释放。超时时先结束消费者，再允许生产者释放被消费者使用的资源；进程退出或管道 EOF 结束会话，不沿用旧句柄。

静止页面不保证继续产帧。诊断在画布外左上角放置 4×4 新鲜度标记，确认响应对应本次请求，旧标记会重试；该区域在对比前统一清零。它仅用于实验，不能替代未来独立心跳及固定节奏的最后一帧复用机制。

`decode`：Mirraflow 通过实际 GStreamer playbin 解码本地 FFV1/MKV 测试文件，抽取八个 RGBA 帧与 PTS，音频 sink 为 fakesink。使用合成移动色条及 alpha=0/128/255 的边框素材，不能据此承诺任意编码支持透明视频。

## 几何、遮挡与颜色验证

每个时间点通过已有 DEV 时间线定位；不复制 cue，不驱动真实游戏。快照组在同一个暂停的页面状态下取得：透明底、黑、白、低强度 R/G/B 及浏览器直接放置同一解码帧的参考画面。每个组有唯一 prefix 与 timeMs，不能跨组复用布局。

页面导出实际投影后的四角（左上、右上、右下、左下），单位为输出像素，原点左上。native 用逆单应变换采样原始媒体；不是 axis-aligned bounding rectangle。网页边框、扫描线、人物和地板遮挡参与真实 DOM 渲染，不从最终画面挖矩形洞。

普通视频位于媒体原容器内。透明装饰实验明确使用内容外扩范围：横向各 32、纵向各 24 个局部 CSS px，放到原 frame-shell 中，位于 frame 裁切之外且仍在原地板遮挡内；读取原媒体的 transform、opacity、filter，复用现有姿态。这个数值仅是 fixture 放置规则，正式素材必须明确提供自己的内容区域和装饰范围。

诊断合成使用 RGB 响应样本估计 CSS 颜色混合，保留点亮过程中 brightness / saturate 顺序以及 brightness 的输入裁限。媒体双线性采样先预乘 alpha，避免透明边缘 RGB 污染。该计算用于证明/否定当前页面可分解，不是已承诺支持所有 CSS backdrop-filter、非局部 blur 或其他非线性特效的通用分层算法。

## 验收与下一关

报告逐样本记录 activePixels、媒体区域 activeMae（8 位通道误差）、activeBadFraction（任一 RGB 通道差大于 12 的比例）、区域外最大差及 adapter。诊断门限：activeMae < 4、activeBadFraction < 0.06、outsideMax < 20。完全隐藏的样本单列，不能用零像素误差证明媒体已显示；至少应覆盖普通与 Alpha 两类实际可见样本。

必须同时检查合成、直接参考和差异图；一次通过不能推断真实素材、自然连续动画、多窗口、60 fps 或跨 GPU 成功。原路径须继续构建及回归，正式产物应保持一致。

进入 V1 前仍需：持续 GPU 合成/呈现、无需多次暂停捕获的逐帧 UI 导出、独立存活心跳、最后一帧复用、反压和资源池、实际 Alpha 素材、真实 NDI 预热/断流，以及转场素材的统一切点。不得用本诊断 PNG 或 CPU readback 接管正式 Program。

本文件的编辑源是 Lobby AV 工作区 docs；同步到 Mirraflow、Sound 和本机联合目录并核对 SHA256。

## 4K 同画面对照扩展（2026-09-09）

显式 `host.cjs ... --4k` 或本机 `Test-VisualV0.ps1 -FourK` 开启。默认 720p 诊断入口保留。仍使用 1280×720 CSS 布局，Electron offscreen.deviceScaleFactor=3 直接渲染出 3840×2160 共享纹理；导出的 CSS 四角乘以 3 后才传给 native。不能把 720p PNG 拉伸后称为本测试的 4K 输出。

新增 A 参考来自独立 Electron 离屏窗口的 GPU 加速 bitmap `paint` 输出，直接保存为 `*-browser.png`，不经过 Mirraflow；使用相同 URL、素材、时间和 3 倍渲染密度，并校验 PNG IHDR 为 3840×2160。本机 `capturePage()` 实测只返回 1280×720，因此不作为 4K 参考，也不将其放大。B 为 `*-composite.png`：全场分层 UI 通过共享纹理导入，Mirraflow 独立 example 再加入原始解码帧并做 CPU 参考合成。`compose` 新增可选 `browserReference`（安全文件标识，无扩展名）；提供时，统计和差异图改与这张独立截图比较，不提供时保持原有参考来源。所有比较图要求尺寸相同。

测试素材可用 `make-visual-fixtures.cjs <新目录> --hd` 生成 1920×1080 色条及半透明边框视频。4K 指整场输出，不能推断既有角色图片或测试视频本身也有原生 4K 细节。普通 / Alpha 两类各取 3.2、6.0、9.6、16.0 秒；隐藏媒体的 VS 样本单列，不计为媒体可见验证。

`make-visual-comparison.cjs <完整结果目录>` 生成无外部依赖的 `index.html`，可以直接打开，或通过只监听 loopback 的 `serve-visual-comparison.cjs` 查看。提供 A/B 分界线、100% 像素、差异图和原图下载；该查看页不属于正式 Lobby 入口。它展示静帧证据，不代表 Mirraflow Program 已经播放完整动画，也不包含实际 NDI 或 stinger 转场验收。

参考：[Electron 离屏输出](https://www.electronjs.org/docs/latest/tutorial/offscreen-rendering)、[共享纹理资源释放](https://www.electronjs.org/docs/latest/api/structures/offscreen-shared-texture)、[Windows NT HANDLE](https://www.electronjs.org/docs/latest/api/structures/shared-texture-handle)。
