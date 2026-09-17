using Godot;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline.UnlockScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;
using STS2AIAgent.Server;

namespace STS2AIAgent.Game;

/// <summary>
/// The merchant: opening the inventory, the three purchase paths and card removal.
/// </summary>
/// <remarks>
/// Part of <see cref="GameActionService"/>, split out of <c>GameActionService.cs</c> on
/// 2026-09-17. Every handler here follows the one pattern the whole service follows: validate
/// the screen, act on the game thread, wait for the transition with a deadline. The file
/// boundary is not a design -- which members live here was computed, by asking which ones
/// nothing outside this room references. Anything two rooms both reach stayed in the base file.
/// </remarks>
internal static partial class GameActionService
{
    private static async Task<ActionResponsePayload> ExecuteOpenShopInventoryAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanOpenShopInventory(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "open_shop_inventory",
                screen
            });
        }

        var fakeMerchantButton = GameStateService.GetFakeMerchantButton(currentScreen);
        if (currentScreen is NMerchantRoom merchantRoom)
        {
            merchantRoom.OpenInventory();
        }
        else if (fakeMerchantButton != null)
        {
            fakeMerchantButton.ForceClick();
        }
        else
        {
            throw new ApiException(503, "state_unavailable", "Shop inventory control is unavailable.", new
            {
                action = "open_shop_inventory",
                screen
            }, retryable: true);
        }

        var stable = await WaitForShopInventoryOpenAsync(TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "open_shop_inventory",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteCloseShopInventoryAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanCloseShopInventory(currentScreen) || currentScreen is not NMerchantInventory inventoryScreen)
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "close_shop_inventory",
                screen
            });
        }

        var backButton = inventoryScreen.GetNodeOrNull<NButton>("%BackButton")
            ?? throw new ApiException(503, "state_unavailable", "Shop back button not found.", new
            {
                action = "close_shop_inventory",
                screen
            }, retryable: true);

        backButton.ForceClick();
        var stable = await WaitForShopInventoryCloseAsync(TimeSpan.FromSeconds(10));

        return new ActionResponsePayload
        {
            action = "close_shop_inventory",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteBuyCardAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanBuyShopCard(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "buy_card",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "buy_card requires option_index.", new
            {
                action = "buy_card"
            });
        }

        var inventory = GameStateService.GetMerchantInventory(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Shop inventory is unavailable.", new
            {
                action = "buy_card",
                screen
            }, retryable: true);

        var cards = GameStateService.GetMerchantCardEntries(currentScreen).ToList();
        if (request.option_index < 0 || request.option_index >= cards.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "buy_card",
                option_index = request.option_index,
                option_count = cards.Count
            });
        }

        var entry = cards[request.option_index.Value];
        if (!entry.IsStocked)
        {
            throw new ApiException(409, "invalid_target", "The selected card is out of stock.", new
            {
                action = "buy_card",
                option_index = request.option_index
            });
        }

        var previousGold = inventory.Player.Gold;
        var previousCardId = entry.CreationResult?.Card.Id.Entry;
        var purchaseTimeout = TimeSpan.FromSeconds(10);
        var purchaseTask = entry.OnTryPurchaseWrapper(inventory);
        var completedPurchaseTask = await WaitForGameTaskAsync(purchaseTask, purchaseTimeout);
        var purchaseOutcome = ClassifyGameTaskWait(purchaseTask, completedPurchaseTask == null);
        if (purchaseOutcome == GameTaskWaitOutcome.TimedOut)
        {
            ObserveBackgroundResult(purchaseTask, "buy_card");
            return BuildGameTaskTimeoutResponse("buy_card", purchaseTimeout);
        }

        if (purchaseOutcome == GameTaskWaitOutcome.Failed)
        {
            throw new ApiException(409, "invalid_action", $"Card purchase failed: {DescribeGameTaskFailure(purchaseTask)}.", new
            {
                action = "buy_card",
                option_index = request.option_index
            });
        }

        var success = await completedPurchaseTask!;
        if (!success)
        {
            throw new ApiException(409, "invalid_action", "Card purchase failed in the current state.", new
            {
                action = "buy_card",
                option_index = request.option_index
            });
        }

        var stable = await WaitForMerchantCardPurchaseAsync(inventory.Player, entry, previousGold, previousCardId, purchaseTimeout);
        return new ActionResponsePayload
        {
            action = "buy_card",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteBuyRelicAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanBuyShopRelic(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "buy_relic",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "buy_relic requires option_index.", new
            {
                action = "buy_relic"
            });
        }

        var inventory = GameStateService.GetMerchantInventory(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Shop inventory is unavailable.", new
            {
                action = "buy_relic",
                screen
            }, retryable: true);

        var relics = GameStateService.GetMerchantRelicEntries(currentScreen).ToList();
        if (request.option_index < 0 || request.option_index >= relics.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "buy_relic",
                option_index = request.option_index,
                option_count = relics.Count
            });
        }

        var entry = relics[request.option_index.Value];
        if (!entry.IsStocked)
        {
            throw new ApiException(409, "invalid_target", "The selected relic is out of stock.", new
            {
                action = "buy_relic",
                option_index = request.option_index
            });
        }

        var previousGold = inventory.Player.Gold;
        var previousRelicId = entry.Model?.Id.Entry;
        var purchaseTimeout = TimeSpan.FromSeconds(10);
        var purchaseTask = entry.OnTryPurchaseWrapper(inventory);
        var completedPurchaseTask = await WaitForGameTaskAsync(purchaseTask, purchaseTimeout);
        var purchaseOutcome = ClassifyGameTaskWait(purchaseTask, completedPurchaseTask == null);
        if (purchaseOutcome == GameTaskWaitOutcome.TimedOut)
        {
            ObserveBackgroundResult(purchaseTask, "buy_relic");
            return BuildGameTaskTimeoutResponse("buy_relic", purchaseTimeout);
        }

        if (purchaseOutcome == GameTaskWaitOutcome.Failed)
        {
            throw new ApiException(409, "invalid_action", $"Relic purchase failed: {DescribeGameTaskFailure(purchaseTask)}.", new
            {
                action = "buy_relic",
                option_index = request.option_index
            });
        }

        var success = await completedPurchaseTask!;
        if (!success)
        {
            throw new ApiException(409, "invalid_action", "Relic purchase failed in the current state.", new
            {
                action = "buy_relic",
                option_index = request.option_index
            });
        }

        var stable = await WaitForMerchantRelicPurchaseAsync(inventory.Player, entry, previousGold, previousRelicId, purchaseTimeout);
        return new ActionResponsePayload
        {
            action = "buy_relic",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteBuyPotionAsync(ActionRequest request)
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanBuyShopPotion(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "buy_potion",
                screen
            });
        }

        if (request.option_index == null)
        {
            throw new ApiException(400, "invalid_request", "buy_potion requires option_index.", new
            {
                action = "buy_potion"
            });
        }

        var inventory = GameStateService.GetMerchantInventory(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Shop inventory is unavailable.", new
            {
                action = "buy_potion",
                screen
            }, retryable: true);

        var potions = GameStateService.GetMerchantPotionEntries(currentScreen).ToList();
        if (request.option_index < 0 || request.option_index >= potions.Count)
        {
            throw new ApiException(409, "invalid_target", "option_index is out of range.", new
            {
                action = "buy_potion",
                option_index = request.option_index,
                option_count = potions.Count
            });
        }

        var entry = potions[request.option_index.Value];
        if (!entry.IsStocked)
        {
            throw new ApiException(409, "invalid_target", "The selected potion is out of stock.", new
            {
                action = "buy_potion",
                option_index = request.option_index
            });
        }

        var previousGold = inventory.Player.Gold;
        var previousPotionId = entry.Model?.Id.Entry;
        var purchaseTimeout = TimeSpan.FromSeconds(10);
        var purchaseTask = entry.OnTryPurchaseWrapper(inventory);
        var completedPurchaseTask = await WaitForGameTaskAsync(purchaseTask, purchaseTimeout);
        var purchaseOutcome = ClassifyGameTaskWait(purchaseTask, completedPurchaseTask == null);
        if (purchaseOutcome == GameTaskWaitOutcome.TimedOut)
        {
            ObserveBackgroundResult(purchaseTask, "buy_potion");
            return BuildGameTaskTimeoutResponse("buy_potion", purchaseTimeout);
        }

        if (purchaseOutcome == GameTaskWaitOutcome.Failed)
        {
            throw new ApiException(409, "invalid_action", $"Potion purchase failed: {DescribeGameTaskFailure(purchaseTask)}.", new
            {
                action = "buy_potion",
                option_index = request.option_index
            });
        }

        var success = await completedPurchaseTask!;
        if (!success)
        {
            throw new ApiException(409, "invalid_action", "Potion purchase failed in the current state.", new
            {
                action = "buy_potion",
                option_index = request.option_index
            });
        }

        var stable = await WaitForMerchantPotionPurchaseAsync(inventory.Player, entry, previousGold, previousPotionId, purchaseTimeout);
        return new ActionResponsePayload
        {
            action = "buy_potion",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<ActionResponsePayload> ExecuteRemoveCardAtShopAsync()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        var screen = GameStateService.ResolveScreen(currentScreen);

        if (!GameStateService.CanRemoveCardAtShop(currentScreen))
        {
            throw new ApiException(409, "invalid_action", "Action is not available in the current state.", new
            {
                action = "remove_card_at_shop",
                screen
            });
        }

        var inventory = GameStateService.GetMerchantInventory(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Shop inventory is unavailable.", new
            {
                action = "remove_card_at_shop",
                screen
            }, retryable: true);

        var entry = GameStateService.GetMerchantCardRemovalEntry(currentScreen)
            ?? throw new ApiException(503, "state_unavailable", "Shop card removal service is unavailable.", new
            {
                action = "remove_card_at_shop",
                screen
            }, retryable: true);

        // Fire-and-forget: merchant card removal opens deck selection and blocks
        // until the player confirms a card. Do not await the full task here, but a
        // purchase that already failed must not look like a pending transition.
        var purchaseTask = entry.OnTryPurchaseWrapper(inventory);
        ObserveBackgroundResult(purchaseTask, "remove_card_at_shop");
        var stable = await WaitForShopCardRemovalTransitionAsync(TimeSpan.FromSeconds(10));

        var purchaseFailure = BackgroundTaskOutcome.DescribeFailure(
            isCompleted: purchaseTask.IsCompleted,
            isFaulted: purchaseTask.IsFaulted,
            isCanceled: purchaseTask.IsCanceled,
            result: purchaseTask.Status == TaskStatus.RanToCompletion ? purchaseTask.Result : null);
        if (!stable && purchaseFailure != null)
        {
            throw new ApiException(
                409,
                "invalid_action",
                $"Card removal failed: {purchaseFailure}{DescribeTaskFault(purchaseTask)}.",
                new
                {
                    action = "remove_card_at_shop",
                    screen
                });
        }

        return new ActionResponsePayload
        {
            action = "remove_card_at_shop",
            status = stable ? "completed" : "pending",
            stable = stable,
            message = stable ? "Action completed." : "Action queued but state is still transitioning.",
            state = GameStateService.BuildStatePayload()
        };
    }

    private static async Task<bool> WaitForShopInventoryOpenAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is NMerchantInventory inventory && inventory.IsOpen)
            {
                return true;
            }
        }

        return ActiveScreenContext.Instance.GetCurrentScreen() is NMerchantInventory openInventory && openInventory.IsOpen;
    }

    private static async Task<bool> WaitForShopInventoryCloseAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is not NMerchantInventory)
            {
                return true;
            }
        }

        return ActiveScreenContext.Instance.GetCurrentScreen() is not NMerchantInventory;
    }

    private static async Task<bool> WaitForMerchantCardPurchaseAsync(
        Player player,
        MerchantCardEntry entry,
        int previousGold,
        string? previousCardId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentGold = player.Gold;
            var currentCardId = entry.CreationResult?.Card.Id.Entry;
            if (currentGold != previousGold || currentCardId != previousCardId || !entry.IsStocked)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> WaitForMerchantRelicPurchaseAsync(
        Player player,
        MerchantRelicEntry entry,
        int previousGold,
        string? previousRelicId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentGold = player.Gold;
            var currentRelicId = entry.Model?.Id.Entry;
            if (currentGold != previousGold || currentRelicId != previousRelicId || !entry.IsStocked)
            {
                return true;
            }
        }

        return player.Gold != previousGold || entry.Model?.Id.Entry != previousRelicId || !entry.IsStocked;
    }

    private static async Task<bool> WaitForMerchantPotionPurchaseAsync(
        Player player,
        MerchantPotionEntry entry,
        int previousGold,
        string? previousPotionId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentGold = player.Gold;
            var currentPotionId = entry.Model?.Id.Entry;
            if (currentGold != previousGold || currentPotionId != previousPotionId || !entry.IsStocked)
            {
                return true;
            }
        }

        return player.Gold != previousGold || entry.Model?.Id.Entry != previousPotionId || !entry.IsStocked;
    }

    private static async Task<bool> WaitForShopCardRemovalTransitionAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await WaitForNextFrameAsync();

            var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (currentScreen is NCardGridSelectionScreen || currentScreen is not NMerchantInventory)
            {
                return true;
            }
        }

        var finalScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        return finalScreen is NCardGridSelectionScreen || finalScreen is not NMerchantInventory;
    }
}
