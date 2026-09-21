using System.Text.Json;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

internal static class DecisionContextTests
{
    public static void ClientContext_ReasonIsOptionalAndTrimmed()
    {
        Assert.Null(DecisionContext.ReadReason(null));
        Assert.Null(DecisionContext.ReadReason("not an object"));
        Assert.Null(DecisionContext.ReadReason(Json("""{"source":"mcp"}""")));
        Assert.Null(DecisionContext.ReadReason(Json("""{"decision_reason":null}""")));
        Assert.Null(DecisionContext.ReadReason(Json("""{"decision_reason":"   "}""")));
        Assert.Null(DecisionContext.ReadReason(Json("""{"decision_reason":42}""")));
        Assert.Equal(
            "Clear the weak slime first.",
            DecisionContext.ReadReason(Json("""{"source":"mcp","decision_reason":"  Clear the weak slime first.  "}""")));
    }

    private static JsonElement Json(string text)
    {
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
