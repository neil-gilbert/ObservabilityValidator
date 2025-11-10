namespace ObservabilityValidator.Core.Model;

public sealed class MetricPoint
{
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
}
