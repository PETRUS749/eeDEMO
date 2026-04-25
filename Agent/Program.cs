using Shared;
using eeCLOUD.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };


var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        var cfg = ctx.Configuration;
        var ee = cfg.GetSection("eeCLOUD");
        var apiKey = ee["ApiKey"];

        services.AddSingleton(_ => new eeCloudClient(apiKey!));
        services.AddSingleton(jsonOptions);
        services.AddHostedService<DeviceAgentWorker>();
    })
    .Build();

await host.RunAsync();



public sealed class DeviceAgentWorker : BackgroundService
{
    private readonly eeCloudClient _ee;
    private readonly IConfiguration _cfg;
    private readonly JsonSerializerOptions _jsonOptions;

    private long _appliedVersion = 0;
    private int _samplingMs = 1000;
    private string _logLevel = "Information";

    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    // CPU tracking
    private TimeSpan _lastCpuTime = TimeSpan.Zero;
    private DateTime _lastCpuCheck = DateTime.UtcNow;

    public DeviceAgentWorker(eeCloudClient ee, IConfiguration cfg, JsonSerializerOptions jsonOptions)
    {
        _ee = ee;
        _cfg = cfg;
        _jsonOptions = jsonOptions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var deviceId = _cfg["Device:DeviceId"]!;
        var group = _cfg["Device:Group"]!;
        var name = _cfg["Device:Name"]!;

        Device device = new()
        {
            DeviceId = deviceId,
            Group = group,
            Name = name
        };

        await RegisterDeviceAsync(group, deviceId, device);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1) fetch desired config: device override, then group fallback
                var configuration = await ReadDesiredConfigAsync(deviceId, group);
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
                Memory stateMem = await UpsertReportedStateAsync(group, deviceId, newState);
                sw.Stop();

                // 3) push telemetry con metriche hardware reali
                var metrics = new Dictionary<string, object>
                {
                    ["uptimeSec"]  = (long)_uptime.Elapsed.TotalSeconds,
                    ["cloudDelay"] = stateMem.time,
                    ["netDelay"]   = sw.ElapsedMilliseconds,
                    ["cpuPercent"] = Math.Round(HardwareMetrics.GetCpuPercent(ref _lastCpuTime, ref _lastCpuCheck), 1),
                    ["ramMB"]      = HardwareMetrics.GetRamUsedMB(),
                    ["tempC"]      = HardwareMetrics.GetCpuTemperatureC()
                };

                await _ee.Memory("telemetry").WriteAsync(deviceId, new TelemetryPoint(
                    DeviceId: deviceId,
                    TimestampUtc: DateTime.UtcNow,
                    Metrics: metrics
                ));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Agent loop error: {ex.Message}");
            }

            await Task.Delay(_samplingMs, stoppingToken);
        }
    }

    private async Task RegisterDeviceAsync(string group, string deviceId, Device device)
    {
        var existing = await _ee.Memory("devices").ReadByIndexAsync(deviceId);
        if (HasRows(existing))
        {
            await _ee.Memory("devices").UpdateAsync(existing!.area![0]!.address, device);
            return;
        }

        await _ee.Memory("devices").WriteAsync(group, deviceId, device);
    }

    private async Task<DesiredConfig?> ReadDesiredConfigAsync(string deviceId, string group)
    {
        var deviceConfig = await ReadConfigByIndexAsync(deviceId);
        if (deviceConfig is not null)
            return deviceConfig;

        return await ReadConfigByIndexAsync(group);
    }

    private async Task<DesiredConfig?> ReadConfigByIndexAsync(string index)
    {
        Memory? mem = await _ee.Memory("configurations").ReadByIndexAsync(index);
        if (!HasRows(mem))
            return null;

        return JsonSerializer.Deserialize<DesiredConfig>(mem!.area![0]!.data, _jsonOptions);
    }

    private async Task<Memory> UpsertReportedStateAsync(string group, string deviceId, ReportedState state)
    {
        Memory? existing = await _ee.Memory("states").ReadByIndexAsync(deviceId);
        if (HasRows(existing))
            return (await _ee.Memory("states").UpdateAsync(existing!.area![0]!.address, state))!;

        return (await _ee.Memory("states").WriteAsync(group, deviceId, state))!;
    }

    private static bool HasRows(Memory? mem)
        => mem?.result == true && mem.area is not null && mem.area.Any();

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


/// <summary>
/// Legge metriche hardware reali. Funziona su Linux (LattePanda IOTA)
/// e degrada gracefully su altri sistemi operativi.
/// </summary>
public static class HardwareMetrics
{
    // --- CPU % (cross-platform via Process CPU time) ---

    public static double GetCpuPercent(ref TimeSpan lastCpuTime, ref DateTime lastCheck)
    {
        try
        {
            var proc = Process.GetCurrentProcess();
            var now = DateTime.UtcNow;
            var currentCpu = proc.TotalProcessorTime;

            var elapsedWall = (now - lastCheck).TotalMilliseconds;
            var elapsedCpu  = (currentCpu - lastCpuTime).TotalMilliseconds;

            lastCpuTime = currentCpu;
            lastCheck   = now;

            if (elapsedWall <= 0) return 0;

            // normalizza sul numero di core
            var cores = Environment.ProcessorCount;
            return Math.Min(100.0, elapsedCpu / (elapsedWall * cores) * 100.0);
        }
        catch
        {
            return -1;
        }
    }

    // --- RAM usata (MB) — legge /proc/meminfo su Linux, fallback su Process ---

    public static long GetRamUsedMB()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var lines = File.ReadAllLines("/proc/meminfo");
                long total = 0, available = 0;

                foreach (var line in lines)
                {
                    if (line.StartsWith("MemTotal:"))
                        total = ParseMemInfoKb(line);
                    else if (line.StartsWith("MemAvailable:"))
                        available = ParseMemInfoKb(line);
                }

                if (total > 0)
                    return (total - available) / 1024;
            }
        }
        catch { }

        // fallback: memoria del processo corrente
        return Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
    }

    private static long ParseMemInfoKb(string line)
    {
        // Formato: "MemTotal:       16234568 kB"
        var parts = line.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return 0;
        var numPart = parts[1].Replace("kB", "").Trim();
        return long.TryParse(numPart, out var v) ? v : 0;
    }

    // --- Temperatura CPU (°C) — solo Linux via thermal_zone ---

    public static double GetCpuTemperatureC()
    {
        if (!OperatingSystem.IsLinux()) return -1;

        try
        {
            // Prova le prime thermal_zone disponibili
            for (int i = 0; i < 5; i++)
            {
                var path = $"/sys/class/thermal/thermal_zone{i}/temp";
                if (!File.Exists(path)) continue;

                var raw = File.ReadAllText(path).Trim();
                if (long.TryParse(raw, out var milliC))
                    return Math.Round(milliC / 1000.0, 1);
            }
        }
        catch { }

        return -1;
    }
}
