# 故障排查

## 找不到界面

新版启动后不自动打开浏览器。先查看右下角隐藏图标，右键 Sound Transportation 打开管理界面。也可访问 `http://127.0.0.1:5080`。

网页不可访问时检查进程和端口：

```powershell
Get-Process SoundTransportation.Mixer -ErrorAction SilentlyContinue | Select-Object Id, Path
Get-NetTCPConnection -LocalPort 5080 -ErrorAction SilentlyContinue
Get-NetUDPEndpoint -LocalPort 5055 -ErrorAction SilentlyContinue
```

记录进程路径，确认是否启动了桌面旧目录。旧页面或 Solo 残留也可能是浏览器缓存；检查路径后尝试 Ctrl+F5。不能仅根据旧页面就断言网络音频故障。

## 发送端没有出现或没有声音

1. 发送端勾选 Transmitter Enabled，Target 为 A 实际 IP 和接收端口。
2. 发送端在 Windows 默认播放设备上播放声音。
3. A 勾选 Receiver Enabled 和 Output enabled。
4. 自动创建关闭时，A 必须配置与实际来源 IP 一致的通道。多网卡/VPN 环境需核对来源。
5. 检查远程通道 Output、Mute、音量和电平；Control App 可能已将其静音。
6. 检查 UDP 5055 防火墙规则、网络连通性和接收端口监听。网页 TCP 可访问不代表 UDP 音频畅通。

## 本地 A 不响或回响

- 回响时先降低音响音量，关闭 Local channel output，删除 A 发往自己的 Target。
- Control App 选择 A 的本地通道，确认本地会话控制已启用；再检查 Windows 音量合成器。
- 本地电平/红色状态不等于原始本地播放是否正常，参见 [已知限制](ARCHITECTURE.md)。
- 强制结束软件时可能来不及恢复应用静音；检查 Windows 音量合成器，正常退出优先使用托盘菜单。

## Control App 不能控制

确认请求发往 A 的实际 IP、端口 5080，使用 `POST /api/control/focus-channel`。先 GET 通道列表取得正确 ID，再检查响应是否 404 或服务器错误。不要继续使用 Solo 参数。

## 重启不运行、运行旧版

检查 `shell:startup` 和 `shell:common startup` 中快捷方式的目标及工作目录，应指向正式安装目录。只有登录桌面后才会自动启动。清理手动添加的重复旧快捷方式，确认未运行过删除自启动脚本。

## 报告问题时记录

提供安装包 `VERSION.txt`、进程路径、A/B 的角色配置、实际 IP、触发步骤、接口响应及是否有声音/电平。当前未配置统一持久化日志收集，不能假设安装目录一定存在日志文件。
