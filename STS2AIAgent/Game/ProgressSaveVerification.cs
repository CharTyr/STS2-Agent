using System.Text;
using System.Text.Json;

namespace STS2AIAgent.Game;

internal sealed class ProgressSaveVerificationResult
{
    private ProgressSaveVerificationResult(string status, bool verified, string? error)
    {
        Status = status;
        Verified = verified;
        Error = error;
    }

    public string Status { get; }

    public bool Verified { get; }

    public string? Error { get; }

    public static ProgressSaveVerificationResult Pending()
    {
        return new ProgressSaveVerificationResult("pending", verified: false, error: null);
    }

    public static ProgressSaveVerificationResult Success()
    {
        return new ProgressSaveVerificationResult("verified", verified: true, error: null);
    }

    public static ProgressSaveVerificationResult Failure(string error)
    {
        return new ProgressSaveVerificationResult("error", verified: false, error);
    }
}

/// <summary>
/// Verifies that the progress snapshot currently held by the game is present in the primary
/// on-disk progress save. This deliberately performs no save operation: it observes the result of
/// the native save flow so a swallowed SaveProgress exception cannot be reported as success.
/// </summary>
internal static class ProgressSaveVerification
{
    public static ProgressSaveVerificationResult Verify(string expectedJson, string persistedPath)
    {
        if (string.IsNullOrWhiteSpace(expectedJson))
        {
            return ProgressSaveVerificationResult.Failure("progress_snapshot_empty");
        }

        if (string.IsNullOrWhiteSpace(persistedPath))
        {
            return ProgressSaveVerificationResult.Failure("progress_save_path_unavailable");
        }

        string persistedJson;
        try
        {
            using var stream = new FileStream(
                persistedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length == 0)
            {
                return ProgressSaveVerificationResult.Failure("progress_save_empty");
            }

            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            persistedJson = reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            return ProgressSaveVerificationResult.Failure("progress_save_missing");
        }
        catch (DirectoryNotFoundException)
        {
            return ProgressSaveVerificationResult.Failure("progress_save_missing");
        }
        catch (Exception exception)
        {
            return ProgressSaveVerificationResult.Failure(
                $"progress_save_read_failed:{exception.GetType().Name}");
        }

        return VerifyJson(expectedJson, persistedJson);
    }

    internal static ProgressSaveVerificationResult VerifyJson(
        string expectedJson,
        string persistedJson)
    {
        if (string.IsNullOrWhiteSpace(expectedJson))
        {
            return ProgressSaveVerificationResult.Failure("progress_snapshot_empty");
        }

        if (string.IsNullOrEmpty(persistedJson))
        {
            return ProgressSaveVerificationResult.Failure("progress_save_empty");
        }

        JsonDocument expectedDocument;
        try
        {
            expectedDocument = JsonDocument.Parse(expectedJson);
        }
        catch (JsonException)
        {
            return ProgressSaveVerificationResult.Failure("progress_snapshot_invalid_json");
        }

        using (expectedDocument)
        {
            JsonDocument persistedDocument;
            try
            {
                persistedDocument = JsonDocument.Parse(persistedJson);
            }
            catch (JsonException)
            {
                return ProgressSaveVerificationResult.Failure("progress_save_invalid_json");
            }

            using (persistedDocument)
            {
                return JsonEquivalent(expectedDocument.RootElement, persistedDocument.RootElement)
                    ? ProgressSaveVerificationResult.Success()
                    : ProgressSaveVerificationResult.Failure("progress_save_content_mismatch");
            }
        }
    }

    private static bool JsonEquivalent(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return false;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var expectedProperties = expected.EnumerateObject().ToArray();
                var actualProperties = actual.EnumerateObject().ToArray();
                if (expectedProperties.Length != actualProperties.Length)
                {
                    return false;
                }

                foreach (var expectedProperty in expectedProperties)
                {
                    if (!actual.TryGetProperty(expectedProperty.Name, out var actualValue) ||
                        !JsonEquivalent(expectedProperty.Value, actualValue))
                    {
                        return false;
                    }
                }

                return true;
            }
            case JsonValueKind.Array:
            {
                var expectedItems = expected.EnumerateArray().ToArray();
                var actualItems = actual.EnumerateArray().ToArray();
                if (expectedItems.Length != actualItems.Length)
                {
                    return false;
                }

                for (var index = 0; index < expectedItems.Length; index++)
                {
                    if (!JsonEquivalent(expectedItems[index], actualItems[index]))
                    {
                        return false;
                    }
                }

                return true;
            }
            case JsonValueKind.String:
                return string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Number:
                return string.Equals(expected.GetRawText(), actual.GetRawText(), StringComparison.Ordinal);
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return true;
            default:
                return false;
        }
    }
}
