# Sound Transportation

## 中文项目文档

部署、维护和开发请从 [文档索引](docs/README.md) 开始。文档按当前代码整理，已实现功能和待实现事项分开记录。

- [部署与配置](docs/DEPLOYMENT.md)：安装、更新、自启动、A/B/C/D/E 配置。
- [Control App 接口](docs/CONTROL_API.md)：请求示例、参数、运行状态与持久化。
- [音频架构与限制](docs/ARCHITECTURE.md)：本地与远程路径、源码位置。
- [故障排查](docs/TROUBLESHOOTING.md)：无声、旧页面、控制失败、自启动。
- [开发与待办](docs/DEVELOPMENT.md)：构建发布、检查清单、Limiter 计划。

当前本地 A 使用 Windows 应用音频会话静音/恢复，不支持与远程通道相同的音量渐变。具体限制见中文文档。

LAN audio routing MVP for multiple Windows PCs.

One executable can run as:

- Receiver only
- Transmitter only
- Receiver + transmitter

The receiver can bind each mixer channel to a specific transmitter IP address.

## One-click Setup on Another PC

Copy `install-and-run.bat` to another Windows PC and double-click it.

The script will:

- Check Git
- Check .NET SDK
- Clone or update this repository into `%USERPROFILE%\Local-Sound-Transmitter`
- Build the project
- Start the Web UI at `http://127.0.0.1:5080`

Requirements:

- Git: https://git-scm.com/downloads
- .NET 8 SDK: https://dotnet.microsoft.com/download

If the source code is already downloaded, you can run:

```bat
scripts\run-mixer.bat
```

## UI Configuration

Start the app and open the Web UI:

```powershell
.\SoundTransportation.Mixer.exe --urls http://0.0.0.0:5080
```

Open:

```text
http://localhost:5080
```

Use the `Config` tab to edit:

- Receiver enabled / disabled
- Receiver channel list
- Channel name
- Source IP for each channel
- Whether each channel outputs to speakers
- Transmitter enabled / disabled
- Transmitter name
- Receiver target IP and UDP port
- Audio UDP port
- Local loopback channel options

Click `Save Config` to write changes to `appsettings.json`.

All settings are applied immediately after clicking `Save Config`, including transmitter targets, UDP port, receiver/transmitter enabled flags, output enabled, and local loopback enabled.

## Ports

Defaults:

- Web/API: `TCP 5080`
- Audio UDP: `UDP 5055`

Allow these inbound ports in Windows Firewall on receiver machines.

## Publish EXE

Build a self-contained win-x64 package:

```powershell
.\scripts\publish-win-x64.ps1
```

Deploy this folder to each PC:

```text
publish\SoundTransportation\
```

Important files:

```text
SoundTransportation.Mixer.exe
appsettings.json
wwwroot\
```

## Multi-store Deployment

Recommended flow:

1. Build one release package at HQ.
2. Test it on one PC.
3. Deploy the same package to store PCs.
4. Store-specific `appsettings.json` is preserved during updates by default.

Build a versioned zip package:

```powershell
.\scripts\build-package.ps1 -Version 2026.08.25.1
```

This creates:

```text
release\SoundTransportation-2026.08.25.1.zip
```

This zip is the unified package for receiver PCs and transmitter PCs. Extract it on any store PC and double-click:

```text
install.bat
```

The installer copies the app to:

```text
C:\SoundTransportation
```

It preserves existing `appsettings.json`, creates the auto-start shortcut, and does not start the app immediately. The app will start automatically after Windows logs in; it can also be started manually with `start.bat`.

The app runs in the Windows notification area without a console or automatic browser tabs. Right-click the Sound Transportation tray icon to open the management page or exit. Double-click also opens the page. Repeated starts in the same Windows session do not launch another instance. Existing `Ui:AutoOpen` values are ignored.

To remove the installer-created auto-start shortcut without removing the program, run:

```text
remove-autostart.bat
```

To add a manual startup entry later, create a shortcut to `start.bat` and place it in the Windows Startup folder. Press `Win+R`, enter `shell:startup`, and place the shortcut there.

Install or update one local PC from a zip:

```powershell
.\scripts\install-update-local.ps1 -PackageZip .\release\SoundTransportation-2026.08.25.1.zip
```

Default install directory:

```text
C:\SoundTransportation
```

The PowerShell updater stops the installed app, replaces program files, preserves existing `appsettings.json`, and recreates the auto-start shortcut. Pass `-NoStart` to prevent an immediate restart. The packaged `install.bat` already passes `-NoStart`.

For multiple stores, copy the template:

```powershell
copy deploy\stores.example.csv deploy\stores.csv
```

Edit `deploy\stores.csv`:

```csv
Name,ComputerName,InstallDir,Enabled
Store-001,STORE001-PC,C:\SoundTransportation,true
Store-002,192.168.1.52,C:\SoundTransportation,true
```

Deploy to all enabled stores:

```powershell
.\scripts\deploy-stores.ps1 -PackageZip .\release\SoundTransportation-2026.08.25.1.zip -StoresCsv .\deploy\stores.csv
```

Remote deployment requirements:

- The HQ/admin computer can reach each store computer by VPN/LAN.
- PowerShell Remoting / WinRM is enabled on each store computer.
- The Windows account running deployment has admin permissions on store computers.
- Firewalls allow WinRM traffic.

If remote PowerShell is not available, send the zip plus `scripts\install-update-local.ps1` to the store and run the local install command there.

## External Control

Another program can control the receiver/master PC through HTTP.

Focus one channel by channel name, fade it to 100%, and fade all other channels to 0% over 1 second:

```http
POST /api/control/focus-channel
Content-Type: application/json

{
  "channelName": "Computer A",
  "volumePercent": 100,
  "durationMs": 1000
}
```

Focus by source IP:

```json
{
  "sourceIp": "192.168.1.101",
  "volumePercent": 80,
  "durationMs": 1000
}
```

Focus by channel ID:

```json
{
  "channelId": "a2ff5ade-dfb7-5732-6b54-29b82c5c5ffa",
  "volume": 0.75,
  "durationMs": 1000
}
```

Notes:

- `volumePercent` accepts `0` to `200`.
- `volume` accepts `0.0` to `2.0`.
- `durationMs` accepts `0` to `60000`.
- The focus command uses volume fading and Mute state, so the close/open transition is gradual.

## Receiver Example

E PC receives A/B/C/D and outputs to speakers:

```json
{
  "Receiver": {
    "Enabled": true,
    "AutoCreateChannels": false,
    "Channels": [
      {
        "Name": "Computer A",
        "SourceIp": "192.168.1.101",
        "OutputEnabled": true
      },
      {
        "Name": "Computer B",
        "SourceIp": "192.168.1.102",
        "OutputEnabled": true
      }
    ]
  },
  "Transmitter": {
    "Enabled": false
  },
  "Audio": {
    "UdpPort": 5055,
    "Output": {
      "Enabled": true
    },
    "LocalLoopback": {
      "Enabled": true,
      "Name": "E Local",
      "OutputEnabled": false
    }
  }
}
```

## Transmitter Example

A/B/C/D send local system audio to E:

```json
{
  "Receiver": {
    "Enabled": false
  },
  "Transmitter": {
    "Enabled": true,
    "Name": "Computer A",
    "SenderId": "",
    "Targets": [
      {
        "Address": "192.168.1.100",
        "Port": 5055
      }
    ]
  },
  "Audio": {
    "Output": {
      "Enabled": false
    },
    "LocalLoopback": {
      "Enabled": false
    }
  }
}
```

## API

Read config:

```http
GET /api/config
```

Save config:

```http
PUT /api/config
Content-Type: application/json
```

List channels:

```http
GET /api/channels
```

Focus one channel and fade other channels out:

```http
POST /api/control/focus-channel
Content-Type: application/json

{
  "channelName": "Computer A",
  "volumePercent": 100,
  "durationMs": 1000
}
```

Update live channel state:

```http
PATCH /api/channels/{id}
Content-Type: application/json

{
  "sourceIp": "192.168.1.101",
  "volume": 0.75,
  "muted": false,
  "outputEnabled": true
}
```

## Current Limits

- Audio transport is uncompressed UDP PCM.
- Capture is currently the Windows default playback mix, not per-application audio.
- There is no virtual audio driver yet.
- Device selection should be added later for capture and output devices.
