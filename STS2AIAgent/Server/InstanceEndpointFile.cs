using System.Text.Json;
using STS2AIAgent.Config;

namespace STS2AIAgent.Server;

internal static class InstanceEndpointFile
{
    private static string PathForProcess => Path.Combine(Path.GetTempPath(), $"sts2-ai-agent-instance-{Environment.ProcessId}.json");

    public static void Publish(int port)
    {
        var path = PathForProcess;
        try
        {
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new
            {
                process_id = Environment.ProcessId, instance_role = InstanceRole.Current, api_port = port
            }));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static void Delete()
    {
        try { File.Delete(PathForProcess); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
