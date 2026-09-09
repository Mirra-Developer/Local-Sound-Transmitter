# Control App 接口

基础地址：`http://A的IP:5080`。以下路径均相对此地址，JSON 请求使用 `Content-Type: application/json`。

## 查询

| 方法与路径 | 用途 |
| --- | --- |
| GET /api/status | 配置中的启用状态、端口和传输格式；不是实际收包健康检查 |
| GET /api/channels | 通道列表，含 id、name、sourceIp、isLocalLoopback、muted、volume、effectiveVolume、outputEnabled、lastSeenUtc 等 |
| GET /api/channels/{id} | 单通道；不存在返回 404 |
| GET /api/config | 配置文件内容 |

## 选择唯一播放源

`POST /api/control/focus-channel`

```json
{
  "channelName": "B",
  "volumePercent": 100,
  "durationMs": 1000
}
```

可改用 `channelId` 或 `sourceIp` 定位。代码依次尝试 ID、IP、名称；建议每次只提供一种，且确保名称唯一。名称可能被发送端 Hello 更新，固定 IP 场景可优先使用来源 IP。

| 参数 | 行为 |
| --- | --- |
| volumePercent | 0 至 200；优先于 volume |
| volume | 0 至 2，1 表示 100%；两种音量均省略时默认 1 |
| durationMs | 0 至 60000 毫秒，默认 1000；0 为立即切换 |

超范围值会被限制到范围内。成功返回聚焦通道 ID、名称、目标音量及持续时间；找不到通道返回 404。

远程选中通道淡入，其他远程通道淡出后 Mute。此接口会启用远程通道 Output。启用本地应用控制时，同时静音 A 默认播放设备上符合条件的应用音频会话。

选择 A 应使用 `GET /api/channels` 中 `isLocalLoopback: true` 的通道 ID，或它的唯一名称：

```json
{
  "channelName": "A",
  "volumePercent": 100,
  "durationMs": 1000
}
```

本地 A 的当前实现是恢复应用声音，不执行该音量百分比或渐变；`volumePercent: 0` 也不能用来把本地 A 静音。详见 [架构与限制](ARCHITECTURE.md)。

## 单通道修改

`PATCH /api/channels/{id}`

```json
{
  "volume": 0.75,
  "muted": false,
  "outputEnabled": true
}
```

可选字段还有 `name`、`sourceIp`。这是运行时修改，不自动静音其他通道，也不等同于本地应用会话控制。不要用本地通道的 PATCH Mute 代替聚焦接口。Solo 已移除，不再发送 `solo` 参数。

`POST /api/channels` 可创建通道，字段为 `name`、`sourceIp` 及可选的 `volume`、`muted`、`outputEnabled`。创建和 PATCH 的状态不会自动写入配置。

## 保存配置

`PUT /api/config` 接收完整配置对象，包含 `receiver`、`transmitter`、`audio`。先 GET 获取，再修改需要的字段并完整 PUT；不是局部 PATCH。

成功返回 `saved`、`restartRequired`、`message`、`config`。UI 中旧名称对应的 JSON 字段仍为 `audio.localSessionMuteOnRemoteSolo.enabled`，保留是为了兼容历史配置，并不代表 Solo 功能仍存在。

当前接口没有认证或 TLS，只应在受控网络中使用，不直接暴露到公网。Control App 应检查 HTTP 状态码和响应内容，调用后可查询通道确认状态。
