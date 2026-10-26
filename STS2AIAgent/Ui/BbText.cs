using System.Text;

namespace STS2AIAgent.Ui;

/// <summary>
/// The BBCode the overlay's rich logs are written in: escaping and the two colour wrappers.
/// </summary>
/// <remarks>
/// Godot-free so the executable test project can pin the escaping. Everything a model or a player
/// typed goes through <see cref="Escape"/> before it reaches a <c>RichTextLabel</c>, so a reason that
/// contains <c>[b]</c> or <c>[url]</c> is shown as text instead of restyling the panel it is quoted on.
/// </remarks>
internal static class BbText
{
    /// <summary>
    /// The text with every square bracket replaced by Godot's bracket tags, so it renders as written.
    /// </summary>
    /// <remarks>
    /// One pass, character by character: replacing <c>[</c> first and <c>]</c> second would turn the
    /// <c>]</c> of the freshly inserted <c>[lb]</c> into <c>[rb]</c>. The earlier version swapped the
    /// brackets for full-width ones, which kept the markup inert but changed the text: a card named
    /// "Strike [+]" was shown as "Strike ［+］".
    /// </remarks>
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text.IndexOf('[') < 0 && text.IndexOf(']') < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + 16);
        foreach (var character in text)
        {
            switch (character)
            {
                case '[':
                    builder.Append("[lb]");
                    break;
                case ']':
                    builder.Append("[rb]");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Escaped text in one colour, given as a six-digit hex without the hash.</summary>
    public static string Colored(string hex, string? text, bool bold = false)
    {
        var body = Escape(text);
        if (bold)
        {
            body = "[b]" + body + "[/b]";
        }

        return "[color=#" + hex + "]" + body + "[/color]";
    }
}
