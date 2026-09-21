using System.IO;
using System.Text.Json;

namespace STS2AIAgent.Server;

internal static class JsonHelper
{
    // Layout is not free here. Every response this serializer writes is parsed by a client -- the MCP
    // sidecar, an external agent, the validation scripts -- and never read by a human, while the
    // indentation is 35.6% of the bytes across the response examples in docs/api.md, rising to 44% on
    // the nested combat payload. A model pays for those bytes as tokens on every state read, so the
    // wire stays compact and anyone who wants it pretty pipes it through `jq`. Field names stay
    // PascalCase: that part is a contract, and JsonHelperTests pins it.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    public static ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        return JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }
}
