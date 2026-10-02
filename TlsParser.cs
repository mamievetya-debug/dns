using System.Buffers.Binary;
using System.Text;

namespace QuickBypass;

public record SniInfo(string Hostname, int SniOffset, int SniLength);

public static class TlsParser
{
    /// <summary>
    /// Checks if the buffer starts with a TLS ClientHello packet.
    /// </summary>
    public static bool IsClientHello(ReadOnlySpan<byte> data)
    {
        if (data.Length < 9)
            return false;

        // Byte 0: 0x16 (TLS Handshake)
        // Byte 1: 0x03 (TLS major version 3)
        // Byte 5: 0x01 (Handshake Type: Client Hello)
        return data[0] == 0x16 && data[1] == 0x03 && data[5] == 0x01;
    }

    /// <summary>
    /// Extracts the Server Name Indication (SNI) and its byte offset in the buffer.
    /// </summary>
    public static bool TryExtractSni(ReadOnlySpan<byte> data, out SniInfo? sniInfo)
    {
        sniInfo = null;
        if (!IsClientHello(data))
            return false;

        try
        {
            int offset = 5; // Skip TLS Record Header (5 bytes)
            if (offset >= data.Length) return false;

            // Handshake header: type (1) + length (3) + version (2) + random (32) = 38 bytes
            offset += 4 + 2 + 32;
            if (offset >= data.Length) return false;

            // Session ID
            int sessionIdLength = data[offset];
            offset += 1 + sessionIdLength;
            if (offset + 2 > data.Length) return false;

            // Cipher Suites
            int cipherSuitesLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            offset += 2 + cipherSuitesLength;
            if (offset + 1 > data.Length) return false;

            // Compression Methods
            int compressionMethodsLength = data[offset];
            offset += 1 + compressionMethodsLength;
            if (offset + 2 > data.Length) return false;

            // Extensions Length
            int extensionsLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            offset += 2;

            int extensionsEnd = offset + extensionsLength;
            if (extensionsEnd > data.Length)
                extensionsEnd = data.Length;

            while (offset + 4 <= extensionsEnd)
            {
                ushort extType = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
                ushort extLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 2, 2));
                offset += 4;

                if (offset + extLen > extensionsEnd)
                    break;

                // 0x0000 = server_name (SNI)
                if (extType == 0)
                {
                    if (extLen >= 5)
                    {
                        // Server Name List Length (2 bytes)
                        // Server Name Type (1 byte) -> 0 = host_name
                        byte nameType = data[offset + 2];
                        if (nameType == 0)
                        {
                            ushort nameLen = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 3, 2));
                            int nameOffset = offset + 5;

                            if (nameOffset + nameLen <= data.Length)
                            {
                                string hostname = Encoding.ASCII.GetString(data.Slice(nameOffset, nameLen));
                                sniInfo = new SniInfo(hostname, nameOffset, nameLen);
                                return true;
                            }
                        }
                    }
                }

                offset += extLen;
            }
        }
        catch
        {
            // Invalid/partial packet
        }

        return false;
    }
}
