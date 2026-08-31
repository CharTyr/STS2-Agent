using STS2AIAgent.Game;

namespace STS2AIAgent.Tests;

internal static class ProgressSaveVerificationTests
{
    public static void MatchingPhysicalFileIsVerified()
    {
        WithTemporaryFile(
            """
            {"current_score":17,"epochs":[{"id":"EPOCH.ONE","state":3}]}
            """,
            path =>
            {
                var result = ProgressSaveVerification.Verify(
                    """
                    {"current_score":17,"epochs":[{"id":"EPOCH.ONE","state":3}]}
                    """,
                    path);

                Assert.Equal("verified", result.Status);
                Assert.True(result.Verified);
                Assert.Null(result.Error);
            });
    }

    public static void EquivalentJsonWithDifferentFormattingAndPropertyOrderIsVerified()
    {
        WithTemporaryFile(
            """
            {
              "epochs": [{"state": 3, "id": "EPOCH.ONE"}],
              "current_score": 17
            }
            """,
            path =>
            {
                var result = ProgressSaveVerification.Verify(
                    """
                    {"current_score":17,"epochs":[{"id":"EPOCH.ONE","state":3}]}
                    """,
                    path);

                Assert.Equal("verified", result.Status);
                Assert.True(result.Verified);
                Assert.Null(result.Error);
            });
    }

    public static void MismatchedScoreOrUnlockStateCannotReportSuccess()
    {
        WithTemporaryFile(
            """
            {"current_score":16,"epochs":[{"id":"EPOCH.ONE","state":2}]}
            """,
            path =>
            {
                var result = ProgressSaveVerification.Verify(
                    """
                    {"current_score":17,"epochs":[{"id":"EPOCH.ONE","state":3}]}
                    """,
                    path);

                Assert.Equal("error", result.Status);
                Assert.False(result.Verified);
                Assert.Equal("progress_save_content_mismatch", result.Error);
            });
    }

    public static void MissingOrMalformedFileCannotReportSuccess()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            "sts2-agent-progress-save-tests",
            Guid.NewGuid().ToString("N"),
            "progress.save");
        var missing = ProgressSaveVerification.Verify("{}", missingPath);
        Assert.Equal("error", missing.Status);
        Assert.False(missing.Verified);
        Assert.Equal("progress_save_missing", missing.Error);

        WithTemporaryFile(
            "not-json",
            path =>
            {
                var malformed = ProgressSaveVerification.Verify("{}", path);
                Assert.Equal("error", malformed.Status);
                Assert.False(malformed.Verified);
                Assert.Equal("progress_save_invalid_json", malformed.Error);
            });
    }

    public static void ReadFailureCannotReportSuccess()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "sts2-agent-progress-save-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var result = ProgressSaveVerification.Verify("{}", directory);
            Assert.Equal("error", result.Status);
            Assert.False(result.Verified);
            Assert.Contains("progress_save_read_failed:", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: false);
        }
    }

    private static void WithTemporaryFile(string content, Action<string> assertion)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "sts2-agent-progress-save-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "progress.save");
        try
        {
            File.WriteAllText(path, content);
            assertion(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
