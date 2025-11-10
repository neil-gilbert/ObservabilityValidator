using ObservabilityValidator.Core.Contracts;
using ObservabilityValidator.Core.Model;
using ObservabilityValidator.Core.Providers;
using ObservabilityValidator.Core.Validation;
using Xunit;

namespace ObservabilityValidator.Core.Tests;

public class SampleContractTests
{
    private sealed class FakeProvider : IObservabilityProvider
    {
        public string Name => "Fake";

        public Task<IReadOnlyList<TraceSpan>> GetSpansAsync(
            string query,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            var span = new TraceSpan
            {
                Id = "1",
                TraceId = "trace-1",
                Name = "POST /payments",
                Service = "payment-api",
                Duration = TimeSpan.FromMilliseconds(100),
                StartTime = DateTimeOffset.UtcNow,
                Attributes = new Dictionary<string, object?>
                {
                    ["payment.status"] = "success",
                    ["customer.id"] = "123"
                }
            };

            return Task.FromResult<IReadOnlyList<TraceSpan>>(new[] { span });
        }

        public Task<IReadOnlyList<MetricPoint>> GetMetricsAsync(
            string query,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MetricPoint>>(Array.Empty<MetricPoint>());
    }

    [Fact]
    public async Task Validator_Passes_When_Spans_Match()
    {
        var contract = new ObservabilityContractsFile
        {
            Contracts =
            [
                new()
                {
                    Name = "TestContract",
                    Query = "service:payment-api",
                    ExpectedSpans =
                    [
                        new()
                        {
                            Name = "POST /payments",
                            Service = "payment-api",
                            MinCount = 1,
                            Tags =
                            [
                                new() { Key = "payment.status", Expected = "success" },
                                new() { Key = "customer.id", Required = true }
                            ]
                        }
                    ]
                }
            ]
        };

        var validator = new Validator(new FakeProvider());

        var results = await validator.ValidateAsync(contract);

        Assert.Single(results);
        Assert.True(results[0].Passed);
    }
}
