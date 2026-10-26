using System.Collections.Generic;
using Godot;

namespace STS2AIAgent.Ui;

/// <summary>
/// Logs which controls ask for more width than the panel has.
/// </summary>
/// <remarks>
/// Kept permanently as a regression sentinel: it logs again whenever the offender count on the current
/// tab changes (a new wide control appeared, or a fix landed), so an overflow introduced later by an
/// action -- like the settings rebuild after deleting a model -- cannot hide behind a clean first
/// report. Look for <c>[STS2AIAgent.Wide]</c> in the game log.
/// </remarks>
internal sealed class OverlayOverflowSentinel
{
    private string? _signature;

    public void Report(Control panel, Control content, string tab)
    {
        if (!panel.Visible)
        {
            return;
        }

        var limit = panel.Size.X;
        var lines = new List<string>();
        CollectWide(content, limit, lines);
        // Log on change only: tab + offender count is the signature. The first exposure logs too
        // (signature starts empty), so the baseline "offenders=0" still appears in the log.
        var signature = tab + "|" + lines.Count;
        if (_signature == signature)
        {
            return;
        }

        _signature = signature;
        GD.Print($"[STS2AIAgent.Wide] tab={tab} panel={limit:0} offenders={lines.Count}");
        foreach (var line in lines)
        {
            GD.Print("  " + line);
        }
    }

    private static void CollectWide(Node node, float limit, List<string> lines)
    {
        if (node is Control control)
        {
            // A hidden subtree takes no width, so neither it nor its children can be an offender.
            if (!control.Visible)
            {
                return;
            }

            var minimum = control.GetCombinedMinimumSize().X;
            if (minimum > limit)
            {
                var text = control is Label label ? " text=" + label.Text : string.Empty;
                lines.Add($"{control.GetType().Name} minW={minimum:0} sizeW={control.Size.X:0}{text}");
            }
        }

        foreach (var child in node.GetChildren())
        {
            CollectWide(child, limit, lines);
        }
    }
}
