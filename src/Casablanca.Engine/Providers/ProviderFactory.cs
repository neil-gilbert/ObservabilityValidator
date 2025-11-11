using Casablanca.Abstractions.Providers;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Casablanca.Engine.Providers;

public sealed class TelemetryConfig
{
    public string Version { get; set; } = "1";
    public List<ProviderConfig> Providers { get; set; } = [];
}

public sealed class ProviderConfig
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Settings { get; set; } = new();
}

public static class ProviderFactory
{
    public static TelemetryConfig LoadConfig(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Telemetry config file not found", path);

        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        return deserializer.Deserialize<TelemetryConfig>(yaml)
               ?? new TelemetryConfig();
    }

    public static IReadOnlyList<IObservabilityProvider> CreateProviders(
        TelemetryConfig config,
        Func<string, IObservabilityProvider?>? customResolver = null)
    {
        var providers = new List<IObservabilityProvider>();

        foreach (var p in config.Providers.Where(p => p.Enabled))
        {
            IObservabilityProvider? provider = null;

            provider = customResolver?.Invoke(p.Type);

            if (provider is null)
            {
                throw new InvalidOperationException(
                    $"Provider type '{p.Type}' is not resolved in core. " +
                    "Wire concrete providers in the CLI or composition root.");
            }

            providers.Add(provider);
        }

        return providers;
    }
}

