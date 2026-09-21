using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Ftue;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.FeedbackScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;
using MegaCrit.Sts2.Core.Nodes.Screens.PotionLab;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.StatsScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline.UnlockScreens;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.addons.mega_text;
using STS2AIAgent.Agent;
using STS2AIAgent.Config;
using STS2AIAgent.Localization;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Game;

internal static partial class GameStateService
{

    private static EventPayload? BuildEventPayload(IScreenContext? currentScreen)
    {
        if (currentScreen is not NEventRoom)
        {
            return null;
        }

        try
        {
            var eventModel = RunManager.Instance.EventSynchronizer.GetLocalEvent();
            if (eventModel == null)
            {
                return null;
            }

            var options = new List<EventOptionPayload>();

            if (eventModel.IsFinished)
            {
                // Mirror NEventRoom.SetOptions(): synthesize a Proceed option
                options.Add(new EventOptionPayload
                {
                    index = 0,
                    text_key = "PROCEED",
                    title = "Proceed",
                    description = "",
                    is_locked = false,
                    is_proceed = true
                });
            }
            else
            {
                var currentOptions = eventModel.CurrentOptions;
                for (int i = 0; i < currentOptions.Count; i++)
                {
                    var opt = currentOptions[i];
                    options.Add(new EventOptionPayload
                    {
                        index = i,
                        text_key = SafeReadString(() => opt.TextKey),
                        title = SafeReadString(() => EventOptionLocalization.Format(
                            opt.Title,
                            locString => eventModel.DynamicVars.AddTo(locString),
                            locString => locString.GetFormattedText())),
                        description = SafeReadString(() => EventOptionLocalization.Format(
                            opt.Description,
                            locString => eventModel.DynamicVars.AddTo(locString),
                            locString => locString.GetFormattedText())),
                        is_locked = SafeReadBool(() => opt.IsLocked),
                        is_proceed = SafeReadBool(() => opt.IsProceed),
                        will_kill_player = GetEventOptionWillKillPlayer(eventModel, opt),
                        has_relic_preview = SafeReadBool(() => opt.Relic != null)
                    });
                }
            }

            return new EventPayload
            {
                event_id = SafeReadString(() => eventModel.Id?.Entry, "unknown"),
                title = SafeReadString(() => eventModel.Title?.GetFormattedText()),
                description = SafeReadString(() => eventModel.Description?.GetFormattedText()),
                is_finished = SafeReadBool(() => eventModel.IsFinished),
                options = options.ToArray()
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"[STS2AIAgent] Failed to build event payload on screen {currentScreen.GetType().FullName}: {ex}");
            return null;
        }
    }

    private static bool GetEventOptionWillKillPlayer(EventModel eventModel, EventOption option)
    {
        try
        {
            var owner = eventModel.Owner;
            return owner != null && option.WillKillPlayer != null && option.WillKillPlayer(owner);
        }
        catch
        {
            return false;
        }
    }

    private static RestPayload? BuildRestPayload(IScreenContext? currentScreen, RunState? runState)
    {
        if (currentScreen is not NRestSiteRoom)
        {
            return null;
        }

        try
        {
            var options = RunManager.Instance.RestSiteSynchronizer.GetLocalOptions();
            var localPlayer = GetLocalPlayer(runState);
            if (options == null)
            {
                return new RestPayload
                {
                    options = Array.Empty<RestOptionPayload>()
                };
            }

            return new RestPayload
            {
                options = options.Select((opt, i) =>
                {
                    var requiresTarget = RestOptionRequiresTarget(opt, runState, localPlayer);
                    var validTargetIndices = requiresTarget
                        ? GetRestOptionTargetIndices(runState, localPlayer, allowSelf: false)
                        : Array.Empty<int>();

                    return new RestOptionPayload
                    {
                        index = i,
                        option_id = opt.OptionId ?? "unknown",
                        title = opt.Title?.GetFormattedText() ?? "",
                        description = opt.Description?.GetFormattedText() ?? "",
                        is_enabled = opt.IsEnabled,
                        requires_target = requiresTarget,
                        target_index_space = requiresTarget
                            ? GetRestOptionTargetIndexSpace(opt, runState, localPlayer)
                            : null,
                        valid_target_indices = validTargetIndices,
                        valid_target_player_ids = requiresTarget
                            ? validTargetIndices
                                .Select(index => ResolveRunPlayerTarget(runState, index))
                                .Where(player => player != null)
                                .Select(player => NetIdToString(player!.NetId))
                                .ToArray()
                            : Array.Empty<string>()
                    };
                }).ToArray()
            };
        }
        catch
        {
            return null;
        }
    }

    public static bool RestOptionRequiresTarget(RestSiteOption option, RunState? runState, Player? localPlayer)
    {
        return runState != null &&
            localPlayer != null &&
            string.Equals(option.OptionId, "MEND", StringComparison.OrdinalIgnoreCase) &&
            GetRestOptionTargetIndices(runState, localPlayer, allowSelf: false).Length > 0;
    }

    public static string? GetRestOptionTargetIndexSpace(RestSiteOption option, RunState? runState, Player? localPlayer)
    {
        return RestOptionRequiresTarget(option, runState, localPlayer) ? "run.players" : null;
    }

    public static int[] GetRestOptionTargetIndices(RunState? runState, Player? localPlayer, bool allowSelf)
    {
        if (runState == null || localPlayer == null)
        {
            return Array.Empty<int>();
        }

        return runState.Players
            .OrderBy(runState.GetPlayerSlotIndex)
            .Select((player, index) => new { player, index })
            .Where(entry => entry.player.Creature.IsAlive && (allowSelf || entry.player.NetId != localPlayer.NetId))
            .Select(entry => entry.index)
            .ToArray();
    }

    private static object? BuildCapstonePayload(IScreenContext? currentScreen)
    {
        var buttons = GetCapstoneButtons(currentScreen);
        if (buttons.Count == 0)
        {
            return null;
        }

        return new
        {
            options = buttons.Select((button, index) => new
            {
                i = index,
                index,
                line = GetButtonLabel(button) ?? $"option {index}"
            }).ToArray()
        };
    }

    /// <summary>
    /// True while the shared capstone container is showing one of the in-run human menus it exists
    /// for (pause, settings, compendium, card library and their siblings).
    /// </summary>
    /// <summary>
    /// The pages this build pushes into the container. Every one of them is a menu the player opened
    /// (the container is only ever shown by the top-bar pause button), so none is a decision list and
    /// the run underneath is frozen while one is up.
    /// </summary>
    private static bool IsKnownCapstoneContainerPage(NSubmenu? page)
    {
        return page is NPauseMenu
            or NSettingsScreen
            or NCompendiumSubmenu
            or NCardLibrary
            or NRelicCollection
            or NPotionLab
            or NBestiary
            or NStatsScreen
            or NRunHistory;
    }

    /// <summary>
    /// The page on the container's stack that <c>close_main_menu_submenu</c> may pop, or null when
    /// there is nothing an agent should close from here. Every page in the container is left by its own
    /// BackButton, which the game wires to <c>Stack.Pop()</c>, so backing out one level is exactly what
    /// the person sitting there would do. The pause menu itself is never one of them: that page is where
    /// a person resumes the run, and the agent does not unpause a game on their behalf.
    /// </summary>
    public static NSubmenu? GetClosableCapstonePage(IScreenContext? currentScreen)
    {
        if (currentScreen is not NCapstoneSubmenuStack container)
        {
            return null;
        }

        var page = container.Stack?.Peek();
        if (page == null || !IsKnownCapstoneContainerPage(page) || page is NPauseMenu || !page.IsVisibleInTree())
        {
            return null;
        }

        return page;
    }

    public static IReadOnlyList<NButton> GetCapstoneButtons(IScreenContext? currentScreen)
    {
        if (currentScreen is not NCapstoneSubmenuStack capstoneScreen)
        {
            return Array.Empty<NButton>();
        }

        // The container's pages are menus, not option lists: their buttons are navigation tiles,
        // filter tickboxes and the pause menu's own "放弃", none of which is an agent decision. This
        // build has no boss-reward option screen (CapstoneSubmenuType carries no such value), so the
        // descendant scan only runs for a page this build does not know, which is what a real option
        // screen would need if the game grows one back.
        if (IsKnownCapstoneContainerPage(capstoneScreen.Stack?.Peek()))
        {
            return Array.Empty<NButton>();
        }

        return FindDescendants<NButton>((Node)capstoneScreen)
            .Where(b => GodotObject.IsInstanceValid(b) && b.IsVisibleInTree() && b.IsEnabled)
            .ToArray();
    }

    private static CrystalSpherePayload? BuildCrystalSpherePayload(IScreenContext? currentScreen)
    {
        var minigame = GetCrystalSphereMinigame(currentScreen);
        if (minigame == null)
        {
            return null;
        }

        try
        {
            var grid = minigame.GridSize;
            var cells = minigame.cells;
            var hiddenCells = new List<int[]>();
            var items = new List<CrystalSphereItemPayload>();

            for (var x = 0; x < grid.X; x++)
            {
                for (var y = 0; y < grid.Y; y++)
                {
                    if (cells[x, y].IsHidden)
                    {
                        hiddenCells.Add(new[] { x, y });
                    }
                }
            }

            foreach (var item in minigame.Items)
            {
                // Occupied cells are the source of truth: items that failed to
                // place occupy no cells, can never be revealed, and are omitted.
                var occupied = new List<CrystalSphereCell>();
                for (var x = 0; x < grid.X; x++)
                {
                    for (var y = 0; y < grid.Y; y++)
                    {
                        if (ReferenceEquals(cells[x, y].Item, item))
                        {
                            occupied.Add(cells[x, y]);
                        }
                    }
                }

                if (occupied.Count == 0)
                {
                    continue;
                }

                // Identity is bought with divinations, not read off the board: an item whose every
                // cell is still hidden reports kind/is_good as null, so the raw and compact payloads
                // cannot hand a client the reward/curse layout for free. Geometry and occupancy stay
                // visible either way -- they are what the client plans its clicks against.
                var revealed = occupied.All(c => !c.IsHidden);

                items.Add(new CrystalSphereItemPayload
                {
                    kind = revealed ? item.GetType().Name.Replace("CrystalSphere", string.Empty) : null,
                    is_good = revealed ? SafeReadBool(() => item.IsGood) : null,
                    x = occupied.Min(c => c.X),
                    y = occupied.Min(c => c.Y),
                    width = item.Size.X,
                    height = item.Size.Y,
                    revealed = revealed,
                    cells = occupied.Select(c => new[] { c.X, c.Y }).ToArray(),
                    hidden_cells = occupied.Where(c => c.IsHidden).Select(c => new[] { c.X, c.Y }).ToArray()
                });
            }

            return new CrystalSpherePayload
            {
                divinations_left = minigame.DivinationCount,
                tool = minigame.CrystalSphereTool.ToString().ToLowerInvariant(),
                is_finished = minigame.IsFinished,
                grid_width = grid.X,
                grid_height = grid.Y,
                hidden_cells = hiddenCells.ToArray(),
                items = items.ToArray()
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"[STS2AIAgent] Failed to build crystal sphere payload: {ex}");
            return null;
        }
    }

    public static CrystalSphereMinigame? GetCrystalSphereMinigame(IScreenContext? currentScreen)
    {
        if (currentScreen is not NCrystalSphereScreen crystalSphereScreen)
        {
            return null;
        }

        if (CrystalSphereEntityField == null)
        {
            LogCrystalSphereEntityLookupFailure(
                "private field NCrystalSphereScreen._entity was not found");
            return null;
        }

        try
        {
            var minigame = CrystalSphereEntityField.GetValue(crystalSphereScreen) as CrystalSphereMinigame;
            if (minigame == null)
            {
                LogCrystalSphereEntityLookupFailure(
                    "private field NCrystalSphereScreen._entity did not contain a CrystalSphereMinigame");
            }

            return minigame;
        }
        catch (Exception ex)
        {
            LogCrystalSphereEntityLookupFailure(
                $"reading NCrystalSphereScreen._entity threw {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static FieldInfo? CrystalSphereEntityField =>
        ReflectedGameMembers.Field(typeof(NCrystalSphereScreen), "_entity");

    private static void LogCrystalSphereEntityLookupFailure(string reason)
    {
        if (_crystalSphereEntityLookupWarningLogged)
        {
            return;
        }

        _crystalSphereEntityLookupWarningLogged = true;
        Log.Warn($"[STS2AIAgent] Crystal Sphere state is unavailable: {reason}.");
    }

    public static bool TrySetCrystalSphereTool(
        IScreenContext? currentScreen,
        CrystalSphereMinigame.CrystalSphereToolType tool)
    {
        var minigame = GetCrystalSphereMinigame(currentScreen);
        if (minigame == null || minigame.IsFinished)
        {
            return false;
        }

        minigame.SetTool(tool);

        if (currentScreen is not NCrystalSphereScreen crystalSphereScreen)
        {
            return true;
        }

        try
        {
            var bigButton = crystalSphereScreen.GetNodeOrNull<NDivinationButton>("%BigDivinationButton");
            var smallButton = crystalSphereScreen.GetNodeOrNull<NDivinationButton>("%SmallDivinationButton");
            if (bigButton == null || smallButton == null)
            {
                LogCrystalSphereButtonLookupFailure(
                    $"missing button node(s): big={bigButton != null}, small={smallButton != null}");
            }

            if (bigButton != null && GodotObject.IsInstanceValid(bigButton))
            {
                bigButton.SetActive(tool == CrystalSphereMinigame.CrystalSphereToolType.Big);
            }

            if (smallButton != null && GodotObject.IsInstanceValid(smallButton))
            {
                smallButton.SetActive(tool == CrystalSphereMinigame.CrystalSphereToolType.Small);
            }
        }
        catch (Exception ex)
        {
            // The model state is authoritative; keep the action valid even if a
            // future UI scene revision prevents the visual active-state update.
            LogCrystalSphereButtonLookupFailure(
                $"button synchronization threw {ex.GetType().Name}: {ex.Message}");
        }

        return true;
    }

    private static void LogCrystalSphereButtonLookupFailure(string reason)
    {
        if (_crystalSphereButtonLookupWarningLogged)
        {
            return;
        }

        _crystalSphereButtonLookupWarningLogged = true;
        Log.Warn($"[STS2AIAgent] Crystal Sphere tool button state could not be synchronized: {reason}.");
    }

    private static ModalPayload? BuildModalPayload(IScreenContext? currentScreen)
    {
        var modal = GetOpenModal();
        if (modal is not Node modalNode)
        {
            return null;
        }

        var confirmButton = GetModalConfirmButton(currentScreen);
        var cancelButton = GetModalCancelButton(currentScreen);

        return new ModalPayload
        {
            type_name = modal.GetType().Name,
            underlying_screen = currentScreen is Node node && ReferenceEquals(node, modalNode)
                ? ResolveUnderlyingScreen(modalNode)
                : null,
            // Same predicate that exposes confirm_modal: a FTUE popup without its own button is still
            // confirmable (the executor closes it directly), so a bare button check under-reports it.
            can_confirm = CanConfirmModal(currentScreen),
            can_dismiss = cancelButton != null,
            confirm_label = GetButtonLabel(confirmButton),
            dismiss_label = GetButtonLabel(cancelButton)
        };
    }

    public static IScreenContext? GetOpenModal()
    {
        return NModalContainer.Instance?.OpenModal;
    }

    public static bool TryCloseOpenFtue()
    {
        var modal = GetOpenModal();
        if (modal == null || !FtueModalPolicy.ForceCloseIfStuck(modal.GetType().Name))
        {
            return false;
        }

        var closed = false;
        var confirmButton = GetModalConfirmButton(ActiveScreenContext.Instance.GetCurrentScreen());
        var closeWithButton = FindInstanceMethod(modal.GetType(), "CloseFtue", typeof(NButton));
        if (closeWithButton != null)
        {
            try
            {
                closeWithButton.Invoke(modal, new object?[] { confirmButton });
                closed = true;
            }
            catch (Exception ex)
            {
                Log.Warn("[STS2AIAgent] CloseFtue(NButton) failed: " + ex.GetBaseException().Message);
            }
        }

        if (IsModalGone(modal))
        {
            return true;
        }

        foreach (var methodName in FtueModalPolicy.CloseMethodNames(modal.GetType().Name))
        {
            var method = FindInstanceMethod(modal.GetType(), methodName);
            if (method == null)
            {
                continue;
            }

            try
            {
                method.Invoke(modal, null);
                closed = true;
            }
            catch (Exception ex)
            {
                Log.Warn("[STS2AIAgent] " + methodName + " failed: " + ex.GetBaseException().Message);
            }

            if (IsModalGone(modal))
            {
                return true;
            }
        }

        try
        {
            NModalContainer.Instance?.Clear();
            closed = true;
        }
        catch (Exception ex)
        {
            Log.Warn("[STS2AIAgent] NModalContainer.Clear failed: " + ex.Message);
        }

        if (modal is Node node && GodotObject.IsInstanceValid(node))
        {
            try
            {
                node.QueueFree();
                closed = true;
            }
            catch (Exception ex)
            {
                Log.Warn("[STS2AIAgent] FTUE QueueFree failed: " + ex.Message);
            }
        }

        return closed || IsModalGone(modal);
    }

    private static bool IsModalGone(IScreenContext previousModal)
    {
        var current = GetOpenModal();
        return current == null || !ReferenceEquals(current, previousModal);
    }

    public static NButton? GetModalConfirmButton(IScreenContext? currentScreen)
    {
        return FindModalButton(
            isConfirm: true,
            "VerticalPopup/YesButton",
            "YesButton",
            "%YesButton",
            "ConfirmButton",
            "%ConfirmButton",
            "%Confirm",
            "%AcknowledgeButton",
            "OkButton",
            "%OkButton",
            "OKButton");
    }

    public static NButton? GetModalCancelButton(IScreenContext? currentScreen)
    {
        return FindModalButton(
            isConfirm: false,
            "VerticalPopup/NoButton",
            "NoButton",
            "%NoButton",
            "CancelButton",
            "%CancelButton",
            "%BackButton",
            "BackButton");
    }

    private static NButton? FindModalButton(bool isConfirm, params string[] paths)
    {
        if (GetOpenModal() is not Node modalNode)
        {
            return null;
        }

        if (modalNode is NVerticalPopup verticalPopup)
        {
            var typedButton = isConfirm ? verticalPopup.YesButton : verticalPopup.NoButton;
            if (IsUsableModalButton(typedButton))
            {
                return typedButton;
            }
        }

        foreach (var path in paths)
        {
            var button = modalNode.GetNodeOrNull<NButton>(path);
            if (IsUsableModalButton(button))
            {
                return button;
            }
        }

        var usableButtons = FindDescendants<NButton>(modalNode).Where(IsUsableModalButton).ToArray();
        if (isConfirm)
        {
            var ftueConfirm = usableButtons.OfType<NFtueConfirmButton>().FirstOrDefault();
            if (ftueConfirm != null)
            {
                return ftueConfirm;
            }

            var namedConfirm = usableButtons.FirstOrDefault(IsConfirmNamedModalButton);
            if (namedConfirm != null)
            {
                return namedConfirm;
            }

            if (usableButtons.Length == 1 && !IsDismissNamedModalButton(usableButtons[0]))
            {
                return usableButtons[0];
            }
        }
        else
        {
            var namedDismiss = usableButtons.FirstOrDefault(IsDismissNamedModalButton);
            if (namedDismiss != null)
            {
                return namedDismiss;
            }
        }

        return null;
    }

    private static bool IsUsableModalButton(NButton? button)
    {
        return button != null &&
               GodotObject.IsInstanceValid(button) &&
               button.IsEnabled &&
               button.IsVisibleInTree();
    }

    private static bool IsConfirmNamedModalButton(NButton button)
    {
        return ModalButtonNameContains(button, "Yes", "Confirm", "Acknowledge", "Ok", "Okay", "Continue", "Accept", "RightArrow");
    }

    private static bool IsDismissNamedModalButton(NButton button)
    {
        return ModalButtonNameContains(button, "No", "Cancel", "Back", "Dismiss", "Close");
    }

    private static bool ModalButtonNameContains(NButton button, params string[] tokens)
    {
        var name = button.Name.ToString();
        return tokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static GameOverPayload? BuildGameOverPayload(IScreenContext? currentScreen, RunState? runState)
    {
        if (currentScreen is not NGameOverScreen)
        {
            return null;
        }

        var player = GetLocalPlayer(runState);
        var continueButton = GetGameOverContinueButton(currentScreen);
        var mainMenuButton = GetGameOverMainMenuButton(currentScreen);
        var canContinue = IsGameOverButtonReady(continueButton)
            && !IsGameOverButtonReady(mainMenuButton);
        var canReturnToMainMenu = IsGameOverButtonReady(mainMenuButton);
        var phase = canReturnToMainMenu
            ? "summary_ready"
            : canContinue
                ? "intro"
                : "summary_animating";
        var saveVerification = VerifyGameOverProgressSave(canReturnToMainMenu);
        var history = RunManager.Instance.History;

        return new GameOverPayload
        {
            is_victory = history?.Win ?? (runState?.CurrentRoom?.IsVictoryRoom ?? false),
            floor = runState?.TotalFloor,
            character_id = player?.Character.Id.Entry,
            phase = phase,
            can_continue = continueButton?.Visible == true
                && continueButton?.IsEnabled == true
                && continueButton?.IsVisibleInTree() == true
                && !canReturnToMainMenu,
            can_return_to_main_menu = mainMenuButton?.Visible == true
                && mainMenuButton?.IsEnabled == true
                && mainMenuButton?.IsVisibleInTree() == true,
            showing_summary = mainMenuButton?.Visible == true,
            waiting_for_other_players = IsWaitingForOtherPlayers(currentScreen),
            save_status = saveVerification.Status,
            save_verified = saveVerification.Verified,
            save_error = saveVerification.Error
        };
    }

    private static ProgressSaveVerificationResult VerifyGameOverProgressSave(bool summaryReady)
    {
        if (!summaryReady)
        {
            return ProgressSaveVerificationResult.Pending();
        }

        try
        {
            var saveManager = SaveManager.Instance;
            if (!saveManager.IsProfileInitialized)
            {
                return ProgressSaveVerificationResult.Failure("progress_profile_not_initialized");
            }

            var expectedProgress = saveManager.Progress.ToSerializable();
            expectedProgress.SchemaVersion = saveManager.GetLatestSchemaVersion<SerializableProgress>();
            var expectedJson = SaveManager.ToJson(expectedProgress);
            var relativePath = Path.Combine(UserDataPathProvider.SavesDir, ProgressSaveManager.fileName);
            var profileScopedPath = saveManager.GetProfileScopedPath(relativePath);
            // Use the same Godot user:// filesystem that SaveManager writes.
            // Globalizing and reopening through System.IO can fail while the
            // engine/Steam backend still owns the file even though the native
            // save has completed and Godot can read it normally.
            try
            {
                if (!Godot.FileAccess.FileExists(profileScopedPath))
                {
                    return ProgressSaveVerificationResult.Failure("progress_save_missing");
                }

                using var persistedFile = Godot.FileAccess.Open(
                    profileScopedPath,
                    Godot.FileAccess.ModeFlags.Read);
                if (persistedFile is null)
                {
                    return ProgressSaveVerificationResult.Failure(
                        $"progress_save_read_failed:Godot:{Godot.FileAccess.GetOpenError()}");
                }

                return ProgressSaveVerification.VerifyJson(
                    expectedJson,
                    persistedFile.GetAsText());
            }
            catch (Exception exception)
            {
                return ProgressSaveVerificationResult.Failure(
                    $"progress_save_read_failed:{exception.GetType().Name}");
            }
        }
        catch (Exception exception)
        {
            return ProgressSaveVerificationResult.Failure(
                $"progress_snapshot_failed:{exception.GetType().Name}");
        }
    }

    public static NGameOverContinueButton? GetGameOverContinueButton(IScreenContext? currentScreen)
    {
        return (currentScreen as NGameOverScreen)?
            .GetNodeOrNull<NGameOverContinueButton>("%ContinueButton");
    }

    public static NReturnToMainMenuButton? GetGameOverMainMenuButton(IScreenContext? currentScreen)
    {
        return (currentScreen as NGameOverScreen)?
            .GetNodeOrNull<NReturnToMainMenuButton>("%MainMenuButton");
    }

    private static bool IsGameOverButtonReady(NButton? button)
    {
        return button != null
            && GodotObject.IsInstanceValid(button)
            && button.IsVisibleInTree()
            && button.IsEnabled;
    }

    public static NMainMenuTextButton? GetMainMenuAbandonRunButton(NMainMenu mainMenu)
    {
        return mainMenu.GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/AbandonRunButton");
    }

    public static NMainMenuTextButton? GetMainMenuSingleplayerButton(NMainMenu mainMenu)
    {
        return mainMenu.GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/SingleplayerButton");
    }

    public static NButton? GetSingleplayerStandardButton(NSingleplayerSubmenu submenu)
    {
        if (ReflectedGameMembers.Field(typeof(NSingleplayerSubmenu), "_standardButton")?.GetValue(submenu) is NButton fieldButton &&
            IsUsableModalButton(fieldButton))
        {
            return fieldButton;
        }

        foreach (var path in new[] { "%StandardButton", "StandardButton", "Buttons/StandardButton" })
        {
            var button = submenu.GetNodeOrNull<NButton>(path);
            if (IsUsableModalButton(button))
            {
                return button;
            }
        }

        return FindDescendants<NButton>(submenu).FirstOrDefault(button =>
            IsUsableModalButton(button) &&
            ModalButtonNameContains(button, "Standard") &&
            !ModalButtonNameContains(button, "Daily", "Custom"));
    }

    public static NMainMenuTextButton? GetMainMenuContinueButton(NMainMenu mainMenu)
    {
        return mainMenu.GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/ContinueButton");
    }

    public static bool HideWaitingForOtherPlayers(IScreenContext? currentScreen)
    {
        if (currentScreen is not NGameOverScreen gameOver)
        {
            return false;
        }

        // Two reflective calls used to come first here -- SetWaitingForOtherPlayersOverlayVisible
        // and HideWaitingForPlayersScreen. Neither is declared on NGameOverScreen or anything it
        // inherits from: the first lives on NCombatRoom and the second on NRewardsScreen. Both
        // lookups returned null every time, so hiding the overlay node below has always been the
        // whole of this method.
        var hidden = false;
        var overlay = GetWaitingForOtherPlayersOverlay(gameOver);
        if (overlay != null && overlay.Visible)
        {
            overlay.Visible = false;
            hidden = true;
        }

        return hidden || !IsWaitingForOtherPlayers(gameOver);
    }

    public static CanvasItem? GetWaitingForOtherPlayersOverlay(IScreenContext? currentScreen)
    {
        if (currentScreen is not NGameOverScreen gameOver)
        {
            return null;
        }

        foreach (var path in new[] { "%WaitingForOtherPlayers", "WaitingForOtherPlayers" })
        {
            var node = gameOver.GetNodeOrNull<CanvasItem>(path);
            if (node != null)
            {
                return node;
            }
        }

        return FindDescendants<CanvasItem>(gameOver)
            .FirstOrDefault(node => GodotObject.IsInstanceValid(node)
                && node.Name.ToString().Contains("WaitingForOtherPlayers", StringComparison.OrdinalIgnoreCase));
    }
}
