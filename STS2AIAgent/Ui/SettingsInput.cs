using System.Globalization;

namespace STS2AIAgent.Ui;

/// <summary>
/// Reading the settings form's free-text numbers, independent of the player's system locale.
/// </summary>
/// <remarks>
/// Godot-free so the executable test project can pin it. The Jev threshold used to go through
/// <c>double.TryParse</c> with the current culture: on a German or French Windows, "0.35" typed into
/// the box read as 35 (the dot is a thousands separator there), every Jev confidence fell below it,
/// and dual-layer mode silently handed every turn back to the LLM.
/// </remarks>
internal static class SettingsInput
{
    /// <summary>
    /// A confidence threshold between 0 and 1. Accepts a dot or a comma as the decimal mark; anything
    /// else, or a value outside the range, is rejected so the caller keeps the previous value.
    /// </summary>
    public static bool TryParseThreshold(string? text, out double value)
    {
        value = 0;
        var trimmed = (text ?? string.Empty).Trim().Replace(',', '.');
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || double.IsNaN(parsed)
            || parsed < 0
            || parsed > 1)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>The threshold as the form shows it: two decimals, always with a dot.</summary>
    public static string FormatThreshold(double value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
