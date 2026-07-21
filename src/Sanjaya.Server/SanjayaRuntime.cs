using Sanjaya.Core.Contracts;

namespace Sanjaya.Server;

/// <summary>
/// Stable facts about the currently implemented runtime.
/// </summary>
public static class SanjayaRuntime
{
    public const string BuildVersion = "0.0.0-development";
    public const string Transport = "stdio";
    public const bool DefaultNetworkAccess = false;

    public static int RegisteredToolCount =>
        PublicToolNames.ProtocolFoundation.Count
        + PublicToolNames.ImmediateDiscovery.Count
        + PublicToolNames.LocalGitEvidence.Count;
}
