# GitHub Upload File List

Upload source files and project metadata only. Do not upload build outputs such as `bin/`, `obj/`, or `publish/`.

## Include

```text
.gitignore
GITHUB_UPLOAD_FILES.md
README.md
SoundTransportation.sln
install-and-run.bat
deploy/stores.example.csv
scripts/build-package.ps1
scripts/deploy-stores.ps1
scripts/install-from-folder.ps1
scripts/install-update-local.ps1
scripts/publish-win-x64.ps1
scripts/run-mixer.bat
scripts/run-mixer.ps1

src/SoundTransportation.Shared/SoundTransportation.Shared.csproj
src/SoundTransportation.Shared/AudioProtocol.cs
src/SoundTransportation.Shared/SampleNormalizer.cs

src/SoundTransportation.Sender/SoundTransportation.Sender.csproj
src/SoundTransportation.Sender/Program.cs

src/SoundTransportation.Mixer/SoundTransportation.Mixer.csproj
src/SoundTransportation.Mixer/appsettings.json
src/SoundTransportation.Mixer/appsettings.Development.json
src/SoundTransportation.Mixer/Properties/launchSettings.json
src/SoundTransportation.Mixer/AppSettingsStore.cs
src/SoundTransportation.Mixer/AudioChannel.cs
src/SoundTransportation.Mixer/AudioOutputService.cs
src/SoundTransportation.Mixer/BrowserLauncherService.cs
src/SoundTransportation.Mixer/ChannelFadeService.cs
src/SoundTransportation.Mixer/ChannelRegistry.cs
src/SoundTransportation.Mixer/IntegratedSenderService.cs
src/SoundTransportation.Mixer/LocalLoopbackCaptureService.cs
src/SoundTransportation.Mixer/LocalSessionMuteService.cs
src/SoundTransportation.Mixer/MixerSampleProvider.cs
src/SoundTransportation.Mixer/Program.cs
src/SoundTransportation.Mixer/UdpAudioReceiver.cs
src/SoundTransportation.Mixer/wwwroot/app.js
src/SoundTransportation.Mixer/wwwroot/index.html
src/SoundTransportation.Mixer/wwwroot/styles.css
```

## Exclude

```text
publish/
release/
deploy/stores.csv
src/**/bin/
src/**/obj/
logs/
.vs/
```

## Suggested Git Commands

```powershell
git init
git add .gitignore GITHUB_UPLOAD_FILES.md README.md SoundTransportation.sln install-and-run.bat deploy scripts src
git status
git commit -m "Initial Sound Transportation source"
git branch -M main
git remote add origin https://github.com/<your-user>/<your-repo>.git
git push -u origin main
```
