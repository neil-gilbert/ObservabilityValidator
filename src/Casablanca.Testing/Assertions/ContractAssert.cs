using Casablanca.Abstractions.Model;
using Casablanca.Engine.Contracts;
using Casablanca.Engine.Validation;
using Casablanca.Testing.InMemory;

namespace Casablanca.Testing.Assertions;

public static class ContractAssert
{
    public static void Pass(string contractsPath, string contractName, IEnumerable<TraceSpan> spans)
    {
        var result = ValidateSingle(contractsPath, contractName, spans);
        if (!result.Passed)
        {
            var details = string.Join("\n - ", result.Details);
            throw new ContractAssertionException(
                $"Contract '{contractName}' failed: {result.Message}\n - {details}");
        }
    }

    public static ValidationResult ValidateSingle(string contractsPath, string contractName, IEnumerable<TraceSpan> spans)
    {
        var file = ContractLoader.Load(contractsPath);
        var contract = file.Contracts.FirstOrDefault(c => c.Name == contractName)
                       ?? throw new ArgumentException($"Contract '{contractName}' not found in file '{contractsPath}'.");

        var provider = new InMemoryProvider(spans);
        var validator = new Validator(provider);

        var tempFile = new ObservabilityContractsFile
        {
            Version = file.Version,
            Contracts = new List<ObservabilityContract> { contract }
        };

        var results = validator.ValidateAsync(tempFile).GetAwaiter().GetResult();
        return results.Single();
    }
}

public sealed class ContractAssertionException : Exception
{
    public ContractAssertionException(string message) : base(message) { }
}

