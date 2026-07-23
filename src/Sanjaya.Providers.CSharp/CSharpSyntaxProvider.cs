using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Discovery;
using Sanjaya.Core.Providers;

namespace Sanjaya.Providers.CSharp;

/// <summary>
/// Provides deterministic syntax-only C# structure. It never loads a project,
/// invokes a compiler, or claims semantic resolution.
/// </summary>
public sealed class CSharpSyntaxProvider :
    IFileOutlineProvider,
    IStructuralChunkProvider,
    IReferenceProvider,
    ISourceRetrievalProvider
{
    public const string ProviderId = "csharp-roslyn-syntax";

    public string Id => ProviderId;

    public string ContractVersion => "1";

    public IReadOnlyCollection<string> Languages { get; } = ["csharp"];

    public bool CanHandle(string relativePath) =>
        string.Equals(Path.GetExtension(relativePath), ".cs", StringComparison.OrdinalIgnoreCase);

    public bool IsValidName(string name) =>
        SyntaxFacts.IsValidIdentifier(name)
        || SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None;

    public IReadOnlyCollection<CapabilityDescriptor> GetCapabilities() =>
    [
        Supported(CapabilityKind.FileOutline),
        Supported(CapabilityKind.StructuralChunking),
        Supported(CapabilityKind.Definitions),
        Supported(CapabilityKind.References),
        Supported(CapabilityKind.SourceRetrieval),
        Deferred(CapabilityKind.CallGraph),
    ];

    public FileOutlineAnalysis AnalyzeOutline(
        string relativePath,
        string sourceText,
        CancellationToken cancellationToken)
    {
        ParsedFile parsed = Parse(sourceText, cancellationToken);
        Declaration[] declarations = EnumerateDeclarations(parsed.Root, cancellationToken)
            .Take(DiscoveryLimits.MaximumOutlineItems + 1)
            .ToArray();
        bool truncated = declarations.Length > DiscoveryLimits.MaximumOutlineItems;
        OutlineItem[] items = declarations
            .Take(DiscoveryLimits.MaximumOutlineItems)
            .Select(declaration => declaration.Item)
            .ToArray();

        return new FileOutlineAnalysis(items, truncated, parsed.SyntaxDiagnosticCount);
    }

    public StructuralChunkAnalysis AnalyzeChunks(
        string relativePath,
        string sourceText,
        CancellationToken cancellationToken)
    {
        ParsedFile parsed = Parse(sourceText, cancellationToken);
        Declaration[] declarations = EnumerateDeclarations(parsed.Root, cancellationToken)
            .Take(DiscoveryLimits.MaximumOutlineItems + 1)
            .ToArray();
        bool truncated = declarations.Length > DiscoveryLimits.MaximumOutlineItems;
        StructuralChunk[] chunks = declarations
            .Take(DiscoveryLimits.MaximumOutlineItems)
            .Select(declaration => CreateChunk(declaration))
            .ToArray();

        return new StructuralChunkAnalysis(chunks, truncated, parsed.SyntaxDiagnosticCount);
    }

    public ReferenceAnalysis AnalyzeReferences(
        string relativePath,
        string sourceText,
        string name,
        CancellationToken cancellationToken)
    {
        ParsedFile parsed = Parse(sourceText, cancellationToken);
        SyntaxReferenceCandidate[] matches = parsed.Root.DescendantNodes()
            .OfType<SimpleNameSyntax>()
            .Where(candidate =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return candidate.Identifier.ValueText.Equals(name, StringComparison.Ordinal);
            })
            .Take(ReferenceLookupLimits.MaximumMatchesPerFile + 1)
            .Select(candidate => CreateReference(candidate, sourceText))
            .ToArray();
        bool truncated = matches.Length > ReferenceLookupLimits.MaximumMatchesPerFile;
        return new ReferenceAnalysis(
            matches.Take(ReferenceLookupLimits.MaximumMatchesPerFile).ToArray(),
            truncated,
            parsed.SyntaxDiagnosticCount);
    }

    public SourceRetrievalAnalysis AnalyzeSource(
        string relativePath,
        string sourceText,
        SourceRetrievalTarget target,
        CancellationToken cancellationToken)
    {
        ParsedFile parsed = Parse(sourceText, cancellationToken);
        SourceDeclaration[] matches = EnumerateDeclarations(parsed.Root, cancellationToken)
            .Take(DiscoveryLimits.MaximumOutlineItems + 1)
            .Where(declaration => MatchesTarget(CreateChunk(declaration), target))
            .Select(declaration => CreateSourceDeclaration(declaration.Node, sourceText))
            .ToArray();
        return new SourceRetrievalAnalysis(matches, parsed.SyntaxDiagnosticCount);
    }

    private static ParsedFile Parse(string sourceText, CancellationToken cancellationToken)
    {
        CSharpParseOptions options = new(LanguageVersion.Latest, DocumentationMode.Parse);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceText, options, cancellationToken: cancellationToken);
        CompilationUnitSyntax root = (CompilationUnitSyntax)tree.GetRoot(cancellationToken);
        int diagnostics = tree.GetDiagnostics(cancellationToken)
            .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return new ParsedFile(root, diagnostics);
    }

    private static IEnumerable<Declaration> EnumerateDeclarations(
        CompilationUnitSyntax root,
        CancellationToken cancellationToken)
    {
        foreach (SyntaxNode node in root.DescendantNodes().OrderBy(node => node.SpanStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryCreateItem(node) is OutlineItem item)
            {
                yield return new Declaration(node, item);
            }
        }
    }

    private static OutlineItem? TryCreateItem(SyntaxNode node)
    {
        (string Kind, string Name, string Display)? description = node switch
        {
            FileScopedNamespaceDeclarationSyntax declaration =>
                ("namespace", declaration.Name.ToString(), $"namespace {declaration.Name}"),
            NamespaceDeclarationSyntax declaration =>
                ("namespace", declaration.Name.ToString(), $"namespace {declaration.Name}"),
            RecordDeclarationSyntax declaration =>
                (declaration.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record_struct" : "record",
                    declaration.Identifier.ValueText,
                    Join(declaration.Modifiers, declaration.Keyword, declaration.ClassOrStructKeyword,
                        declaration.Identifier, declaration.TypeParameterList, declaration.ParameterList)),
            ClassDeclarationSyntax declaration =>
                ("class", declaration.Identifier.ValueText, TypeDisplay(declaration)),
            StructDeclarationSyntax declaration =>
                ("struct", declaration.Identifier.ValueText, TypeDisplay(declaration)),
            InterfaceDeclarationSyntax declaration =>
                ("interface", declaration.Identifier.ValueText, TypeDisplay(declaration)),
            EnumDeclarationSyntax declaration =>
                ("enum", declaration.Identifier.ValueText,
                    Join(declaration.Modifiers, declaration.EnumKeyword, declaration.Identifier)),
            DelegateDeclarationSyntax declaration =>
                ("delegate", declaration.Identifier.ValueText,
                    Join(declaration.Modifiers, declaration.DelegateKeyword, declaration.ReturnType,
                        declaration.Identifier, declaration.TypeParameterList, declaration.ParameterList)),
            MethodDeclarationSyntax declaration =>
                ("method", declaration.Identifier.ValueText,
                    Join(declaration.Modifiers, declaration.ReturnType, declaration.ExplicitInterfaceSpecifier,
                        declaration.Identifier, declaration.TypeParameterList, declaration.ParameterList)),
            ConstructorDeclarationSyntax declaration =>
                ("constructor", declaration.Identifier.ValueText,
                    Join(declaration.Modifiers, declaration.Identifier, declaration.ParameterList)),
            DestructorDeclarationSyntax declaration =>
                ("destructor", $"~{declaration.Identifier.ValueText}",
                    Join(declaration.TildeToken, declaration.Identifier, declaration.ParameterList)),
            PropertyDeclarationSyntax declaration =>
                ("property", declaration.Identifier.ValueText,
                    Join(declaration.Modifiers, declaration.Type, declaration.ExplicitInterfaceSpecifier,
                        declaration.Identifier)),
            IndexerDeclarationSyntax declaration =>
                ("indexer", "this",
                    Join(declaration.Modifiers, declaration.Type, declaration.ExplicitInterfaceSpecifier,
                        declaration.ThisKeyword, declaration.ParameterList)),
            OperatorDeclarationSyntax declaration =>
                ("operator", $"operator {declaration.OperatorToken.ValueText}",
                    Join(declaration.Modifiers, declaration.ReturnType, declaration.OperatorKeyword,
                        declaration.OperatorToken, declaration.ParameterList)),
            ConversionOperatorDeclarationSyntax declaration =>
                ("conversion_operator", $"operator {declaration.Type}",
                    Join(declaration.Modifiers, declaration.ImplicitOrExplicitKeyword,
                        declaration.OperatorKeyword, declaration.Type, declaration.ParameterList)),
            _ => null,
        };

        if (description is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(description.Value.Name))
        {
            return null;
        }

        FileLinePositionSpan lines = node.SyntaxTree.GetLineSpan(node.Span);
        return new OutlineItem(
            description.Value.Kind,
            Bound(description.Value.Name),
            Bound(description.Value.Display),
            GetContainer(node),
            lines.StartLinePosition.Line + 1,
            lines.EndLinePosition.Line + 1);
    }

    private static string TypeDisplay(TypeDeclarationSyntax declaration) =>
        Join(declaration.Modifiers, declaration.Keyword, declaration.Identifier, declaration.TypeParameterList);

    private static string? GetContainer(SyntaxNode node)
    {
        string[] parts = node.Ancestors()
            .Where(ancestor => ancestor is BaseNamespaceDeclarationSyntax or BaseTypeDeclarationSyntax)
            .Reverse()
            .Select(ancestor => ancestor switch
            {
                BaseNamespaceDeclarationSyntax declaration => declaration.Name.ToString(),
                BaseTypeDeclarationSyntax declaration => declaration.Identifier.ValueText,
                _ => string.Empty,
            })
            .Where(part => part.Length > 0)
            .ToArray();
        return parts.Length == 0 ? null : string.Join('.', parts);
    }

    private static StructuralChunk CreateChunk(Declaration declaration)
    {
        string content = declaration.Node.ToFullString().Trim();
        bool truncated = content.Length > DiscoveryLimits.MaximumChunkCharacters;
        if (truncated)
        {
            content = content[..DiscoveryLimits.MaximumChunkCharacters];
        }

        OutlineItem item = declaration.Item;
        return new StructuralChunk(
            item.Kind,
            item.Name,
            item.Container,
            item.StartLine,
            item.EndLine,
            content,
            truncated);
    }

    private static bool MatchesTarget(StructuralChunk chunk, SourceRetrievalTarget target) =>
        chunk.Kind.Equals(target.Kind, StringComparison.Ordinal)
        && chunk.Name.Equals(target.Name, StringComparison.Ordinal)
        && string.Equals(chunk.Container, target.Container, StringComparison.Ordinal)
        && chunk.StartLine == target.StartLine
        && chunk.EndLine == target.EndLine
        && chunk.Content.Equals(target.IndexedContent, StringComparison.Ordinal)
        && chunk.ContentTruncated == target.IndexedContentTruncated;

    private static SourceDeclaration CreateSourceDeclaration(SyntaxNode node, string sourceText)
    {
        int start = node.FullSpan.Start;
        int end = node.FullSpan.End;
        while (start < end && char.IsWhiteSpace(sourceText[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(sourceText[end - 1]))
        {
            end--;
        }

        TextSpan span = TextSpan.FromBounds(start, end);
        FileLinePositionSpan lines = node.SyntaxTree.GetLineSpan(span);
        return new SourceDeclaration(
            sourceText[start..end],
            lines.StartLinePosition.Line + 1,
            lines.StartLinePosition.Character + 1,
            lines.EndLinePosition.Line + 1,
            lines.EndLinePosition.Character + 1);
    }

    private static SyntaxReferenceCandidate CreateReference(SimpleNameSyntax reference, string sourceText)
    {
        FileLinePositionSpan lines = reference.SyntaxTree.GetLineSpan(reference.Identifier.Span);
        OutlineItem? enclosing = reference.Ancestors()
            .Select(TryCreateItem)
            .FirstOrDefault(item => item is not null);
        int lineStart = sourceText.LastIndexOf('\n', Math.Max(0, reference.SpanStart - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        int lineEnd = sourceText.IndexOf('\n', reference.Span.End);
        lineEnd = lineEnd < 0 ? sourceText.Length : lineEnd;
        string snippet = sourceText[lineStart..lineEnd].TrimEnd('\r');
        if (snippet.Length > ReferenceLookupLimits.MaximumSnippetCharacters)
        {
            int tokenOffset = reference.SpanStart - lineStart;
            int start = Math.Clamp(
                tokenOffset - (ReferenceLookupLimits.MaximumSnippetCharacters / 2),
                0,
                snippet.Length - ReferenceLookupLimits.MaximumSnippetCharacters);
            snippet = snippet.Substring(start, ReferenceLookupLimits.MaximumSnippetCharacters);
        }

        return new SyntaxReferenceCandidate(
            reference is GenericNameSyntax ? "generic_name" : "identifier_name",
            enclosing?.Kind,
            enclosing?.Name,
            enclosing?.Container,
            lines.StartLinePosition.Line + 1,
            lines.StartLinePosition.Character + 1,
            lines.EndLinePosition.Line + 1,
            lines.EndLinePosition.Character + 1,
            snippet);
    }

    private static string Join(params object?[] parts)
    {
        string value = string.Join(' ', parts
            .Select(part => part?.ToString())
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        return Bound(CollapseWhitespace(value));
    }

    private static string CollapseWhitespace(string value)
    {
        char[] buffer = new char[value.Length];
        int length = 0;
        bool pendingSpace = false;
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = length > 0;
                continue;
            }

            if (pendingSpace)
            {
                buffer[length++] = ' ';
                pendingSpace = false;
            }

            buffer[length++] = character;
        }

        return new string(buffer, 0, length);
    }

    private static string Bound(string value) =>
        value.Length <= DiscoveryLimits.MaximumOutlineDisplayCharacters
            ? value
            : value[..DiscoveryLimits.MaximumOutlineDisplayCharacters];

    private CapabilityDescriptor Supported(CapabilityKind capability) =>
        new(capability, Id, "csharp", CapabilityStatus.Supported);

    private CapabilityDescriptor Deferred(CapabilityKind capability) =>
        new(capability, Id, "csharp", CapabilityStatus.Unavailable, ContractValues.ReasonNotImplemented);

    private sealed record ParsedFile(CompilationUnitSyntax Root, int SyntaxDiagnosticCount);

    private sealed record Declaration(SyntaxNode Node, OutlineItem Item);
}
