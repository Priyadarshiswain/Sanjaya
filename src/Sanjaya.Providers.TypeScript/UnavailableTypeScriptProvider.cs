using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Providers;

namespace Sanjaya.Providers.TypeScript;

public sealed class UnavailableTypeScriptProvider(string language) : IFileOutlineProvider
{
    private readonly string language = language switch
    {
        "typescript" => language,
        "javascript" => language,
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };

    public string Id => language == "typescript"
        ? TypeScriptSyntaxProvider.TypeScriptProviderId
        : TypeScriptSyntaxProvider.JavaScriptProviderId;

    public string ContractVersion => "1";

    public IReadOnlyCollection<string> Languages => [language];

    public bool CanHandle(string relativePath) =>
        TypeScriptSyntaxProvider.CanHandleLanguage(language, relativePath);

    public IReadOnlyCollection<CapabilityDescriptor> GetCapabilities() =>
    [
        RuntimeUnavailable(CapabilityKind.FileOutline),
        RuntimeUnavailable(CapabilityKind.StructuralChunking),
        Deferred(CapabilityKind.Definitions),
        Deferred(CapabilityKind.References),
        Deferred(CapabilityKind.SourceRetrieval),
        Deferred(CapabilityKind.CallGraph),
    ];

    public FileOutlineAnalysis AnalyzeOutline(
        string relativePath,
        string sourceText,
        CancellationToken cancellationToken) =>
        throw new StructuralProviderException(StructuralProviderFailure.Unavailable);

    private CapabilityDescriptor RuntimeUnavailable(CapabilityKind capability) =>
        new(capability, Id, language, CapabilityStatus.Unavailable, ContractValues.ReasonRuntimeUnavailable);

    private CapabilityDescriptor Deferred(CapabilityKind capability) =>
        new(capability, Id, language, CapabilityStatus.Unavailable, ContractValues.ReasonNotImplemented);
}
