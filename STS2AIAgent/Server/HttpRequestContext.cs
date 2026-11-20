using System.Net;
using System.Text;

namespace STS2AIAgent.Server;

// Public so the isolated fallback transport can implement the contract. Only BCL types cross
// that assembly boundary; Router and NativeMcpServer own all route and protocol decisions.
public abstract class HttpRequestContext
{
    public abstract HttpRequestData Request { get; }
    public abstract HttpResponseData Response { get; }

    // A disconnect may cancel I/O, but not a game action after its request has been read.
    public virtual CancellationToken RequestAborted => CancellationToken.None;

    internal CancellationTokenSource? LinkIoCancellation(CancellationToken shutdownToken) =>
        RequestAborted.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, RequestAborted)
            : null;
}

public abstract class HttpRequestData
{
    public abstract string HttpMethod { get; }
    public abstract Uri? Url { get; }
    public abstract string? GetHeader(string name);
    public abstract string? GetQueryValue(string name);
    public abstract bool IsLocal { get; }
    public abstract bool HasEntityBody { get; }
    // -1 means unknown (e.g. chunked); 0 means a bodyless request or explicit zero length.
    public abstract long ContentLength64 { get; }
    public abstract Encoding? ContentEncoding { get; }
    public abstract Stream InputStream { get; }
}

public abstract class HttpResponseData
{
    public abstract int StatusCode { set; }
    public abstract string? ContentType { set; }
    public abstract Encoding? ContentEncoding { set; }
    public abstract long ContentLength64 { set; }
    public abstract Stream OutputStream { get; }
    public abstract void SetHeader(string name, string value);
    // The transport chooses wire framing for a response without a fixed length.
    public abstract void EnableStreaming();
    public abstract Task CompleteAsync();
    public abstract void Abort();
}

internal sealed class HttpListenerRequestContext(HttpListenerContext context) : HttpRequestContext
{
    public override HttpRequestData Request { get; } = new ListenerRequest(context.Request);
    public override HttpResponseData Response { get; } = new HttpListenerResponseData(context.Response);

    private sealed class ListenerRequest(HttpListenerRequest request) : HttpRequestData
    {
        public override string HttpMethod => request.HttpMethod;
        public override Uri? Url => request.Url;
        public override string? GetHeader(string name) => request.Headers[name];
        public override string? GetQueryValue(string name) => request.QueryString[name];
        public override bool IsLocal => request.IsLocal;
        public override bool HasEntityBody => request.HasEntityBody;
        public override long ContentLength64 => request.ContentLength64;
        public override Encoding? ContentEncoding => request.ContentEncoding;
        public override Stream InputStream => request.InputStream;
    }
}

internal sealed class HttpListenerResponseData(HttpListenerResponse response) : HttpResponseData
{
    public override int StatusCode { set => response.StatusCode = value; }
    public override string? ContentType { set => response.ContentType = value; }
    public override Encoding? ContentEncoding { set => response.ContentEncoding = value; }
    public override long ContentLength64 { set => response.ContentLength64 = value; }
    public override Stream OutputStream => response.OutputStream;
    public override void SetHeader(string name, string value) => response.Headers[name] = value;
    public override void EnableStreaming() => response.SendChunked = true;

    public override Task CompleteAsync()
    {
        response.Close();
        return Task.CompletedTask;
    }

    public override void Abort() => response.Abort();
}
