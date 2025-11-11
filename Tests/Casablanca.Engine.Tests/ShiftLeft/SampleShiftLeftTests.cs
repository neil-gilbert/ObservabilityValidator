using Casablanca.Abstractions.Model;
using Casablanca.Testing.Assertions;
using Xunit;

namespace Casablanca.Engine.Tests.ShiftLeft;

public class SampleShiftLeftTests
{
    [Fact]
    public void PaymentFlow_HasSuccessSpan_Locally()
    {
        var now = DateTimeOffset.UtcNow;
        var spans = new List<TraceSpan>
        {
            new()
            {
                Id = "1",
                TraceId = "abc",
                Name = "POST /payments",
                Service = "payment-api",
                Duration = TimeSpan.FromMilliseconds(1200),
                StartTime = now,
                Attributes = new Dictionary<string, object?>
                {
                    ["payment.status"] = "success",
                    ["customer.id"] = "123"
                }
            }
        };

        ContractAssert.Pass(
            contractsPath: "src/Casablanca.Cli/Contracts/observability-contracts.yaml",
            contractName: "Payment Flow Success",
            spans: spans);
    }
}

