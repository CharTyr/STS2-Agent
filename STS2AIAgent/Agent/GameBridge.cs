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
        }, cancellationToken);
    }

    public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(() =>
        {
            var state = GameStateService.BuildStatePayload();
            return JsonSerializer.Serialize(state, JsonOptions);
        }, cancellationToken);
    }

    public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(() =>
        {
            var payload = GameStateService.BuildAvailableActionsPayload();
            return JsonSerializer.Serialize(payload.actions, JsonOptions);
        }, cancellationToken);
    }

    /// <summary>
    /// Everything one decision needs from the game -- the compact state and the action descriptors --
    /// from one state read inside one game-thread turn.
    /// </summary>
    /// <remarks>
    /// The reads a decision used to make separately (action names, descriptors, compact state) each
    /// built the whole payload, and each was its own game-thread invocation. Between two of them the
    /// game can advance, so a screen that changed mid-request left the index validator judging the
    /// action against a payload the legality check had never seen. The names and the descriptors come
    /// from the same walk by construction (ADR 0001); <see cref="GameStateService.BuildDecisionSnapshotPayload"/>
    /// makes them come from the same build too, by retaining the descriptors that walk produced
    /// instead of enumerating the action surface a second time. It is what the `act` and `decide`
    /// tools read.
    /// </remarks>
    public Task<string> GetActionSnapshotJsonAsync(CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(() =>
            JsonSerializer.Serialize(GameStateService.BuildDecisionSnapshotPayload(), JsonOptions), cancellationToken);
    }

    public Task<string> GetScreenAsync(CancellationToken cancellationToken)
    {
        return GameThread.InvokeAsync(() => GameStateService.BuildStatePayload().screen, cancellationToken);
    }

    public Task<string> ActAsync(
        string action,
        int? cardIndex,
        int? targetIndex,
        int? optionIndex,
        int? x,
        int? y,
        string? tool,
        CancellationToken cancellationToken,
        bool rawState = false)
    {
        return GameThread.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
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

                // The action's own post-action snapshot either way; only its shape changes. The
                // compact view is the default because the raw one costs thousands of tokens and a
                // decision reads the same fields from either -- raw_state is the escape hatch for a
                // caller that has to see a field the compact projection does not carry.
                var compactState = response.state.agent_view ?? (object)response.state;
                return JsonSerializer.Serialize(new
                {
                    response.action,
                    response.status,
                    response.stable,
                    response.message,
                    state = rawState ? (object)response.state : compactState
                }, JsonOptions);
            }
            catch (ApiException ex)
            {
                // A deliberate game failure already carries the same code/details/retryable the HTTP
                // boundary would send; keep it observable in-process through the shared envelope.
                return AgentErrorEnvelope.Serialize(ex, JsonOptions);
            }
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

            var state = GameStateService.BuildStatePayload();
            // An empty id list is the scene-aware call, not an empty answer: derive the ids this
            // screen is about so the tool does what its name and description promise when the
            // caller does not know them yet.
            IReadOnlyList<string> ids = itemIds;
            if (ids.Count == 0)
            {
                using var document = JsonSerializer.SerializeToDocument(state, JsonOptions);
                ids = GameDataFilter.DeriveRelevantItemIds(state.screen, collection, document.RootElement);
            }

            return JsonSerializer.Serialize(GameDataFilter.ProjectRelevant(state.screen, collection, element, ids), JsonOptions);
        });
    }

    /// <summary>
    /// The per-probe deadline inside <see cref="WaitUntilActionableAsync"/>: the outer loop's deadline
    /// is only checked between iterations, so each game-thread probe gets its own bound and a game
    /// thread that stops pumping fails the wait instead of hanging the turn.
    /// </summary>
    private static readonly TimeSpan ActionableProbeBudget = TimeSpan.FromSeconds(5);

    public async Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        string? stuckActionType = null;
        var stuckSince = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var probeBudget = ActionableProbeBudget;
            var remaining = deadline - DateTime.UtcNow;
            if (remaining < probeBudget)
            {
                probeBudget = remaining;
            }

            var actionable = await GameThread.InvokeAsync(() =>
            {
                var state = GameStateService.BuildStatePayload();
                var readiness = state.combat?.action_readiness;
                var selecting = readiness?.modal_open == true
                    || readiness?.hand_in_card_selection == true
                    || readiness?.hand_in_card_play == true;
                var runningType = readiness?.running_action_type;
                if (state.screen == "COMBAT"
                    && readiness?.reason == "game_action_running"
                    && !selecting
                    && !string.IsNullOrEmpty(runningType))
                {
                    if (!string.Equals(runningType, stuckActionType, StringComparison.Ordinal))
                    {
                        stuckActionType = runningType;
                        stuckSince = DateTime.UtcNow;
                    }
                    else if (DateTime.UtcNow - stuckSince >= TimeSpan.FromSeconds(12))
                    {
                        GameActionService.TryCancelRunningPlayerAction();
                        stuckSince = DateTime.UtcNow;
                    }
                }
                else
                {
                    stuckActionType = null;
                }

                if (state.screen == "COMBAT" && state.combat?.action_readiness?.can_use_combat_actions != true)
                {
                    return false;
                }

                return (state.available_actions ?? Array.Empty<string>())
                    .Any(name => !PassiveActions.Contains(name));
            }, probeBudget, cancellationToken);

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
