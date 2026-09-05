using System.Text.Json;
using STS2AIAgent.Game;
using STS2AIAgent.Server;
using STS2AIAgent.Vision;

namespace STS2AIAgent.Agent;

internal sealed class GameBridge : IGameBridge
{
    private static readonly HashSet<string> PassiveActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "discard_potion",
        "save_and_quit"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(() =>
        {
            var state = GameStateService.BuildStatePayload();
            return JsonSerializer.Serialize(state.agent_view ?? state, JsonOptions);
        });
    }

    public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(() =>
        {
            var state = GameStateService.BuildStatePayload();
            return JsonSerializer.Serialize(state, JsonOptions);
        });
    }

    public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(() =>
        {
            var payload = GameStateService.BuildAvailableActionsPayload();
            return JsonSerializer.Serialize(payload.actions, JsonOptions);
        });
    }

    public Task<IReadOnlyList<string>> GetAvailableActionNamesAsync(CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(() =>
        {
            var state = GameStateService.BuildStatePayload();
            return (IReadOnlyList<string>)(state.available_actions ?? Array.Empty<string>());
        });
    }

    public Task<string> GetScreenAsync(CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(() => GameStateService.BuildStatePayload().screen);
    }

    public Task<string> ActAsync(
        string action,
        int? cardIndex,
        int? targetIndex,
        int? optionIndex,
        int? x,
        int? y,
        string? tool,
        CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await GameActionService.ExecuteAsync(new ActionRequest
            {
                action = action,
                card_index = cardIndex,
                target_index = targetIndex,
                option_index = optionIndex,
                x = x,
                y = y,
                tool = tool,
                client_context = new
                {
                    source = "in_game_agent",
                    instance_role = Config.InstanceRole.Current
                }
            });

            return JsonSerializer.Serialize(new
            {
                response.action,
                response.status,
                response.stable,
                response.message,
                state = response.state.agent_view ?? (object)response.state
            }, JsonOptions);
        });
    }

    public Task<string> GetGameDataItemJsonAsync(string collection, string itemId, CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(() =>
        {
            if (!TryExportCollection(collection, out var element, out var error))
            {
                return error;
            }

            var item = GameDataFilter.FindItem(element, itemId);
            return JsonSerializer.Serialize(item, JsonOptions);
        });
    }

    public Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(() =>
        {
            if (!TryExportCollection(collection, out var element, out var error))
            {
                return error;
            }

            return JsonSerializer.Serialize(GameDataFilter.FindItems(element, itemIds), JsonOptions);
        });
    }

    public Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(() =>
        {
            if (!TryExportCollection(collection, out var element, out var error))
            {
                return error;
            }

            var screen = GameStateService.BuildStatePayload().screen;
            return JsonSerializer.Serialize(GameDataFilter.ProjectRelevant(screen, collection, element, itemIds), JsonOptions);
        });
    }

    public async Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actionable = await GameThread.InvokeAsync(() =>
            {
                var state = GameStateService.BuildStatePayload();
                return (state.available_actions ?? Array.Empty<string>())
                    .Any(name => !PassiveActions.Contains(name));
            });

            if (actionable)
            {
                return true;
            }

            await GameThread.WaitForNextFrameAsync();
        }

        return false;
    }

    public Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(async () =>
        {
            ScreenshotService.BeginCapture?.Invoke();
            try
            {
                await GameThread.WaitForNextFrameAsync();
                cancellationToken.ThrowIfCancellationRequested();
                return ScreenshotService.CaptureJpeg();
            }
            finally
            {
                ScreenshotService.EndCapture?.Invoke();
            }
        });
    }

    private static bool TryExportCollection(string collection, out JsonElement element, out string errorJson)
    {
        try
        {
            var raw = GameDataExportService.ExportCollection(collection);
            element = JsonSerializer.SerializeToElement(raw, JsonOptions);
            errorJson = string.Empty;
            return true;
        }
        catch (KeyNotFoundException)
        {
            element = default;
            errorJson = JsonSerializer.Serialize(new
            {
                error = new
                {
                    type = "unknown_collection",
                    available_collections = GameDataFilter.KnownCollections
                }
            }, JsonOptions);
            return false;
        }
        catch (Exception ex)
        {
            element = default;
            errorJson = JsonSerializer.Serialize(new
            {
                error = new
                {
                    type = "game_data_unavailable",
                    message = ex.Message
                }
            }, JsonOptions);
            return false;
        }
    }
}
