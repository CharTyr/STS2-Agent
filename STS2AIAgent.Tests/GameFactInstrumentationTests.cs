#if STS2_RUNTIME_TESTS
using STS2AIAgent.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace STS2AIAgent.Server
{
    internal sealed class GameEventService
    {
        public static GameEventService Instance { get; } = new();

        public void RecordDamageFact(DamageFact fact)
        {
        }

        public void RecordAiDecisionFact(AiDecisionFact fact)
        {
        }
    }
}

namespace STS2AIAgent.Tests
{
    internal static class GameFactInstrumentationTests
    {
        public static void Install_FindsActualDamageSinkAndEffectSources()
        {
            GameFactInstrumentation.SuppressLoggingForTests = true;
            try
            {
                GameFactInstrumentation.Install();
                var poisonType = typeof(AbstractModel).Assembly.GetType("MegaCrit.Sts2.Core.Models.Powers.PoisonPower");
                var trigger = poisonType?.GetMethod("Trigger");
                var stateMachineType = trigger?.GetCustomAttributes(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), false)
                    .OfType<System.Runtime.CompilerServices.AsyncStateMachineAttribute>()
                    .Single().StateMachineType;
                var moveNext = stateMachineType?.GetMethod(
                    "MoveNext",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(moveNext);
                Assert.True(Harmony.GetPatchInfo(moveNext!)?.Prefixes.Count > 0);
                GameFactInstrumentation.Uninstall();
            }
            finally
            {
                GameFactInstrumentation.SuppressLoggingForTests = false;
            }
        }

        public static void OptionalAiTelemetry_PatchesActualWorkshopAssemblyWhenProvided()
        {
            var assemblyPath = Environment.GetEnvironmentVariable("STS2_AI_TEAMMATE_DLL");
            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            {
                return;
            }

            OptionalAiTeammateTelemetry.SuppressLoggingForTests = true;
            try
            {
                System.Reflection.Assembly.LoadFrom(assemblyPath);
                OptionalAiTeammateTelemetry.Install();
                Assert.True(OptionalAiTeammateTelemetry.LastPatchedDecisionMethodCount > 0);
                OptionalAiTeammateTelemetry.Uninstall();
            }
            finally
            {
                OptionalAiTeammateTelemetry.SuppressLoggingForTests = false;
            }
        }
    }
}
#endif
