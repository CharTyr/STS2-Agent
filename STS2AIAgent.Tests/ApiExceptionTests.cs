using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

/// <summary>
/// Behavioural coverage for the HTTP error envelope. <c>ApiException.cs</c> depends on the BCL only,
/// so it compiles straight into this test assembly and its <c>internal</c> constructor is reachable.
/// </summary>
internal static class ApiExceptionTests
{
    public static void CarriesStatusAndCode()
    {
        var details = new { option_index = 2 };
        var error = new ApiException(409, "invalid_action", "Capstone option is not selectable.", details);

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("invalid_action", error.Code);
        Assert.Equal("Capstone option is not selectable.", error.Message);
        Assert.Equal(details, error.Details);
        Assert.True(error is Exception, "ApiException must remain an Exception so Router can catch it.");
    }

    public static void RetryableDefaultsToFalse()
    {
        var implicitRetry = new ApiException(400, "invalid_request", "Missing option_index.");
        Assert.False(implicitRetry.Retryable, "An omitted retryable flag must not advertise a retry.");

        var explicitRetry = new ApiException(409, "companion_not_ready", "Companion is starting.", retryable: true);
        Assert.True(explicitRetry.Retryable, "retryable: true must be preserved on the envelope.");
    }

    public static void DetailsAreOptional()
    {
        var withoutDetails = new ApiException(500, "export_error", "Export failed.");
        Assert.Null(withoutDetails.Details);
        Assert.False(withoutDetails.Retryable);

        // Details must survive as an arbitrary payload rather than being coerced to a string.
        var details = new[] { 1, 2, 3 };
        var withDetails = new ApiException(400, "invalid_request", "Bad index.", details);
        Assert.True(ReferenceEquals(details, withDetails.Details), "Details must be stored by reference.");
    }
}
