using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Sanjaya.Core.Repositories;

namespace Sanjaya.Core.Git;

public interface IGitCommandRunner
{
    Task<GitCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public enum GitCommandStatus
{
    Completed,
    Unavailable,
    TimedOut,
    OutputLimit,
    Cancelled,
    InvalidOutput,
}

public sealed record GitCommandResult(
    GitCommandStatus Status,
    int ExitCode,
    string StandardOutput,
    string StandardError);

public sealed class GitCommandRunner(RepositoryScope repository) : IGitCommandRunner
{
    public const int MaximumOutputBytes = 2 * 1024 * 1024;
    public const int MaximumErrorBytes = 32 * 1024;
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<GitCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!repository.IsReady)
        {
            return new(GitCommandStatus.Unavailable, -1, string.Empty, string.Empty);
        }

        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = repository.CanonicalRoot!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string fixedArgument in new[]
                 {
                     "--no-pager",
                     "-c", "color.ui=false",
                     "-c", "core.quotepath=false",
                     "-c", "core.fsmonitor=false",
                 })
        {
            start.ArgumentList.Add(fixedArgument);
        }

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (string key in start.Environment.Keys
                     .Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            start.Environment.Remove(key);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_PAGER"] = "cat";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

        using Process process = new() { StartInfo = start };
        try
        {
            if (!process.Start())
            {
                return new(GitCommandStatus.Unavailable, -1, string.Empty, string.Empty);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new(GitCommandStatus.Unavailable, -1, string.Empty, string.Empty);
        }

        process.StandardInput.Close();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        Task<BoundedBytes> stdout = ReadBoundedAsync(process.StandardOutput.BaseStream, MaximumOutputBytes, timeout.Token);
        Task<BoundedBytes> stderr = ReadBoundedAsync(process.StandardError.BaseStream, MaximumErrorBytes, timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            BoundedBytes[] output = await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            if (output.Any(item => item.Exceeded))
            {
                return new(GitCommandStatus.OutputLimit, process.ExitCode, string.Empty, string.Empty);
            }

            try
            {
                return new(
                    GitCommandStatus.Completed,
                    process.ExitCode,
                    StrictUtf8.GetString(output[0].Bytes),
                    StrictUtf8.GetString(output[1].Bytes));
            }
            catch (DecoderFallbackException)
            {
                return new(GitCommandStatus.InvalidOutput, process.ExitCode, string.Empty, string.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new(
                cancellationToken.IsCancellationRequested ? GitCommandStatus.Cancelled : GitCommandStatus.TimedOut,
                -1,
                string.Empty,
                string.Empty);
        }
    }

    private static async Task<BoundedBytes> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using MemoryStream stored = new(Math.Min(maximumBytes, 16 * 1024));
        byte[] buffer = new byte[16 * 1024];
        bool exceeded = false;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            int remaining = maximumBytes - checked((int)stored.Length);
            if (remaining > 0)
            {
                stored.Write(buffer, 0, Math.Min(remaining, read));
            }

            exceeded |= read > remaining;
        }

        return new(stored.ToArray(), exceeded);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and termination.
        }
    }

    private sealed record BoundedBytes(byte[] Bytes, bool Exceeded);
}
