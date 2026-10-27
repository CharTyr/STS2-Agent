using Godot;
using System.Reflection;

namespace STS2AIAgent.Ui;

/// <summary>
/// The small logos shown next to each fast-judgment provider in the settings picker. They are
/// embedded in the mod DLL (<c>Assets/jev/&lt;preset id&gt;.png</c>) and decoded once on first use.
/// A missing or undecodable logo is simply no icon: the picker must never fail over decoration.
/// </summary>
internal static class JevProviderIcons
{
    private static readonly Dictionary<string, Texture2D?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Texture2D? Get(string presetId)
    {
        if (Cache.TryGetValue(presetId, out var cached)) return cached;
        Texture2D? texture = null;
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("STS2AIAgent.JevLogo." + presetId + ".png");
            if (stream != null)
            {
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var image = new Image();
                if (image.LoadPngFromBuffer(memory.ToArray()) == Error.Ok)
                    texture = ImageTexture.CreateFromImage(image);
            }
        }
        catch (Exception)
        {
            texture = null;
        }

        Cache[presetId] = texture;
        return texture;
    }
}
