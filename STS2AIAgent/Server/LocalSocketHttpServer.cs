using System.Net;
using System.Net.Sockets;
using System.Text;

namespace STS2AIAgent.Server;

/// <summary>
/// Loopback HTTP/1.1 over a plain TCP socket, used only when the platform listener cannot start.
/// </summary>
/// <remarks>
/// Windows returned ERROR_INVALID_HANDLE from <c>HttpListener.Start()</c> for the GitHub and Workshop
/// build layouts on game v0.113.1 while plain loopback TCP worked in the same process, so the mod
/// needs a transport that does not go through the HTTP Server API at all. This is deliberately a
/// small, strict server for a single local client population: HTTP/1.1 only, one request per
/// connection, a hard cap on header and body bytes, and no support for upgrades, pipelining or
/// ranges. Routing, security checks and payloads stay in <see cref="Router"/>; this class only
/// moves bytes.
/// </remarks>
internal sealed class LocalSocketHttpServer : IDisposable
{
    internal const int MaxHeaderBytes = 32 * 1024;
    internal const int MaxRequestBodyBytes = 2 * 1024 * 1024;
    internal const int MaxConcurrentConnections = 16;
    private const int ReadBufferBytes = 8 * 1024;

    private static readonly byte[] CrLf = "\r\n"u8.ToArray();
    private static readonly byte[] LastChunk = "0\r\n\r\n"u8.ToArray();

    private readonly TcpListener _listener;
    private readonly Func<HttpRequestContext, CancellationToken, Task> _handler;
    private readonly SemaphoreSlim _connectionSlots = new(MaxConcurrentConnections);
    private readonly CancellationTokenSource _stop = new();
    private Task? _acceptLoop;

    public LocalSocketHttpServer(int port, Func<HttpRequestContext, CancellationToken, Task> handler)
    {
        _handler = handler;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    public void Start()
    {
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_stop.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptSocketAsync(cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // A transient accept failure must not end the loop: the API would stop answering.
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _connectionSlots.WaitAsync(cancellationToken);
                    try
                    {
                        await ServeAsync(client, cancellationToken);
                    }
                    finally
                    {
                        _connectionSlots.Release();
                    }
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
                {
                    // The client went away mid-request; nothing to report.
                }
                finally
                {
                    try { client.Dispose(); } catch (Exception) { }
                }
            }, CancellationToken.None);
        }
    }

    private async Task ServeAsync(Socket client, CancellationToken cancellationToken)
    {
        using var network = new NetworkStream(client, ownsSocket: false);
        var reader = new SocketRequestReader(network, Port);
        var parsed = await reader.ReadRequestAsync(cancellationToken);
        if (parsed == null)
        {
            await WriteBareErrorAsync(network, 400, "Bad Request", cancellationToken);
            return;
        }

        var response = new SocketResponseData(network, parsed.HttpVersion);
        var context = new SocketRequestContext(parsed, response);
        try
        {
            await _handler(context, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            response.Abort();
            return;
        }
        finally
        {
            if (!response.Completed)
            {
                // A handler that threw before writing still owes the client a status line.
                try
                {
                    response.StatusCode = 500;
                    await response.CompleteAsync();
                }
                catch (Exception) { }
            }
        }
    }

    private static async Task WriteBareErrorAsync(Stream stream, int status, string reason, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(reason);
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/plain; charset=utf-8\r\n" +
                   $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public void Stop()
    {
        try { _stop.Cancel(); } catch (Exception) { }
        try { _listener.Stop(); } catch (Exception) { }
        try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
    }

    public void Dispose()
    {
        Stop();
        _connectionSlots.Dispose();
        _stop.Dispose();
    }

    /// <summary>One parsed request line and its headers, with a fully buffered bounded body.</summary>
    internal sealed class ParsedRequest
    {
        public required string Method { get; init; }
        public required string Target { get; init; }
        public required string HttpVersion { get; init; }
        public required Dictionary<string, string> Headers { get; init; }
        public required byte[] Body { get; init; }
        public required int Port { get; init; }

        public bool HasBody => Body.Length > 0 || Headers.ContainsKey("transfer-encoding");
        public Uri Url => new($"http://127.0.0.1:{Port}{Target}");
    }

    internal static class RequestParser
    {
        /// <summary>
        /// Splits a raw request into its parts. Null means "not a request this server can serve";
        /// callers answer 400 rather than guessing.
        /// </summary>
        public static ParsedRequest? Parse(byte[] head, byte[] body, int port)
        {
            var text = Encoding.Latin1.GetString(head);
            var lines = text.Split("\r\n", StringSplitOptions.None);
            if (lines.Length == 0)
            {
                return null;
            }

            var parts = lines[0].Split(' ');
            if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 ||
                !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            {
                return null;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length == 0)
                {
                    continue;
                }

                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    return null;
                }

                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            var target = parts[1];
            if (!target.StartsWith('/'))
            {
                return null; // absolute-form or authority-form: not produced by local clients
            }

            return new ParsedRequest
            {
                Method = parts[0].ToUpperInvariant(),
                Target = target,
                HttpVersion = parts[2],
                Headers = headers,
                Body = body,
                Port = port,
            };
        }

        /// <summary>Chunked transfer coding, decoded with the same hard cap as a declared length.</summary>
        public static byte[]? TryDecodeChunked(byte[] raw)
        {
            using var output = new MemoryStream();
            var index = 0;
            while (true)
            {
                var lineEnd = IndexOfCrLf(raw, index);
                if (lineEnd < 0)
                {
                    return null;
                }

                var sizeText = Encoding.ASCII.GetString(raw, index, lineEnd - index).Split(';')[0].Trim();
                if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, null, out var size) || size < 0)
                {
                    return null;
                }

                index = lineEnd + 2;
                if (size == 0)
                {
                    return output.ToArray();
                }

                if (index + size + 2 > raw.Length || output.Length + size > MaxRequestBodyBytes)
                {
                    return null;
                }

                output.Write(raw, index, size);
                index += size + 2;
            }
        }

        private static int IndexOfCrLf(byte[] buffer, int start)
        {
            for (var i = start; i + 1 < buffer.Length; i++)
            {
                if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n')
                {
                    return i;
                }
            }

            return -1;
        }

        internal static string? GetQueryValue(string target, string name)
        {
            var question = target.IndexOf('?');
            if (question < 0)
            {
                return null;
            }

            foreach (var pair in target[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                var key = equals < 0 ? pair : pair[..equals];
                if (!string.Equals(Uri.UnescapeDataString(key), name, StringComparison.Ordinal))
                {
                    continue;
                }

                return equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..]);
            }

            return null;
        }
    }

    /// <summary>Reads one request off the socket: head, then a bounded body.</summary>
    private sealed class SocketRequestReader(Stream stream, int port)
    {
        private readonly byte[] _buffer = new byte[ReadBufferBytes];
        private int _start;
        private int _end;

        public async Task<ParsedRequest?> ReadRequestAsync(CancellationToken cancellationToken)
        {
            var head = new MemoryStream();
            var headerEnd = -1;
            while (headerEnd < 0)
            {
                if (!await FillAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                var available = _end - _start;
                headerEnd = IndexOfHeaderEnd(_buffer, _start, available);
                if (headerEnd >= 0)
                {
                    head.Write(_buffer, _start, headerEnd + 4 - _start);
                    _start = headerEnd + 4;
                }
                else
                {
                    head.Write(_buffer, _start, available);
                    _start = _end;
                }

                if (head.Length > MaxHeaderBytes)
                {
                    return null;
                }
            }

            var headBytes = head.ToArray();
            var headerText = Encoding.Latin1.GetString(headBytes);
            var lines = headerText.Split("\r\n", StringSplitOptions.None);
            var contentLength = 0;
            var chunked = false;
            foreach (var line in lines)
            {
                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(value, out contentLength) || contentLength < 0)
                    {
                        return null;
                    }
                }
                else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                         value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                {
                    chunked = true;
                }
            }

            if (contentLength > MaxRequestBodyBytes)
            {
                return null;
            }

            byte[] body;
            if (chunked)
            {
                body = await ReadChunkedBodyAsync(cancellationToken).ConfigureAwait(false) ?? Array.Empty<byte>();
            }
            else if (contentLength > 0)
            {
                body = await ReadExactlyAsync(contentLength, cancellationToken).ConfigureAwait(false) ?? Array.Empty<byte>();
            }
            else
            {
                body = Array.Empty<byte>();
            }

            var parsed = RequestParser.Parse(headBytes, body, port);
            return parsed == null ? null : new ParsedRequest
            {
                Method = parsed.Method, Target = parsed.Target, HttpVersion = parsed.HttpVersion,
                Headers = parsed.Headers, Body = body, Port = port,
            };
        }

        private async Task<bool> FillAsync(CancellationToken cancellationToken)
        {
            if (_start < _end)
            {
                return true;
            }

            _start = 0;
            _end = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
            return _end > 0;
        }

        private async Task<byte[]?> ReadExactlyAsync(int count, CancellationToken cancellationToken)
        {
            var body = new byte[count];
            var written = 0;
            if (_end > _start)
            {
                var take = Math.Min(count, _end - _start);
                Array.Copy(_buffer, _start, body, 0, take);
                written = take;
                _start += take;
            }

            while (written < count)
            {
                var read = await stream.ReadAsync(body.AsMemory(written, count - written), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    return null;
                }

                written += read;
            }

            return body;
        }

        /// <summary>
        /// Decodes chunked framing as it arrives. Reading to end-of-stream instead would wait for a
        /// connection close the client is entitled not to send, which is what a hanging request is.
        /// </summary>
        private async Task<byte[]?> ReadChunkedBodyAsync(CancellationToken cancellationToken)
        {
            using var all = new MemoryStream();
            while (true)
            {
                var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                {
                    return null;
                }

                var sizeText = line.Split(';')[0].Trim();
                if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, null, out var size) || size < 0)
                {
                    return null;
                }

                if (size == 0)
                {
                    // Consume the optional trailer section's terminating blank line.
                    while (true)
                    {
                        var trailer = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
                        if (trailer == null || trailer.Length == 0)
                        {
                            break;
                        }
                    }

                    return all.ToArray();
                }

                if (all.Length + size > MaxRequestBodyBytes)
                {
                    return null;
                }

                var chunk = await ReadExactlyAsync(size, cancellationToken).ConfigureAwait(false);
                if (chunk == null)
                {
                    return null;
                }

                all.Write(chunk, 0, chunk.Length);
                _ = await ReadLineAsync(cancellationToken).ConfigureAwait(false); // CRLF after the chunk
            }
        }

        /// <summary>One CRLF-terminated line, bounded by the header cap.</summary>
        private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            using var line = new MemoryStream();
            while (true)
            {
                if (_start >= _end && !await FillAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                var index = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
                if (index < 0)
                {
                    line.Write(_buffer, _start, _end - _start);
                    _start = _end;
                    if (line.Length > MaxHeaderBytes)
                    {
                        return null;
                    }

                    continue;
                }

                line.Write(_buffer, _start, index - _start);
                _start = index + 1;
                var text = Encoding.ASCII.GetString(line.ToArray());
                return text.TrimEnd('\r');
            }
        }

        private static int IndexOfHeaderEnd(byte[] buffer, int start, int count)
        {
            for (var i = start; i + 3 < start + count; i++)
            {
                if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n' &&
                    buffer[i + 2] == (byte)'\r' && buffer[i + 3] == (byte)'\n')
                {
                    return i;
                }
            }

            return -1;
        }
    }

    private sealed class SocketRequestContext(ParsedRequest request, SocketResponseData response) : HttpRequestContext
    {
        public override HttpRequestData Request { get; } = new SocketRequest(request);
        public override HttpResponseData Response { get; } = response;
        public override CancellationToken RequestAborted => CancellationToken.None;

        private sealed class SocketRequest(ParsedRequest parsed) : HttpRequestData
        {
            public override string HttpMethod => parsed.Method;
            public override Uri? Url => parsed.Url;
            public override string? GetHeader(string name) => parsed.Headers.TryGetValue(name, out var value) ? value : null;
            public override string? GetQueryValue(string name) => RequestParser.GetQueryValue(parsed.Target, name);
            public override bool IsLocal => true;
            public override bool HasEntityBody => parsed.HasBody;
            public override long ContentLength64 => parsed.Headers.ContainsKey("transfer-encoding") ? -1 : parsed.Body.Length;
            public override Encoding? ContentEncoding => null;
            public override Stream InputStream { get; } = new MemoryStream(parsed.Body, writable: false);
        }
    }

    /// <summary>Writes the response header block on first output, then body bytes as they arrive.</summary>
    private sealed class SocketResponseData(Stream stream, string requestVersion) : HttpResponseData
    {
        private readonly object _gate = new();
        private int _status = 200;
        private string? _contentType;
        private Encoding? _encoding;
        private long _contentLength = -1;
        private bool _streaming;
        private bool _headWritten;
        private bool _completed;
        private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

        public bool Completed => _completed;

        public override int StatusCode { set => _status = value; }
        public override string? ContentType { set => _contentType = value; }
        public override Encoding? ContentEncoding { set => _encoding = value; }
        public override long ContentLength64 { set => _contentLength = value; }
        public override Stream OutputStream => new WriteThroughStream(this);
        public override void SetHeader(string name, string value) => _headers[name] = value;
        public override void EnableStreaming() => _streaming = true;

        public override async Task CompleteAsync()
        {
            await WriteHeadIfNeededAsync().ConfigureAwait(false);
            if (_streaming)
            {
                await stream.WriteAsync(LastChunk).ConfigureAwait(false);
            }

            await stream.FlushAsync().ConfigureAwait(false);
            _completed = true;
            stream.Dispose();
        }

        public override void Abort()
        {
            _completed = true;
            try { stream.Dispose(); } catch (Exception) { }
        }

        internal async Task WriteAsync(ReadOnlyMemory<byte> buffer)
        {
            await WriteHeadIfNeededAsync().ConfigureAwait(false);
            if (_streaming)
            {
                var header = Encoding.ASCII.GetBytes($"{buffer.Length:x}\r\n");
                await stream.WriteAsync(header).ConfigureAwait(false);
                await stream.WriteAsync(buffer).ConfigureAwait(false);
                await stream.WriteAsync(CrLf).ConfigureAwait(false);
                return;
            }

            await stream.WriteAsync(buffer).ConfigureAwait(false);
        }

        internal Task FlushAsync() => stream.FlushAsync();

        private async Task WriteHeadIfNeededAsync()
        {
            lock (_gate)
            {
                if (_headWritten)
                {
                    return;
                }

                _headWritten = true;
            }

            var builder = new StringBuilder();
            builder.Append(requestVersion).Append(' ').Append(_status).Append(' ').Append(Reason(_status)).Append("\r\n");
            if (!string.IsNullOrEmpty(_contentType))
            {
                builder.Append("Content-Type: ").Append(_contentType).Append("\r\n");
            }

            foreach (var header in _headers)
            {
                builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            }

            if (_streaming)
            {
                builder.Append("Transfer-Encoding: chunked\r\n");
            }
            else if (_contentLength >= 0)
            {
                builder.Append("Content-Length: ").Append(_contentLength).Append("\r\n");
            }

            builder.Append("Connection: close\r\n\r\n");
            var bytes = Encoding.ASCII.GetBytes(builder.ToString());
            await stream.WriteAsync(bytes).ConfigureAwait(false);
        }

        private static string Reason(int status) => status switch
        {
            200 => "OK", 201 => "Created", 204 => "No Content", 304 => "Not Modified",
            400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found",
            405 => "Method Not Allowed", 409 => "Conflict", 413 => "Payload Too Large",
            415 => "Unsupported Media Type", 422 => "Unprocessable Entity",
            500 => "Internal Server Error", 501 => "Not Implemented", 503 => "Service Unavailable",
            _ => "Status",
        };
    }

    /// <summary>Adapts <see cref="HttpResponseData.OutputStream"/> to the chunked/content-length writer.</summary>
    private sealed class WriteThroughStream(SocketResponseData owner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => owner.FlushAsync().GetAwaiter().GetResult();
        public override Task FlushAsync(CancellationToken cancellationToken) => owner.FlushAsync();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            owner.WriteAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(owner.WriteAsync(buffer));
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            await owner.WriteAsync(buffer.AsMemory(offset, count)).ConfigureAwait(false);
    }
}
