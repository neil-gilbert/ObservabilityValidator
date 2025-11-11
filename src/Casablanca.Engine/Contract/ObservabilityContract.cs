namespace Casablanca.Engine.Contracts;

public sealed class ObservabilityContractsFile
{
    public string Version { get; set; } = "1";
    public List<ObservabilityContract> Contracts { get; set; } = [];
}

public sealed class ObservabilityContract
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Query { get; set; } = string.Empty;
    public TimeWindow? Window { get; set; }
    public List<ExpectedSpan> ExpectedSpans { get; set; } = [];
}

public sealed class TimeWindow
{
    public int Minutes { get; set; } = 15;
}

public sealed class ExpectedSpan
{
    public string Name { get; set; } = string.Empty;
    public string? Service { get; set; }
    public int? MinCount { get; set; }
    public int? MaxLatencyMs { get; set; }
    public List<ExpectedTag> Tags { get; set; } = [];
}

public sealed class ExpectedTag
{
    public string Key { get; set; } = string.Empty;
    public string? Expected { get; set; }
    public List<string>? ExpectedAnyOf { get; set; }
    public bool Required { get; set; } = true;
}

