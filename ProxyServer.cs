using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace QuickBypass;

public record LogEvent(DateTime Time, string Target, bool Bypassed, string Details);

public class ProxyServer
{
    private readonly int _port;
    private readonly DnsResolver _dnsResolver;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    // Live Metrics
    public long ActiveConnections => Interlocked.Read(ref _activeConnections);
    public long TotalConnections => Interlocked.Read(ref _totalConnections);
    public long TotalBypassed => Interlocked.Read(ref _totalBypassed);
    public long TotalBytesTransferred => Interlocked.Read(ref _totalBytesTransferred);

    private long _activeConnections = 0;
    private long _totalConnections = 0;
    private long _totalBypassed = 0;
    private long _totalBytesTransferred = 0;

    public ConcurrentQueue<LogEvent> RecentLogs { get; } = new();

    public ProxyServer(int port = 8080)
    {
        _port = port;
        _dnsResolver = new DnsResolver();
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        Exception? lastEx = null;

        for (int i = 0; i < 5; i++)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Server.NoDelay = true;
                _listener.Start(200);
                lastEx = null;
                break;
            }
            catch (SocketException ex)
            {
                lastEx = ex;
                try { _listener?.Stop(); } catch { }
                Thread.Sleep(150);
            }
        }

        if (lastEx != null) throw lastEx;

        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await _listener!.AcceptSocketAsync(ct);
                clientSocket.NoDelay = true;
                clientSocket.SendBufferSize = 65536;
                clientSocket.ReceiveBufferSize = 65536;

                _ = Task.Run(() => HandleClientAsync(clientSocket, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Listener errors
            }
        }
    }

    private async Task HandleClientAsync(Socket clientSocket, CancellationToken ct)
    {
        Interlocked.Increment(ref _activeConnections);
        Interlocked.Increment(ref _totalConnections);

        using var clientStream = new NetworkStream(clientSocket, ownsSocket: true);
        Socket? targetSocket = null;

        try
        {
            // Read first byte to detect protocol (SOCKS5 vs HTTP CONNECT)
            byte[] firstByte = new byte[1];
            int read = await clientStream.ReadAsync(firstByte.AsMemory(0, 1), ct);
            if (read == 0) return;

            string targetHost;
            int targetPort;
            bool isHttp = false;
            ReadOnlyMemory<byte> pipelinedData = ReadOnlyMemory<byte>.Empty;

            if (firstByte[0] == 0x05)
            {
                // SOCKS5 Protocol
                var socksTarget = await HandleSocks5HandshakeAsync(clientStream, ct);
                if (socksTarget == null) return;

                targetHost = socksTarget.Value.Host;
                targetPort = socksTarget.Value.Port;
            }
            else
            {
                // HTTP / HTTPS CONNECT
                isHttp = true;
                var httpTarget = await HandleHttpProxyHandshakeAsync(clientStream, firstByte[0], ct);
                if (httpTarget == null) return;

                targetHost = httpTarget.Value.Host;
                targetPort = httpTarget.Value.Port;
                pipelinedData = httpTarget.Value.Leftover;
            }

            // Resolve host via Ultra-Fast DoH (Pre-cached Discord Anycast IPs resolve in 0ms)
            var ips = await _dnsResolver.ResolveAsync(targetHost, ct);
            if (ips.Length == 0) return;

            // Connect to target server with optimized socket settings
            targetSocket = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
                SendBufferSize = 65536,
                ReceiveBufferSize = 65536
            };
            await targetSocket.ConnectAsync(ips[0], targetPort, ct);

            // Notify client that connection is established
            if (!isHttp)
            {
                // SOCKS5 Connection Granted
                byte[] socksReply = [0x05, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
                await clientStream.WriteAsync(socksReply, ct);
                await clientStream.FlushAsync(ct);
            }
            else
            {
                // HTTP 200 Connection Established (Flush immediately so client begins TLS without delay)
                byte[] httpReply = "HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray();
                await clientStream.WriteAsync(httpReply, ct);
                await clientStream.FlushAsync(ct);
            }

            // Read the FIRST payload from client (typically TLS ClientHello)
            ReadOnlyMemory<byte> clientHelloData;
            byte[]? rentedBuffer = null;

            if (!pipelinedData.IsEmpty)
            {
                clientHelloData = pipelinedData;
            }
            else
            {
                rentedBuffer = ArrayPool<byte>.Shared.Rent(16384);
                int initialRead = await clientStream.ReadAsync(rentedBuffer.AsMemory(0, rentedBuffer.Length), ct);
                if (initialRead <= 0) return;
                clientHelloData = rentedBuffer.AsMemory(0, initialRead);
            }

            try
            {
                // Apply DPI bypass fragmentation
                var (bypassed, details) = await DpiEngine.SendWithBypassAsync(targetSocket, clientHelloData, ct);
                Interlocked.Add(ref _totalBytesTransferred, clientHelloData.Length);

                if (bypassed)
                {
                    Interlocked.Increment(ref _totalBypassed);
                }

                AddLog(new LogEvent(DateTime.Now, $"{targetHost}:{targetPort}", bypassed, details));
            }
            finally
            {
                if (rentedBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }

            // High-speed zero-copy bidirectional relay
            using var targetStream = new NetworkStream(targetSocket, ownsSocket: true);

            var t1 = RelayStreamAsync(clientStream, targetStream, ct);
            var t2 = RelayStreamAsync(targetStream, clientStream, ct);

            await Task.WhenAny(t1, t2);
        }
        catch (Exception ex)
        {
            AddLog(new LogEvent(DateTime.Now, "ERROR", false, ex.Message));
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
            targetSocket?.Dispose();
        }
    }

    private async Task<(string Host, int Port)?> HandleSocks5HandshakeAsync(Stream stream, CancellationToken ct)
    {
        byte[] nMethodsBuf = new byte[1];
        if (await stream.ReadAsync(nMethodsBuf.AsMemory(0, 1), ct) == 0) return null;
        int nMethods = nMethodsBuf[0];

        byte[] methods = new byte[nMethods];
        if (await stream.ReadAsync(methods.AsMemory(0, nMethods), ct) == 0) return null;

        // Reply 0x05, 0x00 (No authentication)
        await stream.WriteAsync(new byte[] { 0x05, 0x00 }, ct);
        await stream.FlushAsync(ct);

        // Read SOCKS5 request: [VER, CMD, RSV, ATYP]
        byte[] reqHeader = new byte[4];
        if (await stream.ReadAsync(reqHeader.AsMemory(0, 4), ct) < 4) return null;

        if (reqHeader[1] != 0x01) // Only CMD 1 (CONNECT) supported
            return null;

        byte atyp = reqHeader[3];
        string host;

        if (atyp == 0x01) // IPv4
        {
            byte[] ipv4Buf = new byte[4];
            if (await stream.ReadAsync(ipv4Buf.AsMemory(0, 4), ct) < 4) return null;
            host = new IPAddress(ipv4Buf).ToString();
        }
        else if (atyp == 0x03) // Domain name
        {
            byte[] lenBuf = new byte[1];
            if (await stream.ReadAsync(lenBuf.AsMemory(0, 1), ct) == 0) return null;
            int len = lenBuf[0];

            byte[] domainBuf = new byte[len];
            if (await stream.ReadAsync(domainBuf.AsMemory(0, len), ct) < len) return null;
            host = Encoding.ASCII.GetString(domainBuf);
        }
        else if (atyp == 0x04) // IPv6
        {
            byte[] ipv6Buf = new byte[16];
            if (await stream.ReadAsync(ipv6Buf.AsMemory(0, 16), ct) < 16) return null;
            host = new IPAddress(ipv6Buf).ToString();
        }
        else
        {
            return null;
        }

        // Port (2 bytes, big endian)
        byte[] portBuf = new byte[2];
        if (await stream.ReadAsync(portBuf.AsMemory(0, 2), ct) < 2) return null;
        int port = BinaryPrimitives.ReadUInt16BigEndian(portBuf);

        return (host, port);
    }

    /// <summary>
    /// Reads HTTP proxy CONNECT header in one fast batch with leftover bytes preservation.
    /// </summary>
    private async Task<(string Host, int Port, ReadOnlyMemory<byte> Leftover)?> HandleHttpProxyHandshakeAsync(
        Stream stream, 
        byte firstByte, 
        CancellationToken ct)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            buffer[0] = firstByte;
            int totalRead = 1;
            int endOfHeaders = -1;

            while (totalRead < buffer.Length)
            {
                var span = buffer.AsSpan(0, totalRead);
                int idx = span.IndexOf("\r\n\r\n"u8);
                if (idx != -1)
                {
                    endOfHeaders = idx + 4;
                    break;
                }

                int r = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
                if (r == 0) return null;
                totalRead += r;
            }

            if (endOfHeaders == -1) return null;

            string requestText = Encoding.ASCII.GetString(buffer, 0, endOfHeaders);
            var lines = requestText.Split("\r\n");
            if (lines.Length == 0) return null;

            var parts = lines[0].Split(' ');
            if (parts.Length < 2) return null;

            string target = parts[1]; // e.g. "discord.com:443" or "http://example.com/path"
            string host;
            int port;

            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(target);
                host = uri.Host;
                port = uri.Port;
            }
            else
            {
                var hostPort = target.Split(':');
                host = hostPort[0];
                port = hostPort.Length > 1 && int.TryParse(hostPort[1], out var p) ? p : 443;
            }

            // Capture any pipelined data that arrived after \r\n\r\n
            ReadOnlyMemory<byte> leftover = ReadOnlyMemory<byte>.Empty;
            if (totalRead > endOfHeaders)
            {
                byte[] leftoverCopy = new byte[totalRead - endOfHeaders];
                Buffer.BlockCopy(buffer, endOfHeaders, leftoverCopy, 0, leftoverCopy.Length);
                leftover = leftoverCopy;
            }

            return (host, port, leftover);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task RelayStreamAsync(Stream source, Stream destination, CancellationToken ct)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0) break;

                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                Interlocked.Add(ref _totalBytesTransferred, read);
            }
        }
        catch
        {
            // Stream closed
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void AddLog(LogEvent evt)
    {
        RecentLogs.Enqueue(evt);
        while (RecentLogs.Count > 25)
        {
            RecentLogs.TryDequeue(out _);
        }
    }
}
