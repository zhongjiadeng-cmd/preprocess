using System.Buffers;
using System.Text;

namespace GrayscaleLayersMac;

public static class BoundedTextReader
{
    private const int BufferSize = 8 * 1024;

    public static async Task<string> ReadToEndAsync(
        TextReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCharacters);

        var buffer = ArrayPool<char>.Shared.Rent(BufferSize);
        var text = new StringBuilder(Math.Min(maximumCharacters, BufferSize));
        var exceededLimit = false;
        try
        {
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken);
                if (count == 0)
                    break;

                var remaining = maximumCharacters - text.Length;
                if (count > remaining)
                {
                    exceededLimit = true;
                    if (remaining > 0)
                        text.Append(buffer, 0, remaining);
                }
                else if (!exceededLimit)
                {
                    text.Append(buffer, 0, count);
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        if (exceededLimit)
            throw new ArgumentException("子进程输出过大。", nameof(maximumCharacters));

        return text.ToString();
    }
}
