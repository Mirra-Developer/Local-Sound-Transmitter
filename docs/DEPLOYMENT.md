# 部署与配置

场景：A 接收并输出到音响，B/C/D/E 发送各自电脑的声音。A 的本地应用作为一个整体控制。

## 安装与更新

1. 将 Windows x64 发布包完整解压，不在 ZIP 内直接运行 BAT。
2. 运行 `install.bat`，默认安装到 `C:\SoundTransportation`。
3. 安装脚本会关闭本软件进程并更新文件，保留安装目录内已有的 `appsettings.json`。
4. 安装结束不会立即启动。运行新版 `start.bat`，或在下次 Windows 登录时自动启动。
5. 右下角托盘图标右键选择“打开管理界面”；双击图标也能打开。退出请用托盘菜单。

新版 `start.bat` 优先启动 `C:\SoundTransportation` 内的 EXE；未安装时才启动 BAT 同目录的版本。避免继续使用旧包的启动脚本。

自启动通过 Windows 启动文件夹快捷方式实现，需要登录用户桌面，并非无人登录也运行的系统服务。`remove-autostart.bat` 可删除安装器创建的同名快捷方式；系统级目录可能需要管理员权限。再次安装会重新创建自启动。

直接运行 PowerShell 安装/更新脚本时，默认可能立即启动；加 `-NoStart` 可禁止。打包的 `install.bat` 已带该参数。

## 角色配置

| 设置 | A 接收端 | B/C/D/E 纯发送端 |
| --- | --- | --- |
| Receiver Enabled | 勾选 | 不勾选 |
| Auto create channels | 固定 IP 部署不勾选；临时发现可勾选 | 不需要 |
| Transmitter Enabled | 不向其他机器发送时不勾选 | 勾选 |
| Transmitter Targets | 不要填 A 自己 | A 的实际 IP 和接收端 UDP 端口 |
| Audio UDP Port | 默认 5055 | 可保持 5055 |
| Output enabled | 勾选 | 不勾选 |
| Mute local apps during remote focus | 勾选 | 不勾选 |
| Local channel enabled | 勾选 | 不勾选 |
| Local name | 建议 A | 不启用时无作用 |
| Local channel output | 不勾选 | 不勾选 |

A 上为 B/C/D/E 分别添加接收通道，Source IP 填对应发送电脑的实际来源 IP，并启用该远程通道的 Output。IP 改变后需要同步修改绑定。

发送端捕获 Windows 默认播放设备的混音，不是麦克风。网页、Arena、音乐软件需输出到对应的默认播放设备；ASIO/独占或其他设备的音频不保证能被捕获和静音。

`Local channel output` 新安装默认关闭。更新保留旧配置，因此以前手动勾选过的机器仍需检查一次。不要通过 A 发给 A 的方式重播默认设备混音，否则可能产生回响。

## 端口与保存

- 管理页和 Control App：默认 TCP 5080；本机访问 `http://127.0.0.1:5080`。
- 音频：A 默认接收 UDP 5055。发送端 Targets 中的端口必须匹配 A 的接收端口；各机器自己的接收端口不必强制相同。
- 防火墙需允许实际使用的端口；安装器目前没有自动配置防火墙规则。
- `Save Config` 保存配置并重载收发、输出和本地采集，可能短暂中断音频。出现失败时应排查，不要仅依据文件已保存判断服务正常。
- 更新前建议备份安装目录的 `appsettings.json`。桌面解压目录中的另一个配置文件不会自动合并到安装目录。
- Mixer 页面临时音量、Mute 和聚焦状态不等于持久化配置，不能保证重启恢复。
