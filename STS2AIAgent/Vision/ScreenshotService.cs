using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace STS2AIAgent.Vision;

internal static class ScreenshotService
{
    public const int DefaultMaxEdge = 1280;

    internal static Action? BeginCapture;

    internal static Action? EndCapture;

    public static byte[]? CaptureJpeg(int maxEdge = DefaultMaxEdge, float quality = 0.72f)
    {
        var game = NGame.Instance;
        if (game == null || !GodotObject.IsInstanceValid(game))
        {
            return null;
        }

        var viewport = game.GetViewport();
        if (viewport == null || !GodotObject.IsInstanceValid(viewport))
        {
            return null;
        }

        var texture = viewport.GetTexture();
        if (texture == null)
        {
            return null;
        }

        var image = texture.GetImage();
        if (image == null || image.IsEmpty())
        {
            image?.Dispose();
            return null;
        }

        using (image)
        {
            var width = image.GetWidth();
            var height = image.GetHeight();
            var longest = Math.Max(width, height);
            if (longest > maxEdge && longest > 0)
            {
                var scale = maxEdge / (float)longest;
                var nextWidth = Math.Max(1, (int)Math.Round(width * scale));
                var nextHeight = Math.Max(1, (int)Math.Round(height * scale));
                image.Resize(nextWidth, nextHeight, Image.Interpolation.Lanczos);
            }

            return image.SaveJpgToBuffer(quality);
        }
    }
}
