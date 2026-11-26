using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// Offline ECMA-335 metadata auditor that verifies a compiled <c>STS2AIAgent.dll</c>, the
/// <c>ReflectedGameMembers.cs</c> private-member registry, and <c>GameApiCompatibility</c>
/// capability groups against a target game assembly directory (<c>sts2.dll</c>, <c>GodotSharp.dll</c>,
/// <c>0Harmony.dll</c>) without launching the game.
/// </summary>
internal static class GameAbiAuditor
{
    private static readonly string[] TargetAssemblies = { "sts2", "GodotSharp", "0Harmony" };

    private static readonly Regex RegistryEntryPattern = new(
        @"new\(typeof\(([A-Za-z_][A-Za-z0-9_]*)\),\s*""([A-Za-z_][A-Za-z0-9_]*)"",\s*MemberKind\.(Field|Method|Property)",
        RegexOptions.Compiled);

    private static readonly HashSet<string> StandardObjectMethods = new(StringComparer.Ordinal)
    {
        "ToString", "Equals", "GetHashCode", "GetType", "Finalize", "MemberwiseClone", "ReferenceEquals"
    };

    private static readonly HashSet<string> StandardDelegateMethods = new(StringComparer.Ordinal)
    {
        ".ctor", "Invoke", "BeginInvoke", "EndInvoke", "Combine", "Remove", "GetInvocationList", "DynamicInvoke"
    };

    private static readonly HashSet<string> StandardExceptionMethods = new(StringComparer.Ordinal)
    {
        "get_Message", "get_InnerException", "get_StackTrace", "get_Source", "get_Data", "get_HResult", "GetBaseException"
    };

    internal sealed record AuditReport(
        string ModDll,
        string DataDir,
        int CheckedTypes,
        int CheckedMembers,
        int CheckedReflected,
        IReadOnlyList<string> MissingTypes,
        IReadOnlyList<string> MissingMembers,
        IReadOnlyList<string> MissingReflected,
        IReadOnlyList<CapabilityAudit> Capabilities)
    {
        public bool Ok =>
            MissingTypes.Count == 0
            && MissingMembers.Count == 0
            && MissingReflected.Count == 0
            && Capabilities.All(cap => !string.Equals(cap.Status, "missing", StringComparison.Ordinal));
    }

    internal sealed record CapabilityAudit(string Capability, string Status, string? Detail);

    internal sealed class TypeMetadata
    {
        public required string Assembly { get; init; }
        public required string FullName { get; init; }
        public required string SimpleName { get; init; }
        public string? BaseTypeAssembly { get; init; }
        public string? BaseTypeFullName { get; init; }
        public List<(string Assembly, string FullName)> Interfaces { get; } = new();
        public HashSet<(string Name, int ParamCount)> DeclaredMethods { get; } = new();
        public HashSet<string> DeclaredMethodNames { get; } = new(StringComparer.Ordinal);
        public HashSet<string> DeclaredFields { get; } = new(StringComparer.Ordinal);
        public HashSet<string> DeclaredProperties { get; } = new(StringComparer.Ordinal);
    }

    internal sealed class AssemblyIndex
    {
        public Dictionary<(string Assembly, string FullName), TypeMetadata> ByFullName { get; } = new();
        public Dictionary<string, List<TypeMetadata>> BySimpleName { get; } = new(StringComparer.Ordinal);
        public HashSet<string> LoadedAssemblies { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void LoadAssembly(string dllPath, string? overrideAssemblyName = null)
        {
            using var stream = File.OpenRead(dllPath);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            var asmName = overrideAssemblyName ?? reader.GetString(reader.GetAssemblyDefinition().Name);
            LoadedAssemblies.Add(asmName);

            var fullNames = new Dictionary<TypeDefinitionHandle, string>();
            foreach (var handle in reader.TypeDefinitions)
            {
                ResolveDefFullName(reader, handle, fullNames);
            }

            foreach (var handle in reader.TypeDefinitions)
            {
                var def = reader.GetTypeDefinition(handle);
                var simpleName = reader.GetString(def.Name);
                var fullName = fullNames[handle];
                var (baseAsm, baseFull) = ResolveTypeHandle(reader, def.BaseType, asmName, fullNames);
                var meta = new TypeMetadata
                {
                    Assembly = asmName,
                    FullName = fullName,
                    SimpleName = simpleName,
                    BaseTypeAssembly = baseAsm,
                    BaseTypeFullName = baseFull
                };

                foreach (var ifaceHandle in def.GetInterfaceImplementations())
                {
                    var iface = reader.GetInterfaceImplementation(ifaceHandle);
                    var resolved = ResolveTypeHandle(reader, iface.Interface, asmName, fullNames);
                    if (resolved.Assembly != null && resolved.FullName != null)
                    {
                        meta.Interfaces.Add((resolved.Assembly, resolved.FullName));
                    }
                }

                foreach (var methodHandle in def.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    var methodName = reader.GetString(method.Name);
                    var paramCount = ReadMethodParamCount(reader, method.Signature);
                    meta.DeclaredMethods.Add((methodName, paramCount));
                    meta.DeclaredMethodNames.Add(methodName);
                }

                foreach (var fieldHandle in def.GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    meta.DeclaredFields.Add(reader.GetString(field.Name));
                }

                foreach (var propHandle in def.GetProperties())
                {
                    var prop = reader.GetPropertyDefinition(propHandle);
                    meta.DeclaredProperties.Add(reader.GetString(prop.Name));
                }

                ByFullName[(asmName, fullName)] = meta;
                if (!BySimpleName.TryGetValue(simpleName, out var list))
                {
                    list = new List<TypeMetadata>();
                    BySimpleName[simpleName] = list;
                }

                list.Add(meta);
            }
        }

        public bool TryGetType(string assembly, string fullName, out TypeMetadata meta)
        {
            if (ByFullName.TryGetValue((assembly, fullName), out meta!))
            {
                return true;
            }

            // Fallback across loaded assemblies for forwarded types.
            foreach (var loaded in LoadedAssemblies)
            {
                if (ByFullName.TryGetValue((loaded, fullName), out meta!))
                {
                    return true;
                }
            }

            meta = null!;
            return false;
        }

        public bool HasMethodInHierarchy(string assembly, string fullName, string methodName, int paramCount)
        {
            if (!TryGetType(assembly, fullName, out var start))
            {
                return false;
            }

            var visited = new HashSet<(string, string)>();
            var queue = new Queue<TypeMetadata>();
            queue.Enqueue(start);
            var reachesExternalBase = false;
            var isDelegate = false;
            var isException = false;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add((current.Assembly, current.FullName)))
                {
                    continue;
                }

                if (current.DeclaredMethods.Contains((methodName, paramCount)))
                {
                    return true;
                }

                if (current.BaseTypeFullName != null)
                {
                    if (current.BaseTypeFullName is "System.MulticastDelegate" or "System.Delegate")
                    {
                        isDelegate = true;
                    }
                    else if (current.BaseTypeFullName.EndsWith("Exception", StringComparison.Ordinal))
                    {
                        isException = true;
                    }

                    if (current.BaseTypeAssembly != null && TryGetType(current.BaseTypeAssembly, current.BaseTypeFullName, out var baseMeta))
                    {
                        queue.Enqueue(baseMeta);
                    }
                    else
                    {
                        reachesExternalBase = true;
                    }
                }

                foreach (var iface in current.Interfaces)
                {
                    if (TryGetType(iface.Assembly, iface.FullName, out var ifaceMeta))
                    {
                        queue.Enqueue(ifaceMeta);
                    }
                    else
                    {
                        reachesExternalBase = true;
                    }
                }
            }

            if (StandardObjectMethods.Contains(methodName))
            {
                return true;
            }

            if (isDelegate && StandardDelegateMethods.Contains(methodName))
            {
                return true;
            }

            if (isException && StandardExceptionMethods.Contains(methodName))
            {
                return true;
            }

            // If the type inherits from an external BCL base/interface (e.g., List<T>, IReadOnlyList<T>,
            // Enum, Task), methods declared on that BCL base live outside the game assemblies.
            return reachesExternalBase && (methodName == ".ctor" || methodName.StartsWith("get_", StringComparison.Ordinal)
                || methodName.StartsWith("set_", StringComparison.Ordinal) || methodName == "GetEnumerator"
                || methodName == "Dispose" || methodName == "CompareTo");
        }

        public bool HasFieldInHierarchy(string assembly, string fullName, string fieldName)
        {
            if (!TryGetType(assembly, fullName, out var start))
            {
                return false;
            }

            var visited = new HashSet<(string, string)>();
            var current = start;
            while (current != null && visited.Add((current.Assembly, current.FullName)))
            {
                if (current.DeclaredFields.Contains(fieldName))
                {
                    return true;
                }

                if (current.BaseTypeAssembly == null || current.BaseTypeFullName == null
                    || !TryGetType(current.BaseTypeAssembly, current.BaseTypeFullName, out var next))
                {
                    break;
                }

                current = next;
            }

            return false;
        }
    }

    public static AuditReport Audit(
        string modDllPath,
        string dataDir,
        string? reflectedRegistrySource = null,
        IReadOnlyCollection<string>? targetAssemblies = null)
    {
        var targets = new HashSet<string>(targetAssemblies ?? TargetAssemblies, StringComparer.OrdinalIgnoreCase);
        var index = new AssemblyIndex();
        foreach (var asm in targets)
        {
            var path = Path.Combine(dataDir, asm + ".dll");
            if (File.Exists(path))
            {
                index.LoadAssembly(path);
            }
        }

        var missingTypes = new SortedSet<string>(StringComparer.Ordinal);
        var missingMembers = new SortedSet<string>(StringComparer.Ordinal);
        var checkedTypes = 0;
        var checkedMembers = 0;

        using (var stream = File.OpenRead(modDllPath))
        using (var pe = new PEReader(stream))
        {
            var reader = pe.GetMetadataReader();
            var refCache = new Dictionary<TypeReferenceHandle, (string? Assembly, string FullName)>();

            foreach (var handle in reader.TypeReferences)
            {
                var (asm, fullName) = ResolveTypeRef(reader, handle, refCache);
                if (asm == null || !targets.Contains(asm))
                {
                    continue;
                }

                checkedTypes++;
                if (!index.TryGetType(asm, fullName, out _))
                {
                    missingTypes.Add($"{asm}::{fullName}");
                }
            }

            foreach (var handle in reader.MemberReferences)
            {
                var member = reader.GetMemberReference(handle);
                var (asm, fullName) = ResolveMemberParent(reader, member.Parent, refCache);
                if (asm == null || fullName == null || !targets.Contains(asm))
                {
                    continue;
                }

                checkedMembers++;
                var memberName = reader.GetString(member.Name);
                var sigReader = reader.GetBlobReader(member.Signature);
                var header = sigReader.ReadSignatureHeader();
                if (header.Kind == SignatureKind.Method)
                {
                    if (header.IsGeneric)
                    {
                        sigReader.ReadCompressedInteger();
                    }

                    var paramCount = sigReader.ReadCompressedInteger();
                    if (!index.HasMethodInHierarchy(asm, fullName, memberName, paramCount))
                    {
                        missingMembers.Add($"{asm}::{fullName}.{memberName}({paramCount} args)");
                    }
                }
                else if (header.Kind == SignatureKind.Field)
                {
                    if (!index.HasFieldInHierarchy(asm, fullName, memberName))
                    {
                        missingMembers.Add($"{asm}::{fullName}.{memberName} (field)");
                    }
                }
            }
        }

        var missingReflected = new SortedSet<string>(StringComparer.Ordinal);
        var checkedReflected = 0;
        if (!string.IsNullOrWhiteSpace(reflectedRegistrySource))
        {
            foreach (Match match in RegistryEntryPattern.Matches(reflectedRegistrySource))
            {
                var typeName = match.Groups[1].Value;
                var memberName = match.Groups[2].Value;
                var kind = match.Groups[3].Value;
                checkedReflected++;

                if (!index.BySimpleName.TryGetValue(typeName, out var candidates) || candidates.Count == 0)
                {
                    missingReflected.Add($"{typeName}.{memberName} ({kind}: type not found)");
                    continue;
                }

                var found = candidates.Any(meta => kind switch
                {
                    "Field" => meta.DeclaredFields.Contains(memberName),
                    "Method" => meta.DeclaredMethodNames.Contains(memberName),
                    "Property" => meta.DeclaredProperties.Contains(memberName),
                    _ => false
                });
                if (!found)
                {
                    missingReflected.Add($"{typeName}.{memberName} ({kind}: member not declared on type)");
                }
            }

            // CardModel.UpgradeInternal (0 or 1 param) is probed alongside ReflectedGameMembers.
            if (index.LoadedAssemblies.Contains("sts2"))
            {
                checkedReflected++;
                var hasUpgrade = index.TryGetType("sts2", "MegaCrit.Sts2.Core.Models.CardModel", out var cardModel)
                    && (cardModel.DeclaredMethods.Contains(("UpgradeInternal", 0))
                        || cardModel.DeclaredMethods.Contains(("UpgradeInternal", 1)));
                if (!hasUpgrade)
                {
                    missingReflected.Add("CardModel.UpgradeInternal (Method: expected 0 or 1 parameter)");
                }
            }
        }

        var capabilities = index.LoadedAssemblies.Contains("sts2")
            ? AuditCapabilities(index)
            : Array.Empty<CapabilityAudit>();

        return new AuditReport(
            modDllPath,
            dataDir,
            checkedTypes,
            checkedMembers,
            checkedReflected,
            missingTypes.ToArray(),
            missingMembers.ToArray(),
            missingReflected.ToArray(),
            capabilities);
    }

    public static int RunCli(string[] args)
    {
        string? modDll = null;
        string? dataDir = null;
        var jsonMode = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--json")
            {
                jsonMode = true;
            }
            else if (modDll == null)
            {
                modDll = args[i];
            }
            else if (dataDir == null)
            {
                dataDir = args[i];
            }
        }

        if (string.IsNullOrWhiteSpace(modDll) || string.IsNullOrWhiteSpace(dataDir))
        {
            Console.Error.WriteLine("Usage: --check-abi <path/to/STS2AIAgent.dll> <path/to/game_data_dir> [--json]");
            return 2;
        }

        if (!File.Exists(modDll))
        {
            Console.Error.WriteLine($"Mod assembly not found: {modDll}");
            return 2;
        }

        if (!Directory.Exists(dataDir))
        {
            Console.Error.WriteLine($"Game data directory not found: {dataDir}");
            return 2;
        }

        string? registrySource = null;
        try
        {
            var registryPath = Path.Combine(AgentSourceFixture.Root, "STS2AIAgent", "Game", "ReflectedGameMembers.cs");
            if (File.Exists(registryPath))
            {
                registrySource = File.ReadAllText(registryPath);
            }
        }
        catch (Exception)
        {
            // Running outside a repo checkout still audits compiled ABI and capabilities.
        }

        var report = Audit(modDll, dataDir, registrySource);
        if (jsonMode)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"[game-abi] Mod: {report.ModDll}");
            Console.WriteLine($"[game-abi] DataDir: {report.DataDir}");
            Console.WriteLine($"[game-abi] Checked: {report.CheckedTypes} type ref(s), {report.CheckedMembers} member ref(s), {report.CheckedReflected} reflected entry/entries");
            foreach (var cap in report.Capabilities)
            {
                Console.WriteLine($"[game-abi] Capability {cap.Capability}: {cap.Status}" + (cap.Detail != null ? $" ({cap.Detail})" : string.Empty));
            }

            foreach (var item in report.MissingTypes)
            {
                Console.Error.WriteLine($"[game-abi] MISSING TYPE: {item}");
            }

            foreach (var item in report.MissingMembers)
            {
                Console.Error.WriteLine($"[game-abi] MISSING MEMBER: {item}");
            }

            foreach (var item in report.MissingReflected)
            {
                Console.Error.WriteLine($"[game-abi] MISSING REFLECTED: {item}");
            }

            Console.WriteLine(report.Ok ? "[game-abi] PASS: all ABI references, reflected members, and capabilities resolved." : "[game-abi] FAIL: compatibility mismatches detected.");
        }

        return report.Ok ? 0 : 1;
    }

    private static IReadOnlyList<CapabilityAudit> AuditCapabilities(AssemblyIndex index)
    {
        bool HasProp(string fullType, string prop) =>
            index.TryGetType("sts2", fullType, out var meta) && meta.DeclaredProperties.Contains(prop);
        bool HasField(string fullType, string field) =>
            index.TryGetType("sts2", fullType, out var meta) && meta.DeclaredFields.Contains(field);
        bool HasMethod(string fullType, string method, int? arity = null) =>
            index.TryGetType("sts2", fullType, out var meta)
            && (arity is { } n ? meta.DeclaredMethods.Contains((method, n)) : meta.DeclaredMethodNames.Contains(method));
        bool HasType(string fullType) => index.TryGetType("sts2", fullType, out _);

        var merchant = HasProp("MegaCrit.Sts2.Core.Runs.RunManager", "MerchantSynchronizer")
            && HasMethod("MegaCrit.Sts2.Core.Multiplayer.Game.MerchantSynchronizer", "PurchaseLocalEntry", 1)
            ? new CapabilityAudit("merchant_purchase", "available", "MerchantSynchronizer.PurchaseLocalEntry")
            : HasMethod("MegaCrit.Sts2.Core.Entities.Merchant.MerchantEntry", "OnTryPurchaseWrapper", 2)
                ? new CapabilityAudit("merchant_purchase", "fallback", "MerchantEntry.OnTryPurchaseWrapper")
                : new CapabilityAudit("merchant_purchase", "missing", "Neither MerchantSynchronizer.PurchaseLocalEntry nor LegacyPurchase found");

        var rewards = HasMethod("MegaCrit.Sts2.Core.Runs.RunManager", "ProceedFromTerminalRewardsScreen", 0)
            ? new CapabilityAudit("terminal_rewards_proceed", "available", null)
            : new CapabilityAudit("terminal_rewards_proceed", "missing", "RunManager.ProceedFromTerminalRewardsScreen not found");

        var ascension = (HasField("MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer", "maxStandardAscension")
                || HasField("MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer", "maxMultiplayerAscensionUnlocked"))
            && (HasMethod("MegaCrit.Sts2.Core.Helpers.AscensionHelper", "GetTitle", 2)
                || HasMethod("MegaCrit.Sts2.Core.Helpers.AscensionHelper", "GetTitle", 1))
            ? new CapabilityAudit("ascension_metadata", "available", null)
            : new CapabilityAudit("ascension_metadata", "missing", "StartRunLobbyPlayer ascension field or AscensionHelper.GetTitle missing");

        var hasArrowType = HasType("MegaCrit.Sts2.Core.Entities.FoolArrows.ArrowSlot");
        var arrows = !hasArrowType
            ? new CapabilityAudit("fool_arrows", "not_in_branch", null)
            : HasProp("MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState", "Arrows")
                && HasProp("MegaCrit.Sts2.Core.Entities.FoolArrows.ArrowSlot", "Status")
                && HasProp("MegaCrit.Sts2.Core.Entities.FoolArrows.ArrowSlot", "IsAvailable")
                && HasMethod("MegaCrit.Sts2.Core.Commands.ArrowCmd", "GetAvailableArrowCount", 1)
                ? new CapabilityAudit("fool_arrows", "available", null)
                : new CapabilityAudit("fool_arrows", "missing", "ArrowSlot present but required arrow members missing");

        var hasMelody = HasType("MegaCrit.Sts2.Core.Commands.MelodyCmd");
        var melody = !hasMelody
            ? new CapabilityAudit("fool_melody", "not_in_branch", null)
            : HasMethod("MegaCrit.Sts2.Core.Commands.MelodyCmd", "PlayedWahThisTurn", 1)
                && HasMethod("MegaCrit.Sts2.Core.Commands.MelodyCmd", "PlayedChoThisTurn", 1)
                && HasMethod("MegaCrit.Sts2.Core.Commands.MelodyCmd", "PlayedKiThisTurn", 1)
                ? new CapabilityAudit("fool_melody", "available", null)
                : new CapabilityAudit("fool_melody", "missing", "MelodyCmd present but Played*ThisTurn methods missing");

        var hasBlitz = HasType("MegaCrit.Sts2.Core.Nodes.Screens.Blitz.NBlitzScreen");
        var hasDread = HasType("MegaCrit.Sts2.Core.Nodes.Screens.Dread.NDreadScreen");
        var blitzDread = !hasBlitz && !hasDread
            ? new CapabilityAudit("blitz_dread_modes", "not_in_branch", null)
            : hasBlitz && hasDread
                && HasProp("MegaCrit.Sts2.Core.Nodes.Screens.Blitz.NBlitzScreen", "Lobby")
                && HasProp("MegaCrit.Sts2.Core.Nodes.Screens.Dread.NDreadScreen", "Lobby")
                && HasProp("MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory", "IsBlitzShop")
                && HasProp("MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory", "BlitzChoicesRemaining")
                && HasProp("MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory", "BlitzEntries")
                ? new CapabilityAudit("blitz_dread_modes", "available", null)
                : new CapabilityAudit("blitz_dread_modes", "missing", "Blitz/Dread screens partially present but lobby or Blitz shop properties missing");

        var preBlock = HasMethod("MegaCrit.Sts2.Core.Hooks.Hook", "ModifyPreBlockDamageTarget", 5)
            ? new CapabilityAudit("pre_block_damage_redirect", "available", null)
            : new CapabilityAudit("pre_block_damage_redirect", "not_in_branch", null);

        return [merchant, rewards, ascension, arrows, melody, blitzDread, preBlock];
    }

    private static string ResolveDefFullName(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        Dictionary<TypeDefinitionHandle, string> cache)
    {
        if (cache.TryGetValue(handle, out var cached))
        {
            return cached;
        }

        var def = reader.GetTypeDefinition(handle);
        var name = reader.GetString(def.Name);
        var enclosing = def.GetDeclaringType();
        string full;
        if (!enclosing.IsNil)
        {
            full = ResolveDefFullName(reader, enclosing, cache) + "+" + name;
        }
        else
        {
            var ns = reader.GetString(def.Namespace);
            full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        cache[handle] = full;
        return full;
    }

    private static (string? Assembly, string? FullName) ResolveTypeHandle(
        MetadataReader reader,
        EntityHandle handle,
        string currentAssembly,
        Dictionary<TypeDefinitionHandle, string> defFullNames)
    {
        if (handle.IsNil)
        {
            return (null, null);
        }

        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return (currentAssembly, ResolveDefFullName(reader, (TypeDefinitionHandle)handle, defFullNames));
            case HandleKind.TypeReference:
            {
                var refCache = new Dictionary<TypeReferenceHandle, (string?, string)>();
                var resolved = ResolveTypeRef(reader, (TypeReferenceHandle)handle, refCache);
                return (resolved.Assembly, resolved.FullName);
            }
            case HandleKind.TypeSpecification:
            {
                var spec = reader.GetTypeSpecification((TypeSpecificationHandle)handle);
                var blob = reader.GetBlobReader(spec.Signature);
                return UnwrapGenericSpec(reader, blob, currentAssembly, defFullNames, null);
            }
            default:
                return (null, null);
        }
    }

    private static (string? Assembly, string FullName) ResolveTypeRef(
        MetadataReader reader,
        TypeReferenceHandle handle,
        Dictionary<TypeReferenceHandle, (string? Assembly, string FullName)> cache)
    {
        if (cache.TryGetValue(handle, out var cached))
        {
            return cached;
        }

        var tr = reader.GetTypeReference(handle);
        var name = reader.GetString(tr.Name);
        var ns = reader.GetString(tr.Namespace);
        var scope = tr.ResolutionScope;
        (string? Assembly, string FullName) result;
        if (scope.Kind == HandleKind.TypeReference)
        {
            var parent = ResolveTypeRef(reader, (TypeReferenceHandle)scope, cache);
            result = (parent.Assembly, parent.FullName + "+" + name);
        }
        else if (scope.Kind == HandleKind.AssemblyReference)
        {
            var asm = reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
            result = (asm, string.IsNullOrEmpty(ns) ? name : ns + "." + name);
        }
        else
        {
            result = (null, string.IsNullOrEmpty(ns) ? name : ns + "." + name);
        }

        cache[handle] = result;
        return result;
    }

    private static (string? Assembly, string? FullName) ResolveMemberParent(
        MetadataReader reader,
        EntityHandle parent,
        Dictionary<TypeReferenceHandle, (string? Assembly, string FullName)> refCache)
    {
        if (parent.Kind == HandleKind.TypeReference)
        {
            var resolved = ResolveTypeRef(reader, (TypeReferenceHandle)parent, refCache);
            return (resolved.Assembly, resolved.FullName);
        }

        if (parent.Kind == HandleKind.TypeSpecification)
        {
            var spec = reader.GetTypeSpecification((TypeSpecificationHandle)parent);
            var blob = reader.GetBlobReader(spec.Signature);
            return UnwrapGenericSpec(reader, blob, string.Empty, null, refCache);
        }

        return (null, null);
    }

    private static (string? Assembly, string? FullName) UnwrapGenericSpec(
        MetadataReader reader,
        BlobReader blob,
        string currentAssembly,
        Dictionary<TypeDefinitionHandle, string>? defFullNames,
        Dictionary<TypeReferenceHandle, (string? Assembly, string FullName)>? refCache)
    {
        if (blob.RemainingBytes < 3)
        {
            return (null, null);
        }

        var code = blob.ReadByte();
        if (code != 0x15) // ELEMENT_TYPE_GENERICINST
        {
            return (null, null);
        }

        var kind = blob.ReadByte();
        if (kind is not (0x11 or 0x12)) // VALUETYPE or CLASS
        {
            return (null, null);
        }

        var typeHandle = blob.ReadTypeHandle();
        if (typeHandle.Kind == HandleKind.TypeReference)
        {
            var cache = refCache ?? new Dictionary<TypeReferenceHandle, (string?, string)>();
            var resolved = ResolveTypeRef(reader, (TypeReferenceHandle)typeHandle, cache);
            return (resolved.Assembly, resolved.FullName);
        }

        if (typeHandle.Kind == HandleKind.TypeDefinition && defFullNames != null)
        {
            return (currentAssembly, ResolveDefFullName(reader, (TypeDefinitionHandle)typeHandle, defFullNames));
        }

        return (null, null);
    }

    private static int ReadMethodParamCount(MetadataReader reader, BlobHandle signatureHandle)
    {
        var sig = reader.GetBlobReader(signatureHandle);
        var header = sig.ReadSignatureHeader();
        if (header.IsGeneric)
        {
            sig.ReadCompressedInteger();
        }

        return sig.ReadCompressedInteger();
    }
}
