using Sanjaya.Core.Capabilities;

namespace Sanjaya.Core.Providers;

/// <summary>
/// Common discovery surface for capability-oriented providers.
/// Operation-specific interfaces will extend this contract as they are implemented.
/// </summary>
public interface ICapabilityProvider
{
    string Id { get; }

    IReadOnlyCollection<string> Languages { get; }

    IReadOnlyCollection<CapabilityDescriptor> GetCapabilities();
}

