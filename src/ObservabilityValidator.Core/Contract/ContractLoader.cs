using ObservabilityValidator.Core.Contracts;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ObservabilityValidator.Core.Contracts;

public static class ContractLoader
{
    public static ObservabilityContractsFile Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Contracts file not found", path);

        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        return deserializer.Deserialize<ObservabilityContractsFile>(yaml)
               ?? new ObservabilityContractsFile();
    }
}