using System.Text.Json;
using Casablanca.Engine.Contracts;
using Casablanca.Abstractions.Model;
using Casablanca.Abstractions.Providers;
using Casablanca.Engine.Validation;
using Casablanca.Engine.Providers;
using Casablanca.Providers.Datadog;
using Casablanca.Providers.Honeycomb;

namespace Casablanca.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--"))
        {
            // Back-compat: default to validate with legacy flags
            return await RunValidate(args);
        }

        var cmd = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return cmd switch
        {
            "validate" => await RunValidate(rest),
            "contracts" => await RunContracts(rest),
            "record" => await RunRecord(rest),
            _ => ShowHelpAndExit(cmd)
        };
    }

    private static int ShowHelpAndExit(string cmd)
    {
        Console.WriteLine($"Unknown or missing command '{cmd}'.\n");
        Console.WriteLine("Usage:");
        Console.WriteLine("  ov validate --contracts=<path> --config=<path> [--from=ISO] [--to=ISO] [--spans=<file.ndjson>]");
        Console.WriteLine("  ov contracts lint --contracts=<path>");
        Console.WriteLine("  ov record --config=<path> --provider-name=<name> --query=\"...\" --from=ISO --to=ISO --out=<file.ndjson>");
        return 1;
    }

    private static async Task<int> RunValidate(string[] args)
    {
        var contractsPath = GetArg(args, "--contracts=") ?? "contracts/observability-contracts.yaml";
        var configPath = GetArg(args, "--config=") ?? "contracts/telemetry-config.yaml";
        var fromStr = GetArg(args, "--from=");
        var toStr = GetArg(args, "--to=");
        var spansPath = GetArg(args, "--spans="); // optional offline NDJSON

        Console.WriteLine($"Using contracts: {contractsPath}");
        Console.WriteLine($"Using config   : {configPath}");

        var contractsFile = ContractLoader.Load(contractsPath);
        var now = DateTimeOffset.UtcNow;
        var from = ParseIso(fromStr) ?? now.AddMinutes(-(contractsFile.Contracts.FirstOrDefault()?.Window?.Minutes ?? 15));
        var to = ParseIso(toStr) ?? now;

        if (!string.IsNullOrEmpty(spansPath))
        {
            // Offline validation from NDJSON
            var spans = await ReadNdjsonSpans(spansPath);
            var provider = new SingleShotProvider(spans);
            return await RunValidationAgainstProvider(provider, contractsFile);
        }

        var telemetryConfig = ProviderFactory.LoadConfig(configPath);
        var providers = CreateProviders(telemetryConfig);
        if (providers.Count == 0)
        {
            Console.WriteLine("No enabled providers configured. Exiting.");
            return 1;
        }

        var exitCode = 0;
        foreach (var provider in providers)
        {
            Console.WriteLine();
            Console.WriteLine($"=== Provider: {provider.Name} ===");
            // Wrap provider to enforce from/to window
            var windowingProvider = new WindowingProvider(provider, from, to);
            var validator = new Validator(windowingProvider);
            var results = await validator.ValidateAsync(contractsFile);
            foreach (var result in results)
            {
                var icon = result.Passed ? "✅" : "❌";
                Console.WriteLine($"{icon} [{result.ContractName}] {result.Message}");
                foreach (var detail in result.Details)
                    Console.WriteLine($"   - {detail}");
                if (!result.Passed) exitCode = 2;
            }
        }

        return exitCode;
    }

    private static async Task<int> RunContracts(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: ov contracts lint --contracts=<path>");
            return 1;
        }
        var sub = args[0].ToLowerInvariant();
        if (sub != "lint") return ShowHelpAndExit("contracts " + sub);
        var contractsPath = GetArg(args.Skip(1).ToArray(), "--contracts=") ?? "contracts/observability-contracts.yaml";
        return await RunContractsLint(contractsPath);
    }

    private static async Task<int> RunContractsLint(string contractsPath)
    {
        try
        {
            var file = ContractLoader.Load(contractsPath);
            var errors = new List<string>();
            if (file.Contracts is null || file.Contracts.Count == 0)
                errors.Add("No contracts defined.");

            foreach (var c in file.Contracts)
            {
                if (string.IsNullOrWhiteSpace(c.Name)) errors.Add("Contract has empty name.");
                if (string.IsNullOrWhiteSpace(c.Query)) errors.Add($"Contract '{c.Name}' has empty query.");
                foreach (var s in c.ExpectedSpans)
                {
                    if (string.IsNullOrWhiteSpace(s.Name)) errors.Add($"Contract '{c.Name}' has expected span with empty name.");
                    if (s.MaxLatencyMs.HasValue && s.MaxLatencyMs < 0) errors.Add($"Contract '{c.Name}' span '{s.Name}' has negative MaxLatencyMs.");
                    foreach (var t in s.Tags)
                    {
                        if (string.IsNullOrWhiteSpace(t.Key)) errors.Add($"Contract '{c.Name}' span '{s.Name}' has tag with empty key.");
                    }
                }
            }

            if (errors.Count == 0)
            {
                Console.WriteLine($"Contracts lint OK: {contractsPath}");
                return 0;
            }
            Console.WriteLine("Contracts lint FAILED:");
            foreach (var e in errors) Console.WriteLine(" - " + e);
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Contracts lint FAILED: " + ex.Message);
            return 2;
        }
    }

    private static async Task<int> RunRecord(string[] args)
    {
        var configPath = GetArg(args, "--config=") ?? "contracts/telemetry-config.yaml";
        var providerName = GetArg(args, "--provider-name=");
        var query = GetArg(args, "--query=") ?? string.Empty;
        var fromStr = GetArg(args, "--from=");
        var toStr = GetArg(args, "--to=");
        var outPath = GetArg(args, "--out=");

        if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(outPath) || string.IsNullOrWhiteSpace(fromStr) || string.IsNullOrWhiteSpace(toStr))
        {
            Console.WriteLine("Usage: ov record --config=<path> --provider-name=<name> --query=\"...\" --from=ISO --to=ISO --out=<file.ndjson>");
            return 1;
        }

        var from = ParseIso(fromStr) ?? throw new ArgumentException("Invalid --from");
        var to = ParseIso(toStr) ?? throw new ArgumentException("Invalid --to");

        var telemetryConfig = ProviderFactory.LoadConfig(configPath);
        var provider = CreateProviders(telemetryConfig)
            .FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            Console.WriteLine($"Provider '{providerName}' not found or not enabled in {configPath}.");
            return 1;
        }

        var spans = await provider.GetSpansAsync(query, from, to);
        await WriteNdjsonSpans(outPath, spans);
        Console.WriteLine($"Wrote {spans.Count} spans to {outPath}");
        return 0;
    }

    private static async Task<int> RunValidationAgainstProvider(IObservabilityProvider provider, ObservabilityContractsFile file)
    {
        var validator = new Validator(provider);
        var results = await validator.ValidateAsync(file);
        var exit = 0;
        foreach (var r in results)
        {
            var icon = r.Passed ? "✅" : "❌";
            Console.WriteLine($"{icon} [{r.ContractName}] {r.Message}");
            foreach (var d in r.Details) Console.WriteLine("   - " + d);
            if (!r.Passed) exit = 2;
        }
        return exit;
    }

    private static IReadOnlyList<IObservabilityProvider> CreateProviders(TelemetryConfig config)
    {
        var providers = new List<IObservabilityProvider>();
        foreach (var p in config.Providers.Where(p => p.Enabled))
        {
            switch (p.Type.ToLowerInvariant())
            {
                case "datadog":
                    var apiUrl = p.Settings.TryGetValue("api_url", out var url)
                        ? url
                        : "https://api.datadoghq.com";
                    var apiKeyEnv = p.Settings.GetValueOrDefault("api_key_env", "DD_API_KEY");
                    var appKeyEnv = p.Settings.GetValueOrDefault("app_key_env", "DD_APP_KEY");
                    var apiKey = Environment.GetEnvironmentVariable(apiKeyEnv)
                               ?? throw new InvalidOperationException($"Missing env var '{apiKeyEnv}'.");
                    var appKey = Environment.GetEnvironmentVariable(appKeyEnv)
                               ?? throw new InvalidOperationException($"Missing env var '{appKeyEnv}'.");
                    providers.Add(new DatadogProvider(p.Name, apiUrl, apiKey, appKey));
                    break;
                case "honeycomb":
                    var hcUrl = p.Settings.TryGetValue("api_url", out var hcApi)
                        ? hcApi
                        : "https://api.eu1.honeycomb.io";
                    var hcApiKeyEnv = p.Settings.GetValueOrDefault("api_key_env", "HONEYCOMB_API_KEY");
                    var dataset = p.Settings.TryGetValue("dataset", out var ds) && !string.IsNullOrWhiteSpace(ds)
                        ? ds
                        : throw new InvalidOperationException("Honeycomb provider requires 'dataset' setting.");
                    var hcApiKey = Environment.GetEnvironmentVariable(hcApiKeyEnv)
                                   ?? throw new InvalidOperationException($"Missing env var '{hcApiKeyEnv}'.");
                    providers.Add(new HoneycombProvider(p.Name, hcUrl, hcApiKey, dataset));
                    break;
                default:
                    Console.WriteLine($"WARN: Provider type '{p.Type}' is not supported in CLI (name: {p.Name}).");
                    break;
            }
        }
        return providers;
    }

    private static string? GetArg(IEnumerable<string> args, string prefix)
        => args.FirstOrDefault(a => a.StartsWith(prefix))?.Substring(prefix.Length);

    private static DateTimeOffset? ParseIso(string? s)
        => DateTimeOffset.TryParse(s, out var dto) ? dto : null;

    private static async Task<List<TraceSpan>> ReadNdjsonSpans(string path)
    {
        var list = new List<TraceSpan>();
        await foreach (var line in ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var el = doc.RootElement;
                var span = new TraceSpan
                {
                    Id = el.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    TraceId = el.TryGetProperty("traceId", out var tid) ? tid.GetString() ?? string.Empty : string.Empty,
                    Name = el.TryGetProperty("name", out var nm) ? nm.GetString() ?? string.Empty : string.Empty,
                    Service = el.TryGetProperty("service", out var svc) ? svc.GetString() : null,
                    Duration = el.TryGetProperty("durationMs", out var d) && d.ValueKind == JsonValueKind.Number ? TimeSpan.FromMilliseconds(d.GetDouble()) : TimeSpan.Zero,
                    StartTime = el.TryGetProperty("startTime", out var st) && st.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(st.GetString(), out var dto) ? dto : DateTimeOffset.MinValue,
                    Attributes = el.TryGetProperty("attributes", out var at) && at.ValueKind == JsonValueKind.Object ? at.EnumerateObject().ToDictionary(p => p.Name, p => Extract(p.Value)) : new Dictionary<string, object?>()
                };
                list.Add(span);
            }
            catch { /* skip */ }
        }
        return list;
    }

    private static async IAsyncEnumerable<string> ReadLines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        while (!sr.EndOfStream)
        {
            var line = await sr.ReadLineAsync();
            if (line is not null) yield return line;
        }
    }

    private static async Task WriteNdjsonSpans(string path, IReadOnlyList<TraceSpan> spans)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var sw = new StreamWriter(fs);
        foreach (var s in spans)
        {
            var obj = new
            {
                id = s.Id,
                traceId = s.TraceId,
                name = s.Name,
                service = s.Service,
                durationMs = s.Duration.TotalMilliseconds,
                startTime = s.StartTime.ToString("o"),
                attributes = s.Attributes
            };
            var json = JsonSerializer.Serialize(obj);
            await sw.WriteLineAsync(json);
        }
    }

    private static object? Extract(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.TryGetInt64(out var l) ? l : v.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => v.EnumerateArray().Select(e => Extract(e)).ToArray(),
        JsonValueKind.Object => v.EnumerateObject().ToDictionary(p => p.Name, p => Extract(p.Value)),
        _ => null
    };

    private sealed class SingleShotProvider : IObservabilityProvider
    {
        private readonly IReadOnlyList<TraceSpan> _spans;
        public string Name => "offline";
        public SingleShotProvider(IReadOnlyList<TraceSpan> spans) => _spans = spans;
        public Task<IReadOnlyList<TraceSpan>> GetSpansAsync(string query, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
            => Task.FromResult(_spans);
        public Task<IReadOnlyList<MetricPoint>> GetMetricsAsync(string query, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MetricPoint>>(Array.Empty<MetricPoint>());
    }

    private sealed class WindowingProvider(IObservabilityProvider inner, DateTimeOffset from, DateTimeOffset to) : IObservabilityProvider
    {
        public string Name => inner.Name;
        public Task<IReadOnlyList<TraceSpan>> GetSpansAsync(string query, DateTimeOffset _, DateTimeOffset __, CancellationToken cancellationToken = default)
            => inner.GetSpansAsync(query, from, to, cancellationToken);
        public Task<IReadOnlyList<MetricPoint>> GetMetricsAsync(string query, DateTimeOffset _, DateTimeOffset __, CancellationToken cancellationToken = default)
            => inner.GetMetricsAsync(query, from, to, cancellationToken);
    }
}

