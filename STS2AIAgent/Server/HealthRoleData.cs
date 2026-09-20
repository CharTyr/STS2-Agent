namespace STS2AIAgent.Server;

internal sealed class HealthRoleData
{
    private HealthRoleData()
    {
    }

    public bool? companion_process_alive { get; init; }

    public bool? companion_process_exited { get; init; }

    public object? companion { get; init; }

    public string? dual_status { get; init; }

    public string? dual_launch_outcome { get; init; }

    public string? team_control_status { get; init; }

    public static HealthRoleData NotApplicable { get; } = new();

    public static HealthRoleData ForHost(
        bool companionProcessAlive,
        bool companionProcessExited,
        object? companion,
        string dualStatus,
        string? dualLaunchOutcome,
        string teamControlStatus)
    {
        return new HealthRoleData
        {
            companion_process_alive = companionProcessAlive,
            companion_process_exited = companionProcessExited,
            companion = companion,
            dual_status = dualStatus,
            dual_launch_outcome = dualLaunchOutcome,
            team_control_status = teamControlStatus
        };
    }
}
