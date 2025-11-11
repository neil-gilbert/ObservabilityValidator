using Casablanca.Engine.Contracts;
using Casablanca.Abstractions.Model;
using Casablanca.Abstractions.Providers;

namespace Casablanca.Engine.Validation;

public sealed class Validator
{
    private readonly IObservabilityProvider _provider;

    public Validator(IObservabilityProvider provider)
    {
        _provider = provider;
    }

    public async Task<IReadOnlyList<ValidationResult>> ValidateAsync(
        ObservabilityContractsFile contractsFile,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ValidationResult>();
        var now = DateTimeOffset.UtcNow;

        foreach (var contract in contractsFile.Contracts)
        {
            var windowMinutes = contract.Window?.Minutes ?? 15;
            var from = now.AddMinutes(-windowMinutes);

            IReadOnlyList<TraceSpan> spans;
            try
            {
                spans = await _provider.GetSpansAsync(
                    contract.Query,
                    from,
                    now,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult(
                    _provider.Name,
                    contract.Name,
                    passed: false,
                    message: $"Failed to fetch spans: {ex.Message}"));
                continue;
            }

            results.Add(ValidateContract(contract, spans));
        }

        return results;
    }

    private ValidationResult ValidateContract(
        ObservabilityContract contract,
        IReadOnlyList<TraceSpan> spans)
    {
        var details = new List<string>();

        foreach (var expectedSpan in contract.ExpectedSpans)
        {
            var matching = spans
                .Where(s => s.Name == expectedSpan.Name &&
                            (expectedSpan.Service == null || s.Service == expectedSpan.Service))
                .ToList();

            if (expectedSpan.MinCount.HasValue &&
                matching.Count < expectedSpan.MinCount.Value)
            {
                details.Add(
                    $"Span '{expectedSpan.Name}' (service '{expectedSpan.Service ?? "*"}') " +
                    $"count {matching.Count} < MinCount {expectedSpan.MinCount.Value}");
            }

            if (expectedSpan.MaxLatencyMs.HasValue &&
                matching.Any() &&
                matching.Max(s => s.Duration.TotalMilliseconds) > expectedSpan.MaxLatencyMs.Value)
            {
                var maxLatency = matching.Max(s => s.Duration.TotalMilliseconds);
                details.Add(
                    $"Span '{expectedSpan.Name}' exceeded MaxLatencyMs " +
                    $"({maxLatency:0}ms > {expectedSpan.MaxLatencyMs.Value}ms)");
            }

            foreach (var tag in expectedSpan.Tags)
            {
                var spansWithTag = matching
                    .Where(s => s.Attributes.ContainsKey(tag.Key))
                    .ToList();

                if (tag.Required && spansWithTag.Count == 0)
                {
                    details.Add(
                        $"Span '{expectedSpan.Name}' missing required tag '{tag.Key}'.");
                    continue;
                }

                if (!spansWithTag.Any())
                    continue;

                if (tag.Expected is not null)
                {
                    var wrong = spansWithTag
                        .Where(s => !Equals(NormaliseValue(s.Attributes[tag.Key]), tag.Expected))
                        .ToList();

                    if (wrong.Any())
                    {
                        details.Add(
                            $"Tag '{tag.Key}' on span '{expectedSpan.Name}' " +
                            $"did not match expected value '{tag.Expected}'.");
                    }
                }

                if (tag.ExpectedAnyOf is { Count: > 0 })
                {
                    var allowed = new HashSet<string>(tag.ExpectedAnyOf);
                    var wrong = spansWithTag
                        .Where(s =>
                        {
                            var v = NormaliseValue(s.Attributes[tag.Key]);
                            return v is string vs && !allowed.Contains(vs);
                        })
                        .ToList();

                    if (wrong.Any())
                    {
                        details.Add(
                            $"Tag '{tag.Key}' on span '{expectedSpan.Name}' " +
                            $"did not match any of [{string.Join(", ", allowed)}].");
                    }
                }
            }
        }

        var passed = details.Count == 0;
        var message = passed
            ? "All expected spans/tags satisfied."
            : $"{details.Count} validation issue(s) found.";

        return new ValidationResult(
            _provider.Name,
            contract.Name,
            passed,
            message,
            details);
    }

    private static object? NormaliseValue(object? value)
        => value switch
        {
            null => null,
            string s => s,
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
}

