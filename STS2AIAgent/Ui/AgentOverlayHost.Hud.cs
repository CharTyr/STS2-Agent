using Godot;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Ui;

internal sealed partial class AgentOverlayHost
{
    private CanvasLayer? _hudLayer;
    private AgentHud? _hud;
    private bool _capturePanelWasVisible;
    private bool _captureEdgeWasVisible;

    private void InstallHud(Window root)
    {
        _hudLayer = new CanvasLayer { Name = "STS2AgentIntentLayer", Layer = 120, ProcessMode = Node.ProcessModeEnum.Always };
        _hud = new AgentHud();
        _hud.Theme = _host?.Theme;
        _hudLayer.AddChild(_hud);
        root.CallDeferred(Node.MethodName.AddChild, _hudLayer);
    }

    private void RefreshHud()
    {
        if (_hud != null && GodotObject.IsInstanceValid(_hud) && _hud.IsInsideTree())
            _hud.Refresh(AgentRuntime.Instance, _captureHidden);
    }

    private void RemoveHud()
    {
        _hudLayer?.QueueFree();
        _hudLayer = null;
        _hud = null;
    }
}
