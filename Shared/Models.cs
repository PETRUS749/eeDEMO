namespace Shared;

public class Device
{
    public long id { get; set; }
    public string DeviceId { get; set; }
    public string Group { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Dictionary<string, object>? Metadata { get; set; } = null;
}

public sealed record DesiredConfig(
    string TargetType,   // "device" | "group"
    string TargetId,     // deviceId or group
    long Version,
    Dictionary<string, object> Config,
    DateTime PublishedAtUtc
);

public sealed record ReportedState(
    string DeviceId,
    DateTime LastSeenUtc,
    long AppliedConfigVersion,
    Dictionary<string, object> Runtime
);

public sealed record TelemetryPoint(
    string DeviceId,
    DateTime TimestampUtc,
    Dictionary<string, object> Metrics
);