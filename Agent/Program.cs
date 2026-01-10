using Shared;
using eeCLOUD.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using System.Text.Json;



var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        var cfg = ctx.Configuration;
        var ee = cfg.GetSection("eeCLOUD");
        var apiKey = ee["ApiKey"];

        services.AddSingleton(_ => new eeCloudClient(apiKey));
        services.AddHostedService<DeviceAgentWorker>();
    })
    .Build();

await host.RunAsync();



public sealed class DeviceAgentWorker : BackgroundService
{
    private readonly eeCloudClient _ee;
    private readonly IConfiguration _cfg;

    private long _appliedVersion = 0;
    private int _samplingMs = 1000;
    private string _logLevel = "Information";

    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    public DeviceAgentWorker(eeCloudClient ee, IConfiguration cfg)
    {
        _ee = ee;
        _cfg = cfg;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var deviceId = _cfg["Device:DeviceId"]!;
        var group = _cfg["Device:Group"]!;
        var name = _cfg["Device:Name"]!;

        // register device
        Device device = new()
        {
            DeviceId = deviceId,
            Group = group,
            Name = name
        };

        await _ee.Memory("devices").WriteAsync(group, deviceId, device);

        while (!stoppingToken.IsCancellationRequested)
        {
            // 1) fetch desired config
            Memory configMem = await _ee.Memory("configurations").ReadByIndexAsync(deviceId);

            if (!configMem.result)
                continue;

            var configuration = JsonSerializer.Deserialize<DesiredConfig>(configMem.area[0].data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (configuration is not null && configuration.Version > _appliedVersion)
            {
                Apply(configuration);

                _appliedVersion = configuration.Version;

                Console.WriteLine($"Applied config v{_appliedVersion} (samplingMs={_samplingMs}, logLevel={_logLevel})");
            }


            // 2) push reported state
            var runtime = new Dictionary<string, object>
            {
                ["uptimeSec"] = (long)_uptime.Elapsed.TotalSeconds,
                ["samplingMs"] = _samplingMs,
                ["logLevel"] = _logLevel
            };

            var newState = new ReportedState(
                    DeviceId: deviceId,
                    LastSeenUtc: DateTime.UtcNow,
                    AppliedConfigVersion: _appliedVersion,
                    Runtime: runtime
                );


            var sw = Stopwatch.StartNew();
            Memory stateMem = await _ee.Memory("states").ReadByIndexAsync(deviceId);

            if (stateMem.result)
            {
                stateMem = await _ee.Memory("states").UpdateAsync(stateMem.area[0].address, newState);
            }
            else
            {
                stateMem = await _ee.Memory("states").WriteAsync(group, deviceId, newState);
            }
            sw.Stop();

            // 3) push telemetry
            var metrics = new Dictionary<string, object>
            {
                ["counter"] = DateTime.UtcNow.Ticks,
                ["uptimeSec"] = (long)_uptime.Elapsed.TotalSeconds,
                ["cloudDelay"] = stateMem.time,
                ["netDelay"] = sw.ElapsedMilliseconds
            };

            await _ee.Memory("telemetry").WriteAsync(deviceId, new TelemetryPoint(
                DeviceId: deviceId,
                TimestampUtc: DateTime.UtcNow,
                Metrics: metrics
            ));


            await Task.Delay(_samplingMs, stoppingToken);
        }
    }

    private void Apply(DesiredConfig desired)
    {
        if (desired.Config.TryGetValue("samplingMs", out var sm) && TryGetInt(sm, out var sampling))
            _samplingMs = Math.Clamp(sampling, 200, 60_000);

        if (desired.Config.TryGetValue("logLevel", out var ll))
            _logLevel = ll?.ToString() ?? _logLevel;
    }

    private static bool TryGetInt(object? value, out int result)
    {
        result = 0;
        if (value is null) return false;
        if (value is int i) { result = i; return true; }
        if (value is long l) { result = (int)l; return true; }
        if (value is double d) { result = (int)d; return true; }
        return int.TryParse(value.ToString(), out result);
    }
}