using Casablanca.Abstractions.Model;

namespace Casablanca.Abstractions.Providers;

public interface IObservabilityProvider
{
    string Name { get; }

    Task<IReadOnlyList<TraceSpan>> GetSpansAsync(
        string query,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MetricPoint>> GetMetricsAsync(
        string query,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

