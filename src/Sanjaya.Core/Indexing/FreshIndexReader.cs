using Sanjaya.Core.Contracts;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;

namespace Sanjaya.Core.Indexing;

internal sealed class FreshIndexReader(
    RepositoryScope repository,
    IReadOnlyList<IStructuralChunkProvider> providers,
    IndexBuildLimits limits)
{
    public async Task<IndexDocument> ReadAsync(CancellationToken cancellationToken)
    {
        IndexDocument document = await new IndexDocumentReader(repository, providers)
            .ReadCompatibleAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            IndexSourceSnapshot current = await new IndexSourceScanner(repository, providers, limits)
                .CaptureAsync(includeText: false, cancellationToken)
                .ConfigureAwait(false);
            IndexProvider[] activeProviders = providers.Select(provider => new IndexProvider(
                provider.Id,
                provider.ContractVersion,
                provider.Languages.Order(StringComparer.Ordinal).ToArray())).ToArray();
            string currentFingerprint = IndexFingerprint.CreateRepository(activeProviders, current.Files);
            if (currentFingerprint != document.RepositoryFingerprint)
            {
                throw new IndexReadFailure(
                    ContractValues.ErrorIndexStale,
                    "The structural index does not match the current eligible source files.");
            }

            return document;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IndexReadFailure)
        {
            throw;
        }
        catch (IndexSourceScanFailure failure)
        {
            throw new IndexReadFailure(
                ContractValues.ErrorIndexStateUnverifiable,
                $"The current eligible source state could not be verified: {failure.Message}");
        }
    }
}
