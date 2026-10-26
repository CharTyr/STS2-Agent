using Godot;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Ui;

/// <summary>
/// Where the overlay window sits: its size for the viewport, the drag on its title bar, the clamp that
/// keeps it on screen, the edge tab that follows it, and the saved position.
/// </summary>
/// <remarks>
/// Moved out of <c>AgentOverlayHost</c>, which owned these six methods and three fields beside
/// everything else. None of it reads a page or a setting other than the saved position, so it is a
/// class of its own rather than another partial file of the host.
/// </remarks>
internal sealed class OverlayPlacement
{
    public const int PanelWidth = 440;

    private readonly Control _host;
    private readonly Control _panel;
    private readonly Control _edgeTab;
    private readonly Control _pageHost;
    private bool _dragging;
    private Vector2 _lastViewportSize;

    public OverlayPlacement(Control host, Control panel, Control edgeTab, Control pageHost)
    {
        _host = host;
        _panel = panel;
        _edgeTab = edgeTab;
        _pageHost = pageHost;
    }

    /// <summary>
    /// Re-fits the window when the viewport changes size. Called every frame; does nothing otherwise.
    /// </summary>
    public void FollowViewport()
    {
        if (_host.Size != _lastViewportSize)
        {
            Apply(keepCurrent: _lastViewportSize != Vector2.Zero);
        }
    }

    /// <summary>
    /// Sizes the window for the viewport and puts it at the saved position, or keeps it where it is.
    /// </summary>
    public void Apply(bool keepCurrent = false)
    {
        if (!GodotObject.IsInstanceValid(_panel) || !_panel.IsInsideTree())
        {
            return;
        }

        var viewport = ReadViewportSize();
        if (viewport.X < 32 || viewport.Y < 32)
        {
            return;
        }

        var size = PanelSizeFor(viewport);
        Vector2 position;
        if (keepCurrent)
        {
            position = _panel.Position;
        }
        else
        {
            var settings = AgentRuntime.Instance.Settings;
            var defaultX = viewport.X - size.X - 12;
            var defaultY = 40f;
            position = new Vector2(settings.OverlayLeft ?? defaultX, settings.OverlayTop ?? defaultY);
        }

        _panel.Size = size;
        _panel.CustomMinimumSize = new Vector2(PanelWidth, Math.Min(size.Y, 360));
        // A ceiling on the content column, not just a floor on the panel. A Godot container expands to
        // fit its children's minimums, and one unwrapped sentence can report a minimum wider than the
        // panel -- measured live on 2026-09-21 at 691 against a 440 panel, which clipped every card
        // against the panel edge. Pinning the scroll body to the panel width makes over-wide content
        // reflow instead of pushing the layout out.
        _pageHost.CustomMinimumSize = new Vector2(size.X - 2 * UiFactory.SpaceMd, 0);

        Move(position);
        _lastViewportSize = viewport;
    }

    /// <summary>Forgets the saved position and puts the window back at its default spot.</summary>
    public void Reset()
    {
        AgentRuntime.Instance.PersistOverlayPlacement(null, null);
        _lastViewportSize = Vector2.Zero;
        Apply();
    }

    /// <summary>The title bar's input: press to grab, move to drag, release to save the position.</summary>
    public void OnDragInput(Control handle, InputEvent evt)
    {
        if (evt is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            _dragging = button.Pressed;
            handle.AcceptEvent();
            if (!button.Pressed)
            {
                Persist();
            }

            return;
        }

        if (evt is InputEventMouseMotion motion && _dragging)
        {
            Move(_panel.Position + motion.Relative);
            handle.AcceptEvent();
        }
    }

    private void Move(Vector2 position)
    {
        var viewport = ReadViewportSize();
        if (viewport.X < 32 || viewport.Y < 32)
        {
            return;
        }

        var size = _panel.Size;
        if (size.X < 32 || size.Y < 32)
        {
            size = PanelSizeFor(viewport);
            _panel.Size = size;
        }

        var maxX = Math.Max(8, viewport.X - size.X - 8);
        var maxY = Math.Max(8, viewport.Y - size.Y - 8);
        _panel.Position = new Vector2(
            Math.Clamp(position.X, 8, maxX),
            Math.Clamp(position.Y, 8, maxY));
        PlaceEdgeTab(viewport);
    }

    private void PlaceEdgeTab(Vector2 viewport)
    {
        var tabSize = _edgeTab.CustomMinimumSize;
        var onLeft = _panel.Position.X + _panel.Size.X * 0.5f < viewport.X * 0.5f;
        var y = Math.Clamp(_panel.Position.Y + 16, 8, Math.Max(8, viewport.Y - tabSize.Y - 8));
        _edgeTab.Position = onLeft
            ? new Vector2(4, y)
            : new Vector2(viewport.X - tabSize.X - 4, y);
    }

    private void Persist()
    {
        AgentRuntime.Instance.PersistOverlayPlacement(_panel.Position.X, _panel.Position.Y);
    }

    private Vector2 ReadViewportSize()
    {
        if (_host.Size.X >= 32 && _host.Size.Y >= 32)
        {
            return _host.Size;
        }

        return _host.IsInsideTree() ? _host.GetViewportRect().Size : Vector2.Zero;
    }

    private static Vector2 PanelSizeFor(Vector2 viewport)
    {
        var height = Math.Clamp(viewport.Y * 0.72f, 520f, Math.Max(520f, viewport.Y - 80f));
        // The 520 floor is for readability, not a reason to run off a short window: below about 540
        // pixels of viewport it pushed the footer (Save, Send) past the bottom edge.
        height = Math.Min(height, Math.Max(240f, viewport.Y - 16f));
        return new Vector2(PanelWidth, height);
    }
}
