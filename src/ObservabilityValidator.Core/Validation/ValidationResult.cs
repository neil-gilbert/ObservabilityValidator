namespace ObservabilityValidator.Core.Validation;

public sealed class ValidationResult
{
    public string ProviderName { get; }
    public string ContractName { get; }
    public bool Passed { get; }
    public string Message { get; }
    public IReadOnlyList<string> Details { get; }

    public ValidationResult(
        string providerName,
        string contractName,
        bool passed,
        string message,
        IReadOnlyList<string>? details = null)
    {
        ProviderName = providerName;
        ContractName = contractName;
        Passed = passed;
        Message = message;
        Details = details ?? Array.Empty<string>();
    }
}
