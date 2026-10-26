using System.Diagnostics;

namespace STS2AIAgent.Multiplayer;

/// <summary>One wall-clock budget includes every probe and delay, including stalled HTTP peers.</summary>
internal static class CompanionDiscovery
{
    public static async Task<int?> WaitAsync(
        Func<IEnumerable<int>> candidates, Func<int, CancellationToken, Task<bool>> probe,
        Func<bool> exited, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (!exited())
            {
                foreach (var port in candidates())
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    if (await probe(port, deadline.Token)) return port;
                }
                await Task.Delay(250, deadline.Token);
            }
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return null;
        }
    }
}
