using System.Collections.Generic;

namespace STS2AIAgent.Localization;

internal static partial class Loc
{
    // The 2026-10 experience overhaul: turbo, autopilot, whole-turn plans, combat math, cost meters,
    // self-healing, the in-game HUD and the run report. Kept in one shard so the overhaul's copy can
    // be reviewed (and translated) together.
    private static void AddOverhaulEntries(Dictionary<string, string> map)
    {
        // Cost meters (Agent/PlayerFacingSession.cs).
        map["{0} · 缓存命中 {1}%"] = "{0} · cache hit {1}%";
    }
}
