using Godot;

namespace STS2AIAgent.Ui;

/// <summary>
/// The overlay's show/hide hotkey: reading the setting, and when a press must be ignored.
/// </summary>
internal static class OverlayHotkey
{
    /// <summary>
    /// The key a hotkey setting names: F1-F12 or any Godot key name ("Insert", "Pause", "K").
    /// </summary>
    /// <remarks>
    /// A bare number used to slip through <c>Enum.TryParse</c>, which accepts the numeric value of
    /// any enum member: "0" became <c>Key.None</c> (a hotkey that can never fire) and other digits
    /// became arbitrary key codes. Numbers and <c>None</c> now fall back to F8 like any other typo.
    /// </remarks>
    public static bool TryParse(string? raw, out Key key)
    {
        var value = (raw ?? "F8").Trim().ToUpperInvariant();
        if (value.Length >= 2 && value[0] == 'F' && int.TryParse(value[1..], out var number) && number is >= 1 and <= 12)
        {
            key = Key.F1 + (number - 1);
            return true;
        }

        if (value.Length > 0
            && !char.IsDigit(value[0])
            && value[0] != '-'
            && Enum.TryParse(value, ignoreCase: true, out key)
            && Enum.IsDefined(key)
            && key != Key.None)
        {
            return true;
        }

        key = Key.F8;
        return false;
    }

    /// <summary>
    /// True for a key that types a character. Such a hotkey must not fire while a text box has focus,
    /// or typing the letter into the chat box toggles the window.
    /// </summary>
    public static bool IsTypingKey(Key key)
    {
        return key < Key.Special;
    }
}
