using System.Text;

namespace Sanjaya.Core.Discovery;

internal static class BoundedTextFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<TextFileReadResult> ReadAsync(
        string fullPath,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            FileInfo file = new(fullPath);
            if (file.Length > maximumBytes)
            {
                return TextFileReadResult.Failure(TextFileReadError.TooLarge);
            }

            int length = checked((int)file.Length);
            byte[] bytes = GC.AllocateUninitializedArray<byte>(length);
            await using FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            int offset = 0;
            while (offset < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            if (offset != bytes.Length || stream.ReadByte() != -1)
            {
                return TextFileReadResult.Failure(TextFileReadError.TooLarge);
            }

            if (bytes.AsSpan().Contains((byte)0))
            {
                return TextFileReadResult.Failure(TextFileReadError.Binary);
            }

            string text;
            try
            {
                int preambleLength = bytes.Length >= 3
                    && bytes[0] == 0xEF
                    && bytes[1] == 0xBB
                    && bytes[2] == 0xBF
                        ? 3
                        : 0;
                text = StrictUtf8.GetString(bytes, preambleLength, bytes.Length - preambleLength);
            }
            catch (DecoderFallbackException)
            {
                return TextFileReadResult.Failure(TextFileReadError.Binary);
            }

            return new TextFileReadResult(text, offset, TextFileReadError.None);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return TextFileReadResult.Failure(TextFileReadError.Inaccessible);
        }
    }
}

internal enum TextFileReadError
{
    None,
    TooLarge,
    Binary,
    Inaccessible,
}

internal sealed record TextFileReadResult(string? Text, int ByteCount, TextFileReadError Error)
{
    public bool IsSuccess => Error == TextFileReadError.None;

    public static TextFileReadResult Failure(TextFileReadError error) => new(null, 0, error);
}
