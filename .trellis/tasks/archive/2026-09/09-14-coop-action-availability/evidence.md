# Goal 4 evidence — co-op availability vs structural guards

Date: 2026-09-15

## Change
Invite and continue are advertised only when CoopLaunchPolicy.GetStructuralError allows the host (not companion, not already autoplaying, main menu). Continue still also requires HasMultiplayerRunSave. Verified play model is not part of advertising, so external takeover remains listed.

## Files
- STS2AIAgent/Game/GameStateService.cs
- STS2AIAgent.Tests/ContinueCoopContractTests.cs
- STS2AIAgent.Tests/InviteCoopContractTests.cs
- STS2AIAgent.Tests/TestRunner.cs

## Commands
dotnet run --project STS2AIAgent.Tests/STS2AIAgent.Tests.csproj

## Results
ContinueCoop.Advertised and InviteCoop.Advertised PASS. Full C# runner exit 0.

## Limits
Source-contract tests, not a live overlay click. No Steam co-op launch.

