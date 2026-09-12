using System.Text.Json;
using System.Text.RegularExpressions;
using STS2AIAgent.Agent;
using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

/// <summary>
/// The in-process agent used to report a failed action as a bare message while the HTTP path sent
/// code/details/retryable. These tests pin the mapping to a truth table and pin the wire shape to
/// the one <c>Router.WriteErrorAsync</c> produces, so the two transports cannot drift apart again.
/// </summary>
internal static class AgentErrorEnvelopeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static void ApiExceptionKeepsItsHttpMetadata()
    {
        var details = new { option_index = 2 };
        var exception = new ApiException(409, "invalid_action", "Capstone option is not selectable.", details, retryable: true);

        Assert.Equal("invalid_action", AgentErrorEnvelope.CodeFor(exception));
        Assert.Equal(409, AgentErrorEnvelope.StatusCodeFor(exception));
        Assert.True(ReferenceEquals(details, AgentErrorEnvelope.DetailsFor(exception)), "Details must stay the original payload.");
        Assert.True(AgentErrorEnvelope.IsRetryable(exception));

        using var document = JsonDocument.Parse(AgentErrorEnvelope.Serialize(exception, JsonOptions));
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("invalid_action", error.GetProperty("code").GetString());
        Assert.Equal("Capstone option is not selectable.", error.GetProperty("message").GetString());
        Assert.Equal(409, error.GetProperty("status_code").GetInt32());
        Assert.True(error.GetProperty("retryable").GetBoolean());
        Assert.Equal(2, error.GetProperty("details").GetProperty("option_index").GetInt32());
    }

    public static void ApiExceptionDefaultsArePreserved()
    {
        var exception = new ApiException(400, "invalid_request", "Missing option_index.");

        Assert.Equal(400, AgentErrorEnvelope.StatusCodeFor(exception));
        Assert.False(AgentErrorEnvelope.IsRetryable(exception));
        Assert.Null(AgentErrorEnvelope.DetailsFor(exception));

        using var document = JsonDocument.Parse(AgentErrorEnvelope.Serialize(exception, JsonOptions));
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(JsonValueKind.Null, error.GetProperty("details").ValueKind);
        Assert.False(error.GetProperty("retryable").GetBoolean());
    }

    public static void UnexpectedFailureIsClassifiedNotDropped()
    {
        var exception = new InvalidOperationException("boom");

        Assert.Equal(AgentErrorEnvelope.InternalErrorCode, AgentErrorEnvelope.CodeFor(exception));
        Assert.Null(AgentErrorEnvelope.StatusCodeFor(exception));
        Assert.Null(AgentErrorEnvelope.DetailsFor(exception));
        Assert.False(AgentErrorEnvelope.IsRetryable(exception));

        using var document = JsonDocument.Parse(AgentErrorEnvelope.Serialize(exception, JsonOptions));
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("internal_error", error.GetProperty("code").GetString());
        Assert.Equal("boom", error.GetProperty("message").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, error.GetProperty("status_code").ValueKind);
    }

    public static void CancellationIsClassifiedWithoutRetry()
    {
        // A caller cancellation is never a retryable game failure; it must not be advertised as one.
        var exception = new OperationCanceledException("cancelled by the caller");

        Assert.Equal(AgentErrorEnvelope.InternalErrorCode, AgentErrorEnvelope.CodeFor(exception));
        Assert.False(AgentErrorEnvelope.IsRetryable(exception));
        Assert.Null(AgentErrorEnvelope.StatusCodeFor(exception));
        Assert.Null(AgentErrorEnvelope.DetailsFor(exception));
    }

    public static void EnvelopeReadsBackAsTheFailureText()
    {
        var exception = new ApiException(503, "state_unavailable", "Screen is still settling.", retryable: true);
        Assert.True(AgentErrorEnvelope.TryReadError(AgentErrorEnvelope.Serialize(exception, JsonOptions), out var message));
        Assert.Equal("Screen is still settling.", message);

        // The legacy bare-string shape stays readable, so an older producer degrades instead of
        // being mistaken for a successful payload.
        Assert.True(AgentErrorEnvelope.TryReadError("""{"error":"action is required"}""", out var legacy));
        Assert.Equal("action is required", legacy);

        Assert.False(AgentErrorEnvelope.TryReadError("""{"status":"completed","stable":true}""", out _), "A success payload is not an error.");
        Assert.False(AgentErrorEnvelope.TryReadError("not json", out _));
        Assert.False(AgentErrorEnvelope.TryReadError(null, out _));
    }

    public static void EnvelopeFieldNamesMatchTheHttpRouter()
    {
        var router = AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs");
        var body = AgentSourceFixture.DeclarationBody(router, "public static Task WriteErrorAsync(");
        var routerFields = AnonymousObjectFields(body, "error = new");

        var apiFields = ErrorFieldNames(AgentErrorEnvelope.Serialize(
            new ApiException(409, "invalid_action", "m", retryable: true), JsonOptions));
        var fallbackFields = ErrorFieldNames(AgentErrorEnvelope.Serialize(new InvalidOperationException("m"), JsonOptions));

        Assert.True(apiFields.SetEquals(fallbackFields), "Both branches must emit exactly one error shape.");
        Assert.True(
            apiFields.SetEquals(routerFields.Union(new[] { "status_code" }, StringComparer.Ordinal)),
            "The in-process envelope must be the HTTP envelope plus status_code, not a second vocabulary. " +
            $"router=[{string.Join(",", routerFields.OrderBy(static name => name, StringComparer.Ordinal))}] " +
            $"api=[{string.Join(",", apiFields.OrderBy(static name => name, StringComparer.Ordinal))}]");
        Assert.True(
            routerFields.SetEquals(new[] { "code", "message", "details", "retryable" }),
            "Router.WriteErrorAsync field names changed; update the shared envelope to match.");
    }

    public static void GameBridgeActFailureCarriesTheEnvelope()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Agent/GameBridge.cs");
        var act = AgentSourceFixture.DeclarationBody(source, "public Task<string> ActAsync(");

        Assert.True(act.Contains("AgentErrorEnvelope.Serialize", StringComparison.Ordinal),
            "GameBridge.ActAsync must format a failed action through the shared envelope.");
        Assert.True(act.Contains("catch (ApiException", StringComparison.Ordinal),
            "GameBridge.ActAsync must surface the deliberate ApiException failure.");
        Assert.False(act.Contains("status_code", StringComparison.Ordinal),
            "GameBridge must not spell the envelope field names a second time.");
        Assert.True(act.Contains("response.stable", StringComparison.Ordinal),
            "The GameBridge.ActAsync success payload must stay unchanged.");
    }

    public static void AgentLoopFailureCarriesTheEnvelope()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Agent/AgentLoop.cs");
        var act = AgentSourceFixture.DeclarationBody(source, "Error)> ExecuteActAsync(");

        Assert.True(act.Contains("AgentErrorEnvelope.TryReadError", StringComparison.Ordinal),
            "AgentLoop must not read a bridge error envelope as a successful act.");
        Assert.True(act.Contains("AgentErrorEnvelope.Serialize", StringComparison.Ordinal),
            "AgentLoop's own catch must serialize the shared envelope.");
        Assert.False(act.Contains("new { error = ex.Message }", StringComparison.Ordinal),
            "The catch must no longer collapse the failure to ex.Message.");

        var read = AgentSourceFixture.DeclarationBody(source, "private async Task<string> ExecuteReadToolAsync(");
        Assert.True(read.Contains("AgentErrorEnvelope.Serialize", StringComparison.Ordinal),
            "Read tools must report failures with the same envelope.");
    }

    private static HashSet<string> ErrorFieldNames(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .GetProperty("error")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> AnonymousObjectFields(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("Missing anonymous object: " + marker);
        }

        var open = source.IndexOf('{', start);
        var depth = 0;
        var end = -1;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    end = index;
                    break;
                }
            }
        }

        if (end < 0)
        {
            throw new InvalidOperationException("Unterminated anonymous object: " + marker);
        }

        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in source[(open + 1)..end].Split('\n'))
        {
            var match = Regex.Match(raw.Trim(), @"^([a-z_][a-z0-9_]*)\s*(?:=|,|$)");
            if (match.Success)
            {
                fields.Add(match.Groups[1].Value);
            }
        }

        return fields;
    }
}
