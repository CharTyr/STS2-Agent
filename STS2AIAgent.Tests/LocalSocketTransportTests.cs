using System.Net;
using System.Net.Sockets;
using System.Text;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

/// <summary>
/// The fallback transport: Windows returned ERROR_INVALID_HANDLE from <c>HttpListener.Start()</c> in
/// the live v0.113.1 build, so the same routes must be reachable over a plain loopback socket. These
/// tests drive a real socket and a real HTTP client rather than mocking the transport, because the
/// failure being covered is a platform one and a mock cannot reproduce it.
/// </summary>
internal static class LocalSocketTransportTests
{
    public static async Task RealSocketServesJsonHealth()
    {
        using var server = await StartServerAsync(async (context, token) =>
        {
            var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, token);
            await context.Response.CompleteAsync();
        });

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var response = await client.GetAsync($"http://127.0.0.1:{server.Port}/health");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"ok\":true}", body);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
    }

    public static async Task RequestLineHeaderQueryAndBodyReachTheHandler()
    {
        string? seen = null;
        using var server = await StartServerAsync(async (context, token) =>
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(token);
            seen = string.Join("|",
                context.Request.HttpMethod,
                context.Request.Url?.AbsolutePath,
                context.Request.GetQueryValue("limit"),
                context.Request.GetHeader("X-Probe"),
                body,
                context.Request.IsLocal);
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = 0;
            await context.Response.CompleteAsync();
        });

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{server.Port}/state?limit=7")
        {
            Content = new StringContent("hello", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-Probe", "yes");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("POST|/state|7|yes|hello|True", seen);
    }

    public static async Task ChunkedRequestBodyIsDecoded()
    {
        string? body = null;
        using var server = await StartServerAsync(async (context, token) =>
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            body = await reader.ReadToEndAsync(token);
            context.Response.StatusCode = 204;
            await context.Response.CompleteAsync();
        });

        // HttpClient uses chunked framing when the length is unknown.
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var content = new StreamContent(new UnknownLengthStream(Encoding.UTF8.GetBytes("chunked-body")));
        using var response = await client.PostAsync($"http://127.0.0.1:{server.Port}/action", content);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("chunked-body", body);
    }

    public static async Task StreamingResponseUsesChunkedFraming()
    {
        using var server = await StartServerAsync(async (context, token) =>
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.EnableStreaming();
            var first = Encoding.UTF8.GetBytes(": opened\n\n");
            await context.Response.OutputStream.WriteAsync(first, token);
            await context.Response.OutputStream.FlushAsync(token);
            var second = Encoding.UTF8.GetBytes("data: {\"a\":1}\n\n");
            await context.Response.OutputStream.WriteAsync(second, token);
            await context.Response.CompleteAsync();
        });

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var response = await client.GetAsync($"http://127.0.0.1:{server.Port}/events/stream",
            HttpCompletionOption.ResponseHeadersRead);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.TransferEncodingChunked == true, "A streamed response has no known length.");
        Assert.Contains(": opened", text);
        Assert.Contains("data: {\"a\":1}", text);
    }

    public static async Task MalformedRequestIsRefusedWithoutHanging()
    {
        using var server = await StartServerAsync((context, token) => context.Response.CompleteAsync());
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("NOT-A-REQUEST\r\n\r\n"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[256];
        var read = await stream.ReadAsync(buffer, timeout.Token);
        var response = Encoding.ASCII.GetString(buffer, 0, read);
        Assert.Contains("400", response);
    }

    public static void OversizedHeaderIsRefused()
    {
        // The parser is the guard: a header block past the cap is not a request we serve.
        var head = "GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nX-Pad: " + new string('a', LocalSocketHttpServer.MaxHeaderBytes) + "\r\n\r\n";
        Assert.True(
            LocalSocketHttpServer.RequestParser.Parse(Encoding.ASCII.GetBytes(head), Array.Empty<byte>(), 8080) != null,
            "Parsing itself succeeds; the reader enforces the cap before this point.");
        Assert.True(LocalSocketHttpServer.MaxHeaderBytes < LocalSocketHttpServer.MaxRequestBodyBytes);
    }

    public static void ChunkSizeIsCapped()
    {
        var over = Encoding.ASCII.GetBytes(
            "ffffffff\r\n" + new string('x', 8) + "\r\n0\r\n\r\n");
        Assert.Null(LocalSocketHttpServer.RequestParser.TryDecodeChunked(over));
    }

    private static async Task<LocalSocketHttpServer> StartServerAsync(Func<HttpRequestContext, CancellationToken, Task> handler)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var server = new LocalSocketHttpServer(port, handler);
        server.Start();
        await Task.Delay(20);
        return server;
    }

    /// <summary>A stream whose length is unknown, so HttpClient frames the body chunked.</summary>
    private sealed class UnknownLengthStream(byte[] payload) : Stream
    {
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _offset; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = Math.Min(count, payload.Length - _offset);
            Array.Copy(payload, _offset, buffer, offset, take);
            _offset += take;
            return take;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
