namespace STS2AIAgent.Server;

// The listener binds 127.0.0.1 only, so `HttpListenerRequest.IsLocal` is true for every request,
// including one a web page in the player's browser sends (a cross-site POST, or a DNS-rebound
// hostname that resolves to 127.0.0.1). What tells those apart from a local tool is the headers a
// browser always adds: an `Origin` on cross-site requests and a `Host` naming the attacker's domain
// after a rebind. Local clients (the Python MCP server, curl, scripts) send neither an Origin nor a
// foreign Host, so this refuses exactly the browser cases.
internal static class LocalRequestGuard
{
    /// <summary>
    /// True when the request may reach the REST routes. A present <c>Origin</c> is refused unless
    /// <paramref name="allowOrigin"/> is set (the MCP route validates it against its own endpoint).
    /// A <c>Host</c> must name a loopback host on the port this server listens on.
    /// </summary>
    public static bool IsTrusted(string? host, string? origin, int listeningPort, bool allowOrigin = false)
    {
        if (!allowOrigin && !string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        // HTTP/1.0 clients may omit Host; a browser never does.
        return string.IsNullOrWhiteSpace(host) || IsLoopbackHost(host, listeningPort);
    }

    internal static bool IsLoopbackHost(string host, int listeningPort)
    {
        var value = host.Trim();
        string name;
        string? port = null;
        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close < 0)
            {
                return false;
            }

            name = value[1..close];
            var rest = value[(close + 1)..];
            if (rest.Length > 0)
            {
                if (rest[0] != ':')
                {
                    return false;
                }

                port = rest[1..];
            }
        }
        else
        {
            var colon = value.LastIndexOf(':');
            name = colon < 0 ? value : value[..colon];
            port = colon < 0 ? null : value[(colon + 1)..];
        }

        if (!(name.Equals("127.0.0.1", StringComparison.Ordinal) ||
              name.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
              name.Equals("::1", StringComparison.Ordinal)))
        {
            return false;
        }

        if (port == null)
        {
            return true;
        }

        return int.TryParse(port, System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
               parsed == listeningPort;
    }
}
