using System.Net.Sockets;
using System.Text;

namespace QuickBypass;

public class DpiEngine
{
    /// <summary>
    /// Sends client data to the target server, applying DPI bypass fragmentation if applicable.
    /// </summary>
    public static async Task<(bool Bypassed, string Details)> SendWithBypassAsync(
        Socket targetSocket, 
        ReadOnlyMemory<byte> buffer, 
        CancellationToken ct = default)
    {
        targetSocket.NoDelay = true;

        var span = buffer.Span;

        if (TlsParser.TryExtractSni(span, out var sniInfo) && sniInfo != null)
        {
            // Split right in the middle of the SNI domain!
            // First chunk ends with "updates.di"
            // Second chunk begins with "scord.com"
            // Neither TCP segment contains the blocked domain name!
            int splitOffset = sniInfo.SniOffset + (sniInfo.SniLength / 2);

            var chunk1 = buffer.Slice(0, splitOffset);
            var chunk2 = buffer.Slice(splitOffset);

            await targetSocket.SendAsync(chunk1, SocketFlags.None, ct);
            await targetSocket.SendAsync(chunk2, SocketFlags.None, ct);

            return (true, $"TLS SNI Split: [{sniInfo.Hostname}] @ byte {splitOffset}");
        }

        // 3. Plaintext HTTP (Port 80) Host header manipulation
        if (IsHttpRequest(span, out int hostHeaderIndex))
        {
            // Modify 'Host:' to 'hoSt:' in a copy of buffer to confuse passive DPI
            byte[] modified = buffer.ToArray();
            if (hostHeaderIndex + 4 < modified.Length)
            {
                // 'H' (72) -> 'h' (104), 'o' (111) -> 'o' (111), 'S' (83) -> 'S' (83), 't' (116) -> 't' (116)
                modified[hostHeaderIndex] = (byte)'h';
                modified[hostHeaderIndex + 2] = (byte)'S';
            }

            await targetSocket.SendAsync(modified, SocketFlags.None, ct);
            return (true, "HTTP 'hoSt' header evasion applied");
        }

        // Default: Send untouched
        await targetSocket.SendAsync(buffer, SocketFlags.None, ct);
        return (false, "Direct transfer");
    }

    private static bool IsHttpRequest(ReadOnlySpan<byte> span, out int hostIndex)
    {
        hostIndex = -1;
        if (span.Length < 16) return false;

        bool isMethod = span.StartsWith("GET "u8) || 
                        span.StartsWith("POST "u8) || 
                        span.StartsWith("HEAD "u8) || 
                        span.StartsWith("PUT "u8) || 
                        span.StartsWith("DELETE "u8);

        if (!isMethod) return false;

        // Search for "\r\nHost: "
        int idx = span.IndexOf("\r\nHost: "u8);
        if (idx != -1)
        {
            hostIndex = idx + 2; // Offset to 'H'
            return true;
        }

        return false;
    }
}
