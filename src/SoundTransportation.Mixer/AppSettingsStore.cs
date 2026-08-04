using System.Text.Json;
using System.Text.Json.Nodes;

namespace SoundTransportation.Mixer;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AppSettingsStore(IHostEnvironment environment)
    {
        _settingsPath = Path.Combine(environment.ContentRootPath, "appsettings.json");
    }

    public AppConfigDto Read()
    {
        var root = ReadRoot();
        return new AppConfigDto(
            ReadSection(root, "Receiver", new ReceiverConfigDto(true, true, [])),
            ReadSection(root, "Transmitter", new TransmitterConfigDto(false, string.Empty, string.Empty, [])),
            ReadSection(root, "Audio", new AudioConfigDto(
                5055,
                new AudioOutputConfigDto(true),
                new LocalSessionMuteConfigDto(true),
                new LocalLoopbackConfigDto(true, "E Local", false))));
    }

    public void Save(AppConfigDto config)
    {
        var root = ReadRoot();
        root["Receiver"] = JsonSerializer.SerializeToNode(config.Receiver, JsonOptions);
        root["Transmitter"] = JsonSerializer.SerializeToNode(config.Transmitter, JsonOptions);
        root["Audio"] = JsonSerializer.SerializeToNode(config.Audio, JsonOptions);

        var json = root.ToJsonString(JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private JsonObject ReadRoot()
    {
        if (!File.Exists(_settingsPath))
        {
            return new JsonObject
            {
                ["Logging"] = new JsonObject
                {
                    ["LogLevel"] = new JsonObject
                    {
                        ["Default"] = "Information",
                        ["Microsoft.AspNetCore"] = "Warning"
                    }
                },
                ["AllowedHosts"] = "*"
            };
        }

        var text = File.ReadAllText(_settingsPath);
        return JsonNode.Parse(text)?.AsObject() ?? [];
    }

    private static T ReadSection<T>(JsonObject root, string sectionName, T fallback)
    {
        if (!root.TryGetPropertyValue(sectionName, out var node) || node is null)
        {
            return fallback;
        }

        return node.Deserialize<T>(JsonOptions) ?? fallback;
    }
}

public sealed record AppConfigDto(ReceiverConfigDto Receiver, TransmitterConfigDto Transmitter, AudioConfigDto Audio);

public sealed record ReceiverConfigDto(bool Enabled, bool AutoCreateChannels, List<ReceiverChannelConfigDto> Channels);

public sealed record ReceiverChannelConfigDto(string Name, string? SourceIp, bool OutputEnabled);

public sealed record TransmitterConfigDto(bool Enabled, string Name, string? SenderId, List<TransmitterTargetConfigDto> Targets);

public sealed record TransmitterTargetConfigDto(string Address, int Port);

public sealed record AudioConfigDto(
    int UdpPort,
    AudioOutputConfigDto Output,
    LocalSessionMuteConfigDto LocalSessionMuteOnRemoteSolo,
    LocalLoopbackConfigDto LocalLoopback);

public sealed record AudioOutputConfigDto(bool Enabled);

public sealed record LocalSessionMuteConfigDto(bool Enabled);

public sealed record LocalLoopbackConfigDto(bool Enabled, string Name, bool OutputEnabled);

public sealed record ConfigSaveResult(bool Saved, bool RestartRequired, string Message, AppConfigDto Config);
