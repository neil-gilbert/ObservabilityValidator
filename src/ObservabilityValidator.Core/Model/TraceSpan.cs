namespace ObservabilityValidator.Core.Model;

public sealed class TraceSpan
{
    public string Id { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Service { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public Dictionary<string, object?> Attributes { get; set; } = new();
}
