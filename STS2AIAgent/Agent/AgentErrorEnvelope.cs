using System.Text.Json;
using STS2AIAgent.Server;

namespace STS2AIAgent.Agent;

/// <summary>
/// Maps an in-process game action failure onto the same error envelope the HTTP boundary
/// produces. The HTTP path (<c>Router.WriteErrorAsync</c>) serializes <c>code</c>, <c>message</c>,
/// <c>details</c>, and <c>retryable</c>; the in-game agent path used to collapse the same failure
/// into a bare <c>ex.Message</c>. Both producers now share this one field-name source, so the
/// duplicate <see cref="ApiException"/> metadata stays observable from either transport.
/// </summary>
/// <remarks>
/// BCL only: no Godot, no MegaCrit, no reflection. <c>status_code</c> is additive to the HTTP
/// envelope because the model cannot see an HTTP status line in-process; it stays <c>null</c> when
/// the failure is not a deliberate <see cref="ApiException"/>.
/// </remarks>
internal static class AgentErrorEnvelope
{
    /// <summary>Classification for every failure that is not a deliberate <see cref="ApiException"/>.</summary>
    public const string InternalErrorCode = "internal_error";

    public static bool IsRetryable(Exception exception) =>
        exception is ApiException api && api.Retryable;

    public static string CodeFor(Exception exception) =>
        exception is ApiException api ? api.Code : InternalErrorCode;

    public static int? StatusCodeFor(Exception exception) =>
        exception is ApiException api ? api.StatusCode : null;

    public static object? DetailsFor(Exception exception) =>
        exception is ApiException api ? api.Details : null;

    /// <summary>
    /// The <c>error</c> object itself. Field names intentionally mirror <c>Router.WriteErrorAsync</c>
    /// so a reader cannot tell the two transports apart; <c>status_code</c> is the only addition.
    /// </summary>
    public static object ToPayload(Exception exception) => new
    {
        code = CodeFor(exception),
        message = exception.Message,
        details = DetailsFor(exception),
        retryable = IsRetryable(exception),
        status_code = StatusCodeFor(exception)
    };

    /// <summary>
    /// Serializes <c>{"error": payload}</c>. GameBridge and AgentLoop both call this instead of
    /// spelling the field names a second time.
    /// </summary>
    public static string Serialize(Exception exception, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(new { error = ToPayload(exception) }, options);

    /// <summary>
    /// Reads a serialized error envelope back out. A top-level <c>error</c> key of either shape
    /// (object from <see cref="Serialize"/>, or the legacy bare string) counts as a failure, so a
    /// caller can never mistake a formatted error for a successful payload.
    /// </summary>
    public static bool TryReadError(string? json, out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("error", out var error))
            {
                return false;
            }

            message = error.ValueKind switch
            {
                JsonValueKind.String => error.GetString(),
                JsonValueKind.Object when error.TryGetProperty("message", out var text) &&
                                          text.ValueKind == JsonValueKind.String => text.GetString(),
                _ => null
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
