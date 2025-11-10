using ObservabilityValidator.Core.Contracts;
using ObservabilityValidator.Core.Providers;
using ObservabilityValidator.Core.Validation;
using ObservabilityValidator.Providers.Datadog;

namespace ObservabilityValidator.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var contractsPath = args.FirstOrDefault(a => a.StartsWith("--contracts="))?
                                .Split('=')[1]
                            ?? "contracts/observability-contracts.yaml";

        var configPath = args.FirstOrDefault(a => a.StartsWith("--config="))?
                                .Split('=')[1]
                            ?? "contracts/telemetry-config.yaml";

        Console.WriteLine($"Using contracts: {contractsPath}");
        Console.WriteLine($"Using config   : {configPath}");

        var contractsFile = ContractLoader.Load(contractsPath);
        var telemetryConfig = ProviderFactory.LoadConfig(configPath);

        var providers = new List<IObservabilityProvider>();

        foreach (var p in telemetryConfig.Providers.Where(p => p.Enabled))
        {
            //TODO : Dynamic load these via reflection or DI container
            //Don't hardcode provider types here
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

                default:
                    Console.WriteLine($"WARN: Provider type '{p.Type}' is not supported in CLI (name: {p.Name}).");
                    break;
            }
        }

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

            var validator = new Validator(provider);
            var results = await validator.ValidateAsync(contractsFile);

            foreach (var result in results)
            {
                var icon = result.Passed ? "✅" : "❌";
                Console.WriteLine($"{icon} [{result.ContractName}] {result.Message}");

                foreach (var detail in result.Details)
                {
                    Console.WriteLine($"   - {detail}");
                }

                if (!result.Passed)
                    exitCode = 2;
            }
        }

        return exitCode;
    }
}
