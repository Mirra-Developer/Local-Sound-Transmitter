# Sound Transportation

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

Receiver channel IP bindings are applied immediately. Service-level changes, including transmitter targets, UDP port, receiver/transmitter enabled flags, output enabled, and local loopback enabled, should be applied by restarting the app.

## Ports

Defaults:

- Web/API: `TCP 5080`
- Audio UDP: `UDP 5055`

Allow these inbound ports in Windows Firewall on receiver machines.

## Publish EXE

Build a self-contained win-x64 package:

```powershell
dotnet publish src\SoundTransportation.Mixer -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\SoundTransportation
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

## Receiver Example

E PC receives A/B/C/D and outputs to speakers:

```json
{
  "Receiver": {
    "Enabled": true,
    "AutoCreateChannels": true,
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

Update live channel state:

```http
PATCH /api/channels/{id}
Content-Type: application/json

{
  "sourceIp": "192.168.1.101",
  "volume": 0.75,
  "muted": false,
  "solo": false,
  "outputEnabled": true
}
```

## Current Limits

- Audio transport is uncompressed UDP PCM.
- Capture is currently the Windows default playback mix, not per-application audio.
- There is no virtual audio driver yet.
- Device selection should be added later for capture and output devices.
