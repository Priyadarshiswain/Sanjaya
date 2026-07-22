namespace Sanjaya.Core.Contracts;

/// <summary>
/// Stable string values used by the public response contract.
/// </summary>
public static class ContractValues
{
    public const string StatusOk = "ok";
    public const string StatusPartial = "partial";
    public const string StatusError = "error";

    public const string AvailabilitySupported = "supported";
    public const string AvailabilityUnavailable = "unavailable";

    public const string ResolutionNotFound = "not_found";
    public const string ResolutionUnique = "unique";
    public const string ResolutionAmbiguous = "ambiguous";

    public const string ReasonNotImplemented = "not_implemented";
    public const string ReasonRepositoryRootRequired = "repository_root_required";
    public const string ReasonNotGitRepository = "not_git_repository";
    public const string ReasonStructuralProviderUnavailable = "structural_provider_unavailable";
    public const string ReasonRuntimeUnavailable = "runtime_unavailable";
    public const string ReasonDefinitionProviderUnavailable = "definition_provider_unavailable";
    public const string ReasonReferenceProviderUnavailable = "reference_provider_unavailable";
    public const string ReasonSourceProviderUnavailable = "source_provider_unavailable";
    public const string ReasonIndexMissing = "index_missing";
    public const string ReasonIndexInvalid = "index_invalid";

    public const string ErrorRepositoryRootRequired = "repository_root_required";
    public const string ErrorInvalidArgument = "invalid_argument";
    public const string ErrorInvalidPath = "invalid_path";
    public const string ErrorPathOutsideRepository = "path_outside_repository";
    public const string ErrorFileNotFound = "file_not_found";
    public const string ErrorNotAFile = "not_a_file";
    public const string ErrorBinaryFile = "binary_file";
    public const string ErrorFileTooLarge = "file_too_large";
    public const string ErrorFileInaccessible = "file_inaccessible";
    public const string ErrorCancelled = "cancelled";
    public const string ErrorNotGitRepository = "not_git_repository";
    public const string ErrorGitRootMismatch = "git_root_mismatch";
    public const string ErrorGitUnavailable = "git_unavailable";
    public const string ErrorGitTimeout = "git_timeout";
    public const string ErrorGitOutputLimit = "git_output_limit";
    public const string ErrorGitCommandFailed = "git_command_failed";
    public const string ErrorStructuralProviderUnavailable = "structural_provider_unavailable";
    public const string ErrorStructuralProviderTimeout = "structural_provider_timeout";
    public const string ErrorStructuralProviderOutputLimit = "structural_provider_output_limit";
    public const string ErrorStructuralProviderInvalidOutput = "structural_provider_invalid_output";
    public const string ErrorDefinitionProviderUnavailable = "definition_provider_unavailable";
    public const string ErrorReferenceProviderUnavailable = "reference_provider_unavailable";
    public const string ErrorReferenceLimit = "reference_limit";
    public const string ErrorSourceProviderUnavailable = "source_provider_unavailable";
    public const string ErrorChunkNotFound = "chunk_not_found";
    public const string ErrorSourceAmbiguous = "source_ambiguous";
    public const string ErrorSourceResolutionFailed = "source_resolution_failed";
    public const string ErrorSourceRangeTooLarge = "source_range_too_large";
    public const string ErrorIndexPathConflict = "index_path_conflict";
    public const string ErrorIndexBusy = "index_busy";
    public const string ErrorIndexTraversalLimit = "index_traversal_limit";
    public const string ErrorIndexFileLimit = "index_file_limit";
    public const string ErrorIndexSourceLimit = "index_source_limit";
    public const string ErrorIndexChunkLimit = "index_chunk_limit";
    public const string ErrorIndexOutputLimit = "index_output_limit";
    public const string ErrorIndexSourceUnreadable = "index_source_unreadable";
    public const string ErrorIndexWriteFailed = "index_write_failed";
    public const string ErrorIndexMissing = "index_missing";
    public const string ErrorIndexCorrupt = "index_corrupt";
    public const string ErrorIndexIncompatible = "index_incompatible";
    public const string ErrorIndexStale = "index_stale";
    public const string ErrorIndexStateUnverifiable = "index_state_unverifiable";
}
