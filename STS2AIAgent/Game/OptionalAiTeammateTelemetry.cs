using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Server;

namespace STS2AIAgent.Game;

/// <summary>
/// Optional, dependency-free adapter for the workshop AI teammate. The mod already retains its selected action,
/// concrete card/target and decision reason in AiDecisionRequest/AiDecisionResult, but exposes no public API.
/// Reflection keeps STS2AIAgent usable when that optional assembly is absent.
/// </summary>
internal static class OptionalAiTeammateTelemetry
{
    private const string HarmonyId = "com.chart.sts2-ai-agent.optional-ai-teammate-telemetry";
    private const string AssemblyName = "sts2AITeammate";
    private const string LogPrefix = "[STS2AIAgent.OptionalAiTeammateTelemetry]";
    private const int MaximumRememberedRequests = 2048;

    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<string, byte> EmittedRequestIds = new(StringComparer.Ordinal);
    private static Harmony? _harmony;
    private static bool _installed;

    internal static bool SuppressLoggingForTests { get; set; }

    internal static int LastPatchedDecisionMethodCount { get; private set; }

    public static void Install()
    {
        lock (Gate)
        {
            if (_installed)
            {
                return;
            }

            _installed = true;
            _harmony = new Harmony(HarmonyId);
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                TryPatchAssembly(assembly);
            }
        }
    }

    public static void Uninstall()
    {
        lock (Gate)
        {
            if (!_installed)
            {
                return;
            }

            _installed = false;
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
            EmittedRequestIds.Clear();
            LastPatchedDecisionMethodCount = 0;
        }
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args) => TryPatchAssembly(args.LoadedAssembly);

    private static void TryPatchAssembly(Assembly assembly)
    {
        if (!_installed || !string.Equals(assembly.GetName().Name, AssemblyName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var harmony = _harmony;
        if (harmony == null)
        {
            return;
        }

        var patched = 0;
        foreach (var type in SafeGetTypes(assembly))
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var method in type.GetMethods(flags).Where(IsDecisionMethod))
            {
                if (Harmony.GetPatchInfo(method)?.Postfixes.Any(patch => patch.owner == HarmonyId) == true)
                {
                    continue;
                }

                try
                {
                    harmony.Patch(
                        method,
                        postfix: new HarmonyMethod(typeof(OptionalAiTeammateTelemetry), nameof(DecisionPostfix)));
                    patched += 1;
                }
                catch (Exception ex)
                {
                    Warn($"Could not patch {method.FullDescription()}: {ex.Message}");
                }
            }
        }

        LastPatchedDecisionMethodCount += patched;
        Info($"Patched {patched} decision backend method(s) in {assembly.GetName().Name} {assembly.GetName().Version}.");
    }

    private static bool IsDecisionMethod(MethodInfo method)
    {
        if (!string.Equals(method.Name, "DecideAsync", StringComparison.Ordinal) || method.GetParameters().Length != 2)
        {
            return false;
        }

        var returnType = method.ReturnType;
        return returnType.IsGenericType &&
               returnType.GetGenericTypeDefinition() == typeof(Task<>) &&
               string.Equals(returnType.GetGenericArguments()[0].Name, "AiDecisionResult", StringComparison.Ordinal) &&
               string.Equals(method.GetParameters()[0].ParameterType.Name, "AiDecisionRequest", StringComparison.Ordinal);
    }

    private static void DecisionPostfix(object __0, Task __result)
    {
        _ = CaptureDecisionAsync(__0, __result);
    }

    private static async Task CaptureDecisionAsync(object request, Task resultTask)
    {
        try
        {
            await resultTask.ConfigureAwait(false);
            var result = resultTask.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(resultTask);
            if (result == null || !AiDecisionFactParser.TryParse(request, result, out var fact))
            {
                return;
            }

            if (!EmittedRequestIds.TryAdd(fact.RequestId, 0))
            {
                return;
            }

            if (EmittedRequestIds.Count > MaximumRememberedRequests)
            {
                EmittedRequestIds.Clear();
                EmittedRequestIds.TryAdd(fact.RequestId, 0);
            }

            GameEventService.Instance.RecordAiDecisionFact(fact);
        }
        catch (Exception ex)
        {
            Warn($"Failed to capture an optional AI decision: {ex.Message}");
        }
    }

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
}

internal static class AiDecisionFactParser
{
    public static bool TryParse(object request, object result, out AiDecisionFact fact)
    {
        var requestId = ReadString(request, "RequestId");
        var chosenActionId = ReadString(result, "ChosenActionId");
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(chosenActionId))
        {
            fact = default!;
            return false;
        }

        var option = ReadEnumerable(request, "LegalActions")
            .FirstOrDefault(candidate => string.Equals(ReadString(candidate, "ActionId"), chosenActionId, StringComparison.Ordinal));
        fact = new AiDecisionFact(
            requestId,
            ReadString(request, "SnapshotId"),
            ReadString(request, "ActorId"),
            chosenActionId,
            ReadStrings(result, "RankedActionIds"),
            ReadString(result, "Reason") ?? ReadString(result, "Reasoning"),
            option == null ? null : new AiDecisionOptionFact(
                ReadString(option, "ActionId") ?? chosenActionId,
                ReadString(option, "ActionType"),
                ReadString(option, "Description"),
                ReadString(option, "Label"),
                ReadString(option, "Summary"),
                ReadString(option, "CardId"),
                ReadString(option, "CardInstanceId"),
                ReadString(option, "TargetId"),
                ReadString(option, "TargetLabel"),
                ReadNullableInt(option, "EnergyCost"),
                ReadStrings(option, "PriorityTags"),
                ReadStringDictionary(option, "Metadata")));
        return true;
    }

    private static string? ReadString(object target, string propertyName) =>
        target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(target)?.ToString();

    private static int? ReadNullableInt(object target, string propertyName)
    {
        var value = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(target);
        return value == null ? null : Convert.ToInt32(value);
    }

    private static object[] ReadEnumerable(object target, string propertyName)
    {
        var value = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(target);
        return value is IEnumerable enumerable ? enumerable.Cast<object>().ToArray() : Array.Empty<object>();
    }

    private static string[] ReadStrings(object target, string propertyName) =>
        ReadEnumerable(target, propertyName).Select(static value => value.ToString() ?? string.Empty).ToArray();

    private static IReadOnlyDictionary<string, string> ReadStringDictionary(object target, string propertyName)
    {
        var value = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(target);
        if (value is not IDictionary dictionary)
        {
            return new Dictionary<string, string>();
        }

        return dictionary.Keys.Cast<object>()
            .Where(key => key != null)
            .ToDictionary(key => key.ToString() ?? string.Empty, key => dictionary[key]?.ToString() ?? string.Empty, StringComparer.Ordinal);
    }
}

internal sealed record AiDecisionFact(
    string RequestId,
    string? SnapshotId,
    string? ActorId,
    string ChosenActionId,
    string[] RankedActionIds,
    string? Reason,
    AiDecisionOptionFact? Option);

internal sealed record AiDecisionOptionFact(
    string ActionId,
    string? ActionType,
    string? Description,
    string? Label,
    string? Summary,
    string? CardId,
    string? CardInstanceId,
    string? TargetId,
    string? TargetLabel,
    int? EnergyCost,
    string[] PriorityTags,
    IReadOnlyDictionary<string, string> Metadata);
