using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

internal static class HealthRoleDataTests
{
    public static void HostKeepsHostRuntimeState()
    {
        var companion = new { api_port = 8081, process_id = 123 };
        var data = HealthRoleData.ForHost(
            companionProcessAlive: true,
            companionProcessExited: false,
            companion,
            dualStatus: "AI 队友已连接。",
            dualLaunchOutcome: "Succeeded",
            teamControlStatus: "队友控制已连接。");

        Assert.Equal(true, data.companion_process_alive);
        Assert.Equal(false, data.companion_process_exited);
        Assert.True(ReferenceEquals(companion, data.companion));
        Assert.Equal("AI 队友已连接。", data.dual_status);
        Assert.Equal("Succeeded", data.dual_launch_outcome);
        Assert.Equal("队友控制已连接。", data.team_control_status);
    }

    public static void CompanionDoesNotInventHostRuntimeState()
    {
        var data = HealthRoleData.NotApplicable;

        Assert.Null(data.companion_process_alive);
        Assert.Null(data.companion_process_exited);
        Assert.Null(data.companion);
        Assert.Null(data.dual_status);
        Assert.Null(data.dual_launch_outcome);
        Assert.Null(data.team_control_status);
    }

    public static void RouterKeepsCommonFieldsOutsideRoleProjection()
    {
        var health = AgentSourceFixture.MethodBody(
            AgentSourceFixture.Read("STS2AIAgent/Server/Router.cs"),
            "BuildHealthData");

        foreach (var commonField in new[]
        {
            "service = ServiceName",
            "status = ReflectedGameMembers.ResolveStatus()",
            "api_port = HttpServer.Instance.Port",
            "process_id = Environment.ProcessId",
            "instance_role = InstanceRole.Current",
            "play_running = AgentRuntime.Instance.PlayRunning",
            "play_phase = AgentRuntime.Instance.PlayPhase",
            "session_requests = AgentRuntime.Instance.SessionRequests",
            "compatibility = ReflectedGameMembers.BuildHealthSection()",
            "state_build = StateBuildTiming.Instance.Snapshot()"
        })
        {
            Assert.Contains(commonField, health, StringComparison.Ordinal);
        }

        Assert.Contains("InstanceRole.IsCompanion", health, StringComparison.Ordinal);
        Assert.Contains("HealthRoleData.NotApplicable", health, StringComparison.Ordinal);
        Assert.Contains("HealthRoleData.ForHost(", health, StringComparison.Ordinal);
    }
}
