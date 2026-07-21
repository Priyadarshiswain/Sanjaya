using Sanjaya.Core.Capabilities;

namespace Sanjaya.Core.Providers;

/// <summary>
/// Common discovery surface for capability-oriented providers.
/// Operation-specific interfaces extend this contract only when implemented.
/// </summary>
public interface ICapabilityProvider
{
    string Id { get; }

    string ContractVersion { get; }

    IReadOnlyCollection<string> Languages { get; }

    IReadOnlyCollection<CapabilityDescriptor> GetCapabilities();
}
