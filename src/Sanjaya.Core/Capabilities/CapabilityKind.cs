namespace Sanjaya.Core.Capabilities;

/// <summary>
/// Capabilities that a language or fallback provider may implement.
/// </summary>
public enum CapabilityKind
{
    FileOutline,
    StructuralChunking,
    Definitions,
    References,
    SourceRetrieval,
    CallGraph,
}

