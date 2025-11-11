namespace Casablanca.Abstractions.Model;

public sealed class MetricPoint
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Labels { get; set; } = new();
    public double Value { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

