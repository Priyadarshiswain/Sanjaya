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

    public const string ReasonNotImplemented = "not_implemented";
    public const string ReasonRepositoryRootRequired = "repository_root_required";

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
}
