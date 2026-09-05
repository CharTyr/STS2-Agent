using System.Net;
using System.Text;
using System.Text.Json;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

internal static class TeamConversationTests
{
    public static void HistoryIsBoundedAndCleared()
    {
        var conversation = new TeamConversation();
        Assert.Null(conversation.BuildDecisionContext());
        for (var i = 0; i < 20; i++) conversation.Add("user", "建议" + i);
        Assert.Equal(12, conversation.Snapshot().Count);
        Assert.Equal("建议8", conversation.Snapshot()[0].Text);
        using var context = JsonDocument.Parse(conversation.BuildDecisionContext()!);
        Assert.Equal("human_teammate", context.RootElement[0].GetProperty("speaker").GetString());
        Assert.Equal("建议19", context.RootElement[11].GetProperty("message").GetString());
        conversation.Clear();
        Assert.Null(conversation.BuildDecisionContext());
    }

    public static void SessionTokensAreRequiredAndDistinct()
    {
        var token = CompanionConnection.CreateToken();
        Assert.True(CompanionConnection.IsAuthorized(token, token));
        Assert.False(CompanionConnection.IsAuthorized(token, CompanionConnection.CreateToken()));
        Assert.False(CompanionConnection.IsAuthorized(null, null));
        Assert.False(CompanionConnection.IsAuthorized("", ""));
        Assert.False(CompanionConnection.IsAuthorized(token, "short"));
    }

    public static async Task TransportChecksIdentityAndSendsBoundedBody()
    {
        var token = CompanionConnection.CreateToken();
        using var handler = new Handler(token, replacement: false);
        using var http = new HttpClient(handler);
        var connection = new CompanionConnection(8081, 123, token, http);
        var reply = await connection.SendMessageAsync("我们先集火", CancellationToken.None);
        Assert.Equal("我先处理左侧敌人。", reply);
        Assert.Equal(1, handler.Posts);
    }

    public static async Task ReusedPortDoesNotReceiveMessage()
    {
        var token = CompanionConnection.CreateToken();
        using var handler = new Handler(token, replacement: true);
        using var http = new HttpClient(handler);
        var connection = new CompanionConnection(8081, 123, token, http);
        var rejected = false;
        try { await connection.SendMessageAsync("我们先集火", CancellationToken.None); }
        catch (InvalidOperationException) { rejected = true; }
        Assert.True(rejected);
        Assert.Equal(0, handler.Posts);
    }

    public static async Task PauseControlHasExplicitAcknowledgement()
    {
        var token = CompanionConnection.CreateToken();
        using var handler = new Handler(token, replacement: false);
        using var http = new HttpClient(handler);
        var connection = new CompanionConnection(8081, 123, token, http);
        Assert.Equal("paused", await connection.ControlAsync(false, CancellationToken.None));
        Assert.Equal(1, handler.Posts);
    }

    private sealed class Handler(string token, bool replacement) : HttpMessageHandler
    {
        public int Posts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object payload;
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/health", request.RequestUri!.AbsolutePath);
                payload = new { ok = true, data = new { service = "sts2-ai-agent", status = "ready", instance_role = "companion", api_port = 8081, process_id = replacement ? 456 : 123 } };
            }
            else
            {
                Posts++;
                Assert.Equal(token, request.Headers.GetValues(CompanionConnection.TokenHeader).Single());
                Assert.True(request.Content!.Headers.ContentLength is > 0 and < 16000);
                using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                if (request.RequestUri!.AbsolutePath == "/companion/control")
                {
                    Assert.False(body.RootElement.GetProperty("running").GetBoolean());
                    payload = new { ok = true, data = new { phase = "paused" } };
                }
                else
                {
                    Assert.Equal("/companion/message", request.RequestUri.AbsolutePath);
                    Assert.Equal("我们先集火", body.RootElement.GetProperty("message").GetString());
                    payload = new { ok = true, data = new { reply = "我先处理左侧敌人。" } };
                }
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
        }
    }
}
