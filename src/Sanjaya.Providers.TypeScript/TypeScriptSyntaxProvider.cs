using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Providers;

namespace Sanjaya.Providers.TypeScript;

public sealed class TypeScriptSyntaxProvider : IFileOutlineProvider, IStructuralChunkProvider
{
    public const string TypeScriptProviderId = "typescript-compiler-syntax";
    public const string JavaScriptProviderId = "javascript-typescript-syntax";

    private static readonly HashSet<string> TypeScriptExtensions = new(
        [".ts", ".tsx", ".mts", ".cts"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> JavaScriptExtensions = new(
        [".js", ".jsx", ".mjs", ".cjs"],
        StringComparer.OrdinalIgnoreCase);

    private readonly ITypeScriptWorker worker;
    private readonly HashSet<string> extensions;

    public TypeScriptSyntaxProvider(string language, ITypeScriptWorker worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        Language = language switch
        {
            "typescript" => language,
            "javascript" => language,
            _ => throw new ArgumentOutOfRangeException(nameof(language)),
        };
        this.worker = worker;
        extensions = Language == "typescript" ? TypeScriptExtensions : JavaScriptExtensions;
        Id = Language == "typescript" ? TypeScriptProviderId : JavaScriptProviderId;
        Languages = [Language];
    }

    public string Id { get; }

    public string ContractVersion => "1";

    public string Language { get; }

    public IReadOnlyCollection<string> Languages { get; }

    public bool CanHandle(string relativePath) => extensions.Contains(Path.GetExtension(relativePath));

    internal static bool CanHandleLanguage(string language, string relativePath) => language switch
    {
        "typescript" => TypeScriptExtensions.Contains(Path.GetExtension(relativePath)),
        "javascript" => JavaScriptExtensions.Contains(Path.GetExtension(relativePath)),
        _ => false,
    };

    public IReadOnlyCollection<CapabilityDescriptor> GetCapabilities() =>
    [
        Supported(CapabilityKind.FileOutline),
        Supported(CapabilityKind.StructuralChunking),
        Deferred(CapabilityKind.Definitions),
        Deferred(CapabilityKind.References),
        Deferred(CapabilityKind.SourceRetrieval),
        Deferred(CapabilityKind.CallGraph),
    ];

    public FileOutlineAnalysis AnalyzeOutline(
        string relativePath,
        string sourceText,
        CancellationToken cancellationToken)
    {
        TypeScriptWorkerAnalysis analysis = worker.Analyze(
            relativePath,
            Language,
            sourceText,
            cancellationToken);
        return new FileOutlineAnalysis(
            analysis.Items,
            analysis.ItemsTruncated,
            analysis.SyntaxDiagnosticCount);
    }

    public StructuralChunkAnalysis AnalyzeChunks(
        string relativePath,
        string sourceText,
        CancellationToken cancellationToken)
    {
        TypeScriptWorkerAnalysis analysis = worker.Analyze(
            relativePath,
            Language,
            sourceText,
            cancellationToken);
        return new StructuralChunkAnalysis(
            analysis.Chunks,
            analysis.ChunksTruncated,
            analysis.SyntaxDiagnosticCount);
    }

    private CapabilityDescriptor Supported(CapabilityKind capability) =>
        new(capability, Id, Language, CapabilityStatus.Supported);

    private CapabilityDescriptor Deferred(CapabilityKind capability) =>
        new(capability, Id, Language, CapabilityStatus.Unavailable, ContractValues.ReasonNotImplemented);
}
