# CS1998 invite/continue

ExecuteInviteAiTeammateAsync and ExecuteContinueAiTeammateAsync are now Task-returning methods without the async keyword. Pending still uses ObserveBackgroundTask plus Task.FromResult.

dotnet build STS2AIAgent/STS2AIAgent.csproj -c Release: succeeded, no CS1998.
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj: exit 0, including InviteCoop.NoAsyncWithoutAwait and ContinueCoop.NoAsyncWithoutAwait.
