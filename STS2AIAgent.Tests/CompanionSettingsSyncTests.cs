using System.Net;
using System.Text.Json;
using STS2AIAgent.Config;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Tests;

internal static class CompanionSettingsSyncTests
{
    public static async Task LateAcknowledgementCannotConfirmNewSettings()
    {
        using var handler = new Handler { HoldFirst = true };
        using var http = new HttpClient(handler);
        var connection = new CompanionConnection(8081, 123, new string('A', 64), http);
        var settings = AgentSettings.CreateDefault();
        var synchronizer = new CompanionSettingsSynchronizer();
        var first = synchronizer.SynchronizeAsync(() => new(connection, settings), CancellationToken.None);
        await handler.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        settings.MaxSessionRequests = 17;
        handler.Release.TrySetResult();
        Assert.Equal(CompanionSettingsSyncOutcome.Superseded, await first);
        Assert.Equal(CompanionSettingsSyncOutcome.Confirmed,
            await synchronizer.SynchronizeAsync(() => new(connection, settings), CancellationToken.None));
        Assert.Equal(2, handler.Budgets.Count);
        Assert.Equal(17, handler.Budgets[1]);
    }

    public static async Task LateAcknowledgementCannotConfirmAReplacementCompanion()
    {
        using var oldHandler = new Handler { HoldFirst = true };
        using var oldHttp = new HttpClient(oldHandler);
        using var newHandler = new Handler();
        using var newHttp = new HttpClient(newHandler);
        var oldConnection = new CompanionConnection(8081, 123, new string('A', 64), oldHttp);
        var newConnection = new CompanionConnection(8081, 123, new string('A', 64), newHttp);
        var current = oldConnection;
        var settings = AgentSettings.CreateDefault();
        var synchronizer = new CompanionSettingsSynchronizer();
        var pending = synchronizer.SynchronizeAsync(() => new(current, settings), CancellationToken.None);
        await oldHandler.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        current = newConnection;
        oldHandler.Release.TrySetResult();
        Assert.Equal(CompanionSettingsSyncOutcome.Superseded, await pending);
        Assert.Equal(CompanionSettingsSyncOutcome.Confirmed,
            await synchronizer.SynchronizeAsync(() => new(current, settings), CancellationToken.None));
        Assert.Equal(1, newHandler.Budgets.Count);
    }

    public static async Task QueuedSavesSendLatestSettingsAndSkipDuplicateAcknowledgedWork()
    {
        using var handler = new Handler { HoldFirst = true };
        using var http = new HttpClient(handler);
        var connection = new CompanionConnection(8081, 123, new string('A', 64), http);
        var settings = AgentSettings.CreateDefault();
        var synchronizer = new CompanionSettingsSynchronizer();
        CompanionSettingsSnapshot Read() => new(connection, settings);
        var first = synchronizer.SynchronizeAsync(Read, CancellationToken.None);
        await handler.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        settings.MaxSessionRequests = 12;
        var second = synchronizer.SynchronizeAsync(Read, CancellationToken.None);
        settings.MaxSessionRequests = 22;
        var third = synchronizer.SynchronizeAsync(Read, CancellationToken.None);
        handler.Release.TrySetResult();
        await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, handler.Budgets.Count);
        Assert.Equal(22, handler.Budgets[1]);
        Assert.Equal(CompanionSettingsSyncOutcome.Confirmed, await second);
        Assert.Equal(CompanionSettingsSyncOutcome.Confirmed, await third);
    }

    public static async Task DisconnectionAndCancellationDoNotCacheAConfirmation()
    {
        using var handler = new Handler { HoldFirst = true };
        using var http = new HttpClient(handler);
        var connection = new CompanionConnection(8081, 123, new string('A', 64), http);
        var settings = AgentSettings.CreateDefault();
        var synchronizer = new CompanionSettingsSynchronizer();
        Assert.Equal(CompanionSettingsSyncOutcome.Disconnected,
            await synchronizer.SynchronizeAsync(() => new(null, settings), CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        var pending = synchronizer.SynchronizeAsync(() => new(connection, settings), cancellation.Token);
        await handler.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        try { await pending; throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        handler.Release.TrySetResult();
        Assert.Equal(CompanionSettingsSyncOutcome.Confirmed,
            await synchronizer.SynchronizeAsync(() => new(connection, settings), CancellationToken.None));
        Assert.Equal(2, handler.Budgets.Count);
        synchronizer.Reset();
        await synchronizer.SynchronizeAsync(() => new(connection, settings), CancellationToken.None);
        Assert.Equal(3, handler.Budgets.Count);
    }

    public static async Task MismatchedAndMissingAcknowledgementsAreNeverCached()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var connection = new CompanionConnection(8081, 123, new string('A', 64), http);
        var settings = AgentSettings.CreateDefault(); settings.DualLayerCoopEnabled = true;
        var synchronizer = new CompanionSettingsSynchronizer();
        CompanionSettingsSnapshot Read() => new(connection, settings);
        Assert.Equal(CompanionSettingsSyncOutcome.Unconfirmed, await synchronizer.SynchronizeAsync(Read, CancellationToken.None));
        handler.AcknowledgedEnabled = true;
        Assert.Equal(CompanionSettingsSyncOutcome.Confirmed, await synchronizer.SynchronizeAsync(Read, CancellationToken.None));
        Assert.Equal(2, handler.Budgets.Count);
        settings.MaxSessionRequests = 17;
        handler.MissingFullAck = true;
        try
        {
            await synchronizer.SynchronizeAsync(Read, CancellationToken.None);
            throw new Exception("Expected an incomplete acknowledgement to be refused");
        }
        catch (InvalidOperationException) { }
        handler.MissingFullAck = false;
        Assert.Equal(CompanionSettingsSyncOutcome.Confirmed, await synchronizer.SynchronizeAsync(Read, CancellationToken.None));
        Assert.Equal(4, handler.Budgets.Count);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public bool HoldFirst { get; set; }
        public bool MissingFullAck { get; set; }
        public bool AcknowledgedEnabled { get; set; }
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<int?> Budgets { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object data;
            if (request.Method == HttpMethod.Get)
                data = new { service = "sts2-ai-agent", status = "ready", instance_role = "companion", api_port = 8081, process_id = 123 };
            else
            {
                Assert.Equal(new string('A', 64), request.Headers.GetValues(CompanionConnection.TokenHeader).Single());
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var budget = body.RootElement.GetProperty("settings").GetProperty("play_settings").GetProperty("MaxSessionRequests");
                Budgets.Add(budget.ValueKind == JsonValueKind.Null ? null : budget.GetInt32());
                Posted.TrySetResult();
                if (HoldFirst && Budgets.Count == 1) await Release.Task.WaitAsync(cancellationToken);
                data = MissingFullAck
                    ? new { settings_applied = true, dual_layer_coop_enabled = AcknowledgedEnabled }
                    : new { settings_applied = true, play_settings_applied = true, dual_layer_coop_enabled = AcknowledgedEnabled };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { ok = true, data })) };
        }
    }
}
