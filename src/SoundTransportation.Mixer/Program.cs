using SoundTransportation.Mixer;

var builder = WebApplication.CreateBuilder(args);

if (!HasConfiguredUrl(args))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5080");
}

builder.Services.AddSingleton<ChannelRegistry>();
builder.Services.AddSingleton<AppSettingsStore>();
builder.Services.AddSingleton<ChannelFadeService>();
builder.Services.AddHostedService<UdpAudioReceiver>();
builder.Services.AddHostedService<LocalLoopbackCaptureService>();
builder.Services.AddHostedService<IntegratedSenderService>();
builder.Services.AddHostedService<AudioOutputService>();
builder.Services.AddHostedService<LocalSessionMuteService>();
builder.Services.AddHostedService<BrowserLauncherService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ChannelFadeService>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/channels", (ChannelRegistry registry) =>
{
    return registry.GetChannels().Select(ChannelDto.FromChannel);
});

app.MapGet("/api/channels/{id:guid}", (Guid id, ChannelRegistry registry) =>
{
    var channel = registry.GetChannel(id);
    return channel is null ? Results.NotFound() : Results.Ok(ChannelDto.FromChannel(channel));
});

app.MapPost("/api/channels", (ChannelCreate create, ChannelRegistry registry) =>
{
    var channel = registry.CreateChannel(create.Name, create.SourceIp);

    if (create.Volume is not null)
    {
        channel.Volume = Math.Clamp(create.Volume.Value, 0f, 2f);
    }

    if (create.Muted is not null)
    {
        channel.Muted = create.Muted.Value;
    }

    if (create.Solo is not null)
    {
        channel.Solo = create.Solo.Value;
    }

    if (create.OutputEnabled is not null)
    {
        channel.OutputEnabled = create.OutputEnabled.Value;
    }

    return Results.Created($"/api/channels/{channel.Id}", ChannelDto.FromChannel(channel));
});

app.MapPatch("/api/channels/{id:guid}", (Guid id, ChannelPatch patch, ChannelRegistry registry) =>
{
    var channel = registry.GetChannel(id);
    if (channel is null)
    {
        return Results.NotFound();
    }

    if (patch.Name is not null)
    {
        channel.Name = patch.Name;
    }

    if (patch.Volume is not null)
    {
        channel.Volume = Math.Clamp(patch.Volume.Value, 0f, 2f);
    }

    if (patch.Muted is not null)
    {
        channel.Muted = patch.Muted.Value;
    }

    if (patch.Solo is not null)
    {
        if (patch.Solo.Value)
        {
            foreach (var otherChannel in registry.GetChannels())
            {
                if (otherChannel.Id != channel.Id)
                {
                    otherChannel.Solo = false;
                }
            }
        }

        channel.Solo = patch.Solo.Value;
    }

    if (patch.OutputEnabled is not null)
    {
        channel.OutputEnabled = patch.OutputEnabled.Value;
    }

    if (patch.SourceIp is not null)
    {
        registry.UpdateChannelBinding(channel, patch.SourceIp);
    }

    return Results.Ok(ChannelDto.FromChannel(channel));
});

app.MapPost("/api/control/focus-channel", (FocusChannelCommand command, ChannelRegistry registry, ChannelFadeService fades) =>
{
    var channel = registry.FindChannel(command.ChannelId, command.ChannelName, command.SourceIp);
    if (channel is null)
    {
        return Results.NotFound(new { message = "Channel not found." });
    }

    var targetVolume = command.VolumePercent is not null
        ? Math.Clamp(command.VolumePercent.Value / 100f, 0f, 2f)
        : Math.Clamp(command.Volume ?? 1f, 0f, 2f);
    var duration = TimeSpan.FromMilliseconds(Math.Clamp(command.DurationMs ?? 1000, 0, 60_000));

    fades.FocusChannel(channel.Id, targetVolume, duration);
    return Results.Ok(new
    {
        focusedChannelId = channel.Id,
        focusedChannelName = channel.Name,
        targetVolume,
        durationMs = (int)duration.TotalMilliseconds
    });
});

app.MapGet("/api/status", (IConfiguration configuration) => new
{
    udpPort = configuration.GetValue("Audio:UdpPort", 5055),
    receiverEnabled = configuration.GetValue("Receiver:Enabled", true),
    transmitterEnabled = configuration.GetValue("Transmitter:Enabled", false),
    localLoopbackEnabled = configuration.GetValue("Audio:LocalLoopback:Enabled", true),
    localLoopbackOutputEnabled = configuration.GetValue("Audio:LocalLoopback:OutputEnabled", false),
    sampleRate = SoundTransportation.Shared.AudioProtocol.SampleRate,
    channels = SoundTransportation.Shared.AudioProtocol.Channels
});

app.MapGet("/api/config", (AppSettingsStore settingsStore) => settingsStore.Read());

app.MapPut("/api/config", (AppConfigDto config, AppSettingsStore settingsStore, ChannelRegistry registry) =>
{
    settingsStore.Save(config);
    registry.ApplyReceiverConfig(config.Receiver);
    return new ConfigSaveResult(
        true,
        true,
        "Configuration saved. Receiver channel bindings were applied now; turn off auto create channels to hide unconfigured remote sources. Restart the app for transmitter, port, output, and service enable changes.",
        settingsStore.Read());
});

app.Run();

static bool HasConfiguredUrl(string[] args)
{
    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_URLS")))
    {
        return true;
    }

    return args.Any(arg =>
        arg.Equals("--urls", StringComparison.OrdinalIgnoreCase) ||
        arg.StartsWith("--urls=", StringComparison.OrdinalIgnoreCase) ||
        arg.StartsWith("urls=", StringComparison.OrdinalIgnoreCase));
}

public sealed record ChannelCreate(string Name, string? SourceIp, float? Volume, bool? Muted, bool? Solo, bool? OutputEnabled);

public sealed record ChannelPatch(string? Name, string? SourceIp, float? Volume, bool? Muted, bool? Solo, bool? OutputEnabled);

public sealed record FocusChannelCommand(Guid? ChannelId, string? ChannelName, string? SourceIp, float? Volume, float? VolumePercent, int? DurationMs);

public sealed record ChannelDto(
    Guid Id,
    string Name,
    string? SourceIp,
    string? LastSourceIp,
    bool IsLocalLoopback,
    float Volume,
    float EffectiveVolume,
    bool Muted,
    bool Solo,
    bool OutputEnabled,
    float Level,
    int QueuedSamples,
    DateTimeOffset LastSeenUtc,
    uint LastSequence)
{
    public static ChannelDto FromChannel(AudioChannel channel)
    {
        return new ChannelDto(
            channel.Id,
            channel.Name,
            channel.SourceIp,
            channel.LastSourceIp,
            channel.IsLocalLoopback,
            channel.Volume,
            channel.EffectiveVolume,
            channel.Muted,
            channel.Solo,
            channel.OutputEnabled,
            channel.Level,
            channel.QueuedSamples,
            channel.LastSeenUtc,
            channel.LastSequence);
    }
}
