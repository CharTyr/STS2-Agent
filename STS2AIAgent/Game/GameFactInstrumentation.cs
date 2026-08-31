using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2AIAgent.Server;

namespace STS2AIAgent.Game;

/// <summary>
/// Captures damage at the game's authoritative settlement command. Snapshot polling cannot represent
/// multi-hit or rapid consecutive actions without collapsing them, and CombatHistory intentionally drops
/// CardPlay from DamageReceivedEntry. The command arguments and DamageResult values retain both.
/// </summary>
internal static class GameFactInstrumentation
{
    private const string HarmonyId = "com.chart.sts2-ai-agent.fact-events";
    private const string LogPrefix = "[STS2AIAgent.GameFactInstrumentation]";

    private static readonly AsyncLocal<AbstractModel?> ActiveEffectSource = new();
    private static Harmony? _harmony;

    internal static bool SuppressLoggingForTests { get; set; }

    public static void Install()
    {
        if (_harmony != null)
        {
            return;
        }

        var harmony = new Harmony(HarmonyId);
        var damageMethod = typeof(CreatureCmd).GetMethod(
            nameof(CreatureCmd.Damage),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types:
            [
                typeof(PlayerChoiceContext),
                typeof(IEnumerable<Creature>),
                typeof(decimal),
                typeof(ValueProp),
                typeof(Creature),
                typeof(CardModel),
                typeof(CardPlay)
            ],
            modifiers: null)
            ?? throw new MissingMethodException("Could not find the authoritative CreatureCmd.Damage overload.");

        harmony.Patch(
            damageMethod,
            postfix: new HarmonyMethod(typeof(GameFactInstrumentation), nameof(DamagePostfix)));

        var sourcePatchCount = PatchEffectSourceCallers(harmony);
        _harmony = harmony;
        Info($"Installed damage settlement patch and {sourcePatchCount} exact effect-source patches.");
    }

    public static void Uninstall()
    {
        var harmony = Interlocked.Exchange(ref _harmony, null);
        harmony?.UnpatchAll(HarmonyId);
        ActiveEffectSource.Value = null;
    }

    private static int PatchEffectSourceCallers(Harmony harmony)
    {
        var patched = new HashSet<MethodBase>();
        var modelAssembly = typeof(AbstractModel).Assembly;
        foreach (var modelType in SafeGetTypes(modelAssembly)
                     .Where(type => typeof(AbstractModel).IsAssignableFrom(type)))
        {
            foreach (var method in EnumerateSourceCandidateMethods(modelType))
            {
                var executionMethod = ResolveExecutionMethod(method);
                if (executionMethod == null || executionMethod.IsStatic || !CallsDamage(executionMethod) || !patched.Add(executionMethod))
                {
                    continue;
                }

                try
                {
                    harmony.Patch(
                        executionMethod,
                        prefix: new HarmonyMethod(typeof(GameFactInstrumentation), nameof(EffectSourcePrefix)),
                        postfix: new HarmonyMethod(typeof(GameFactInstrumentation), nameof(EffectSourcePostfix)),
                        finalizer: new HarmonyMethod(typeof(GameFactInstrumentation), nameof(EffectSourceFinalizer)));
                }
                catch (Exception ex)
                {
                    patched.Remove(executionMethod);
                    Warn($"Could not instrument source method {executionMethod.FullDescription()}: {ex.Message}");
                }
            }
        }

        return patched.Count;
    }

    private static IEnumerable<MethodInfo> EnumerateSourceCandidateMethods(Type modelType)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var method in modelType.GetMethods(flags))
        {
            yield return method;
        }

        foreach (var nested in EnumerateNestedTypes(modelType))
        {
            foreach (var method in nested.GetMethods(flags))
            {
                yield return method;
            }
        }
    }

    private static IEnumerable<Type> EnumerateNestedTypes(Type type)
    {
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            yield return nested;
            foreach (var descendant in EnumerateNestedTypes(nested))
            {
                yield return descendant;
            }
        }
    }

    private static MethodInfo? ResolveExecutionMethod(MethodInfo method)
    {
        var asyncStateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        if (asyncStateMachine != null)
        {
            return asyncStateMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        return method;
    }

    private static bool CallsDamage(MethodBase method)
    {
        try
        {
            return PatchProcessor.GetOriginalInstructions(method).Any(instruction =>
                instruction.operand is MethodInfo called &&
                called.DeclaringType == typeof(CreatureCmd) &&
                string.Equals(called.Name, nameof(CreatureCmd.Damage), StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private static Type[] SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>().ToArray();
        }
    }

    private static void EffectSourcePrefix(object __instance, out EffectSourceScope? __state)
    {
        var model = FindOwningModel(__instance, depth: 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
        __state = model == null ? null : new EffectSourceScope(model);
    }

    private static void EffectSourcePostfix(EffectSourceScope? __state) => __state?.Dispose();

    private static Exception? EffectSourceFinalizer(Exception? __exception, EffectSourceScope? __state)
    {
        __state?.Dispose();
        return __exception;
    }

    private static AbstractModel? FindOwningModel(object? value, int depth, HashSet<object> visited)
    {
        if (value is AbstractModel model)
        {
            return model;
        }

        if (value == null || depth >= 3 || !visited.Add(value))
        {
            return null;
        }

        var type = value.GetType();
        if (!type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) && depth > 0)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var field in type.GetFields(flags))
        {
            if (typeof(AbstractModel).IsAssignableFrom(field.FieldType))
            {
                return field.GetValue(value) as AbstractModel;
            }
        }

        foreach (var field in type.GetFields(flags).Where(field =>
                     field.FieldType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) ||
                     field.Name.Contains("this", StringComparison.Ordinal)))
        {
            var nested = FindOwningModel(field.GetValue(value), depth + 1, visited);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void DamagePostfix(
        PlayerChoiceContext choiceContext,
        IEnumerable<Creature>? targets,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        // Do not infer a source from the choice context: it can retain the card that triggered a
        // later poison/power callback. A live model callsite or an explicit card argument is exact.
        var effectSource = cardPlay != null
            ? cardSource
            : ActiveEffectSource.Value ?? cardSource;
        __result = CaptureDamageAsync(__result, amount, props, dealer, cardSource, cardPlay, effectSource);
    }

    private static async Task<IEnumerable<DamageResult>> CaptureDamageAsync(
        Task<IEnumerable<DamageResult>> task,
        decimal requestedAmount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay,
        AbstractModel? effectSource)
    {
        var results = (await task).ToArray();
        GameEventService.Instance.RecordDamageFact(new DamageFact(
            requestedAmount,
            GetValueProps(props),
            dealer,
            cardSource,
            cardPlay,
            effectSource,
            results));
        return results;
    }

    private static string[] GetValueProps(ValueProp props) =>
        Enum.GetValues<ValueProp>()
            .Where(value => props.HasFlag(value))
            .Select(static value => value.ToString().ToLowerInvariant())
            .ToArray();

    private static void Info(string message)
    {
        if (!SuppressLoggingForTests)
        {
            Log.Info($"{LogPrefix} {message}");
        }
    }

    private static void Warn(string message)
    {
        if (!SuppressLoggingForTests)
        {
            Log.Warn($"{LogPrefix} {message}");
        }
    }

    private sealed class EffectSourceScope : IDisposable
    {
        private readonly AbstractModel? _previous;
        private int _disposed;

        public EffectSourceScope(AbstractModel source)
        {
            _previous = ActiveEffectSource.Value;
            ActiveEffectSource.Value = source;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ActiveEffectSource.Value = _previous;
            }
        }
    }
}

internal sealed record DamageFact(
    decimal RequestedAmount,
    string[] Props,
    Creature? Dealer,
    CardModel? CardSource,
    CardPlay? CardPlay,
    AbstractModel? EffectSource,
    IReadOnlyList<DamageResult> Results);
