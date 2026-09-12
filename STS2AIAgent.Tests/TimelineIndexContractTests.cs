using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// Source-contract coverage for the timeline epoch index space. GameActionService.cs is not part of
/// the offline compile, so the executor's index space, its rejection of non-actionable slots, and the
/// new Crystal Sphere descriptor flags are asserted from source.
/// </summary>
internal static class TimelineIndexContractTests
{
    public static void ExecutorIndexesTheSameSlotListTheStateExposes()
    {
        var rawState = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var rawAction = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");

        var stateSlots = Normalize(AgentSourceFixture.MethodBody(rawState, "BuildTimelinePayload"));
        Assert.Contains("var slots = GetTimelineSlots(currentScreen)", stateSlots, StringComparison.Ordinal);
        Assert.Contains(".Select((slot, index) => new TimelineSlotPayload", stateSlots, StringComparison.Ordinal);
        Assert.Contains("index = index", stateSlots, StringComparison.Ordinal);
        Assert.Contains(
            "is_actionable = slot.State is EpochSlotState.Obtained or EpochSlotState.Complete",
            stateSlots,
            StringComparison.Ordinal);

        var resolve = Normalize(AgentSourceFixture.MethodBody(rawAction, "ResolveTimelineSlot"));
        Assert.Contains("var slots = GameStateService.GetTimelineSlots(currentScreen);", resolve, StringComparison.Ordinal);
        Assert.Contains("if (optionIndex < 0 || optionIndex >= slots.Count)", resolve, StringComparison.Ordinal);
        Assert.Contains("var slot = slots[optionIndex];", resolve, StringComparison.Ordinal);
        Assert.False(
            resolve.Contains(".Where(slot => slot.State is", StringComparison.Ordinal),
            "ResolveTimelineSlot must not filter the slot list before indexing it.");
    }

    public static void NonActionableSlotsAreRejectedExplicitly()
    {
        var rawAction = AgentSourceFixture.Read("STS2AIAgent/Game/GameActionService.cs");
        var resolve = Normalize(AgentSourceFixture.MethodBody(rawAction, "ResolveTimelineSlot"));

        Assert.Contains("slot.State is not (EpochSlotState.Obtained or EpochSlotState.Complete)", resolve, StringComparison.Ordinal);
        Assert.Contains("\"invalid_target\"", resolve, StringComparison.Ordinal);
        Assert.Contains("option_index_space = \"timeline.slots[].index\"", resolve, StringComparison.Ordinal);
        Assert.Contains("slot_state = slot.State.ToString().ToLowerInvariant()", resolve, StringComparison.Ordinal);
        Assert.Contains("is_actionable = false", resolve, StringComparison.Ordinal);
        Assert.Contains("slot_count = slots.Count", resolve, StringComparison.Ordinal);

        var rangeCheck = resolve.IndexOf("optionIndex >= slots.Count", StringComparison.Ordinal);
        var stateCheck = resolve.IndexOf("slot.State is not", StringComparison.Ordinal);
        Assert.True(rangeCheck >= 0, "The out-of-range check must compare against the exposed slot count.");
        Assert.True(stateCheck > rangeCheck, "The range check must reject before the slot state is examined.");

        // The executor must reject the slot before it clicks: ResolveTimelineSlot throws, so reading
        // a non-actionable index can never reach ForceClick.
        var execute = Normalize(AgentSourceFixture.MethodBody(rawAction, "ExecuteChooseTimelineEpochAsync"));
        var resolveCall = execute.IndexOf("ResolveTimelineSlot(currentScreen, request.option_index.Value)", StringComparison.Ordinal);
        var clickCall = execute.IndexOf("slot.ForceClick()", StringComparison.Ordinal);
        Assert.True(resolveCall >= 0, "The executor must resolve the requested slot through ResolveTimelineSlot.");
        Assert.True(clickCall > resolveCall, "The slot must be validated before it is clicked.");
    }

    public static void CrystalDescriptorsCarryTheirRequirements()
    {
        var rawState = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var descriptorClass = Normalize(
            AgentSourceFixture.DeclarationBody(rawState, "internal sealed class ActionDescriptor"));

        Assert.Contains("public string name { get; init; } = string.Empty;", descriptorClass, StringComparison.Ordinal);
        Assert.Contains("public bool requires_target { get; init; }", descriptorClass, StringComparison.Ordinal);
        Assert.Contains("public bool requires_index { get; init; }", descriptorClass, StringComparison.Ordinal);
        Assert.Contains("public bool requires_coordinates { get; init; }", descriptorClass, StringComparison.Ordinal);
        Assert.Contains("public bool requires_tool { get; init; }", descriptorClass, StringComparison.Ordinal);

        var normalized = Normalize(rawState);
        Assert.Contains(
            "name = \"crystal_set_tool\", requires_target = false, requires_index = false, requires_tool = true",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(
            "name = \"crystal_clear_cell\", requires_target = false, requires_index = false, requires_coordinates = true",
            normalized,
            StringComparison.Ordinal);

        // Only the two Crystal Sphere descriptors opt in; every other descriptor keeps the false default.
        Assert.Equal(1, Regex.Matches(normalized, "requires_coordinates = true").Count);
        Assert.Equal(1, Regex.Matches(normalized, "requires_tool = true").Count);
    }

    private static string Normalize(string source)
    {
        return Regex.Replace(source, "\\s+", " ");
    }
}
