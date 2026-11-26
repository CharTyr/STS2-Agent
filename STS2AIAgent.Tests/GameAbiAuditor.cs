using System.Collections.Immutable;
using System.Reflection;
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
    private const int MinimumReflectedEntries = 15;

    private static readonly string[] TargetAssemblies = { "sts2", "GodotSharp", "0Harmony" };

    private static readonly Regex RegistryEntryPattern = new(
        @"new\(typeof\(([A-Za-z_][A-Za-z0-9_]*)\),\s*""([A-Za-z_][A-Za-z0-9_]*)"",\s*MemberKind\.(Field|Method|Property),\s*(true|false)",
        RegexOptions.Compiled);

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

    internal sealed record MethodMetadata(
        string Name,
        bool IsStatic,
        int GenericArity,
        string ReturnType,
        string[] ParamTypes,
        MethodAttributes Attributes)
    {
        public bool IsPublic => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;

        public bool IsAccessibleFrom(bool allowProtected)
        {
            var access = Attributes & MethodAttributes.MemberAccessMask;
            return access == MethodAttributes.Public
                || (allowProtected && access is MethodAttributes.Family or MethodAttributes.FamORAssem);
        }
    }

    internal sealed record FieldMetadata(
        string Name,
        bool IsStatic,
        string FieldType,
        FieldAttributes Attributes)
    {
        public bool IsPublic => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public;

        public bool IsAccessibleFrom(bool allowProtected)
        {
            var access = Attributes & FieldAttributes.FieldAccessMask;
            return access == FieldAttributes.Public
                || (allowProtected && access is FieldAttributes.Family or FieldAttributes.FamORAssem);
        }
    }

    internal sealed record PropertyMetadata(
        string Name,
        bool IsStatic,
        bool HasGetter,
        bool HasPublicGetter);

    internal sealed record TypeRefWithArgs(string Assembly, string FullName, string[] GenericArgs);

    internal sealed class TypeMetadata
    {
        public required string Assembly { get; init; }
        public required string FullName { get; init; }
        public required string SimpleName { get; init; }
        public TypeRefWithArgs? BaseType { get; init; }
        public List<TypeRefWithArgs> Interfaces { get; } = new();
        public List<MethodMetadata> DeclaredMethods { get; } = new();
        public List<FieldMetadata> DeclaredFields { get; } = new();
        public List<PropertyMetadata> DeclaredProperties { get; } = new();
    }

    private sealed class CanonicalTypeProvider : ISignatureTypeProvider<string, object?>
    {
        private readonly Dictionary<TypeDefinitionHandle, string> _defFullNames;
        private readonly Dictionary<TypeReferenceHandle, (string? Assembly, string FullName)> _refCache;

        public CanonicalTypeProvider(
            Dictionary<TypeDefinitionHandle, string> defFullNames,
            Dictionary<TypeReferenceHandle, (string? Assembly, string FullName)> refCache)
        {
            _defFullNames = defFullNames;
            _refCache = refCache;
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Void => "System.Void",
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.Int16 => "System.Int16",
            PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.TypedReference => "System.TypedReference",
            _ => typeCode.ToString()
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            ResolveDefFullName(reader, handle, _defFullNames);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            ResolveTypeRef(reader, handle, _refCache).FullName;

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape shape) =>
            $"{elementType}[{new string(',', Math.Max(0, shape.Rank - 1))}]";

        public string GetByReferenceType(string elementType) => elementType + "&";

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPinnedType(string elementType) => elementType;

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            $"{genericType}<{string.Join(",", typeArguments)}>";

        public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";

        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";

        public string GetFunctionPointerType(MethodSignature<string> signature) =>
            $"fn({string.Join(",", signature.ParameterTypes)})->{signature.ReturnType}";
    }

    internal sealed class AssemblyIndex
    {
        private static readonly Lazy<Dictionary<string, string>> TrustedPlatformAssemblies = new(() =>
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            {
                foreach (var entry in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    var name = Path.GetFileNameWithoutExtension(entry);
                    if (!string.IsNullOrWhiteSpace(name) && File.Exists(entry))
                    {
                        map.TryAdd(name, entry);
                    }
                }
            }

            return map;
        });

        private readonly HashSet<string> _attemptedExternalLoads = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<(string Assembly, string FullName), TypeMetadata> ByFullName { get; } = new();
        public Dictionary<string, List<TypeMetadata>> BySimpleName { get; } = new(StringComparer.Ordinal);
        public HashSet<string> LoadedAssemblies { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> TargetLoadedAssemblies { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void LoadAssembly(string dllPath, bool isTargetAssembly = true, string? overrideAssemblyName = null)
        {
            using var stream = File.OpenRead(dllPath);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            var asmName = overrideAssemblyName ?? reader.GetString(reader.GetAssemblyDefinition().Name);
            if (!LoadedAssemblies.Add(asmName))
            {
                if (isTargetAssembly)
                {
                    TargetLoadedAssemblies.Add(asmName);
                }

                return;
            }

            if (isTargetAssembly)
            {
                TargetLoadedAssemblies.Add(asmName);
            }

            var fullNames = new Dictionary<TypeDefinitionHandle, string>();
            var refCache = new Dictionary<TypeReferenceHandle, (string? Assembly, string FullName)>();
            foreach (var handle in reader.TypeDefinitions)
            {
                ResolveDefFullName(reader, handle, fullNames);
            }

            var provider = new CanonicalTypeProvider(fullNames, refCache);

            foreach (var handle in reader.TypeDefinitions)
            {
                var def = reader.GetTypeDefinition(handle);
                var simpleName = reader.GetString(def.Name);
                var fullName = fullNames[handle];
                var baseRef = ResolveTypeRefWithArgs(reader, def.BaseType, asmName, fullNames, refCache, provider);
                var meta = new TypeMetadata
                {
                    Assembly = asmName,
                    FullName = fullName,
                    SimpleName = simpleName,
                    BaseType = baseRef
                };

                foreach (var ifaceHandle in def.GetInterfaceImplementations())
                {
                    var iface = reader.GetInterfaceImplementation(ifaceHandle);
                    var resolved = ResolveTypeRefWithArgs(reader, iface.Interface, asmName, fullNames, refCache, provider);
                    if (resolved != null)
                    {
                        meta.Interfaces.Add(resolved);
                    }
                }

                var methodByHandle = new Dictionary<MethodDefinitionHandle, MethodMetadata>();
                foreach (var methodHandle in def.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    var methodName = reader.GetString(method.Name);
                    var decoded = method.DecodeSignature(provider, null);
                    var isStatic = (method.Attributes & MethodAttributes.Static) != 0;
                    var methodMeta = new MethodMetadata(
                        methodName,
                        isStatic,
                        decoded.GenericParameterCount,
                        decoded.ReturnType,
                        decoded.ParameterTypes.ToArray(),
                        method.Attributes);
                    meta.DeclaredMethods.Add(methodMeta);
                    methodByHandle[methodHandle] = methodMeta;
                }

                foreach (var fieldHandle in def.GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    var fieldType = field.DecodeSignature(provider, null);
                    var isStatic = (field.Attributes & FieldAttributes.Static) != 0;
                    meta.DeclaredFields.Add(new FieldMetadata(
                        reader.GetString(field.Name),
                        isStatic,
                        fieldType,
                        field.Attributes));
                }

                foreach (var propHandle in def.GetProperties())
                {
                    var prop = reader.GetPropertyDefinition(propHandle);
                    var accessors = prop.GetAccessors();
                    MethodMetadata? getter = null;
                    if (!accessors.Getter.IsNil)
                    {
                        methodByHandle.TryGetValue(accessors.Getter, out getter);
                    }

                    MethodMetadata? setter = null;
                    if (!accessors.Setter.IsNil)
                    {
                        methodByHandle.TryGetValue(accessors.Setter, out setter);
                    }

                    var isStatic = (getter ?? setter)?.IsStatic ?? false;
                    meta.DeclaredProperties.Add(new PropertyMetadata(
                        reader.GetString(prop.Name),
                        isStatic,
                        getter != null,
                        getter?.IsPublic == true));
                }

                ByFullName[(asmName, fullName)] = meta;
                if (isTargetAssembly)
                {
                    if (!BySimpleName.TryGetValue(simpleName, out var list))
                    {
                        list = new List<TypeMetadata>();
                        BySimpleName[simpleName] = list;
                    }

                    list.Add(meta);
                }
            }
        }

        public bool TryGetType(string assembly, string fullName, out TypeMetadata meta, bool allowExternalBclLoad = false)
        {
            if (ByFullName.TryGetValue((assembly, fullName), out meta!))
            {
                return true;
            }

            foreach (var loaded in LoadedAssemblies)
            {
                if (ByFullName.TryGetValue((loaded, fullName), out meta!))
                {
                    return true;
                }
            }

            if (allowExternalBclLoad)
            {
                EnsureExternalBclLoaded(assembly);
                if (ByFullName.TryGetValue((assembly, fullName), out meta!))
                {
                    return true;
                }

                foreach (var loaded in LoadedAssemblies)
                {
                    if (ByFullName.TryGetValue((loaded, fullName), out meta!))
                    {
                        return true;
                    }
                }
            }

            meta = null!;
            return false;
        }

        private void EnsureExternalBclLoaded(string assemblyName)
        {
            TryLoadTrustedAssembly("System.Private.CoreLib");
            TryLoadTrustedAssembly(assemblyName);
            if (assemblyName.Equals("System.Runtime", StringComparison.OrdinalIgnoreCase)
                || assemblyName.Equals("netstandard", StringComparison.OrdinalIgnoreCase))
            {
                TryLoadTrustedAssembly("System.Collections");
                TryLoadTrustedAssembly("System.Linq");
            }
        }

        private void TryLoadTrustedAssembly(string assemblyName)
        {
            if (!_attemptedExternalLoads.Add(assemblyName) || LoadedAssemblies.Contains(assemblyName))
            {
                return;
            }

            if (TrustedPlatformAssemblies.Value.TryGetValue(assemblyName, out var path))
            {
                LoadAssembly(path, isTargetAssembly: false);
            }
        }

        public bool HasMethodInHierarchy(
            string assembly,
            string fullName,
            string methodName,
            bool isStatic,
            int genericArity,
            string returnType,
            string[] paramTypes,
            bool allowProtected)
        {
            if (!TryGetType(assembly, fullName, out var start))
            {
                return false;
            }

            // Constructors are never inherited in the CLR.
            if (methodName is ".ctor" or ".cctor")
            {
                return start.DeclaredMethods.Any(m =>
                    MatchesMethod(m, methodName, isStatic, genericArity, returnType, paramTypes, allowProtected, null));
            }

            var visited = new HashSet<(string, string)>();
            var queue = new Queue<(TypeMetadata Meta, string[]? TypeArgMap)>();
            queue.Enqueue((start, null));

            while (queue.Count > 0)
            {
                var (current, argMap) = queue.Dequeue();
                if (!visited.Add((current.Assembly, current.FullName)))
                {
                    continue;
                }

                if (current.DeclaredMethods.Any(m =>
                    MatchesMethod(m, methodName, isStatic, genericArity, returnType, paramTypes, allowProtected, argMap)))
                {
                    return true;
                }

                if (current.BaseType != null
                    && TryGetType(current.BaseType.Assembly, current.BaseType.FullName, out var baseMeta, allowExternalBclLoad: true))
                {
                    queue.Enqueue((baseMeta, ComposeTypeArgs(current.BaseType.GenericArgs, argMap)));
                }

                foreach (var iface in current.Interfaces)
                {
                    if (TryGetType(iface.Assembly, iface.FullName, out var ifaceMeta, allowExternalBclLoad: true))
                    {
                        queue.Enqueue((ifaceMeta, ComposeTypeArgs(iface.GenericArgs, argMap)));
                    }
                }
            }

            return false;
        }

        public bool HasFieldInHierarchy(
            string assembly,
            string fullName,
            string fieldName,
            string fieldType,
            bool allowProtected)
        {
            if (!TryGetType(assembly, fullName, out var start))
            {
                return false;
            }

            var visited = new HashSet<(string, string)>();
            var current = start;
            string[]? argMap = null;
            while (current != null && visited.Add((current.Assembly, current.FullName)))
            {
                if (current.DeclaredFields.Any(f =>
                    string.Equals(f.Name, fieldName, StringComparison.Ordinal)
                    && f.IsAccessibleFrom(allowProtected)
                    && string.Equals(SubstituteTypeArgs(f.FieldType, argMap), fieldType, StringComparison.Ordinal)))
                {
                    return true;
                }

                if (current.BaseType == null
                    || !TryGetType(current.BaseType.Assembly, current.BaseType.FullName, out var next, allowExternalBclLoad: true))
                {
                    break;
                }

                argMap = ComposeTypeArgs(current.BaseType.GenericArgs, argMap);
                current = next;
            }

            return false;
        }

        private static bool MatchesMethod(
            MethodMetadata candidate,
            string name,
            bool isStatic,
            int genericArity,
            string returnType,
            string[] paramTypes,
            bool allowProtected,
            string[]? argMap)
        {
            if (!string.Equals(candidate.Name, name, StringComparison.Ordinal)
                || candidate.IsStatic != isStatic
                || candidate.GenericArity != genericArity
                || candidate.ParamTypes.Length != paramTypes.Length
                || !candidate.IsAccessibleFrom(allowProtected))
            {
                return false;
            }

            if (!string.Equals(SubstituteTypeArgs(candidate.ReturnType, argMap), returnType, StringComparison.Ordinal))
            {
                return false;
            }

            for (var i = 0; i < paramTypes.Length; i++)
            {
                if (!string.Equals(SubstituteTypeArgs(candidate.ParamTypes[i], argMap), paramTypes[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string[]? ComposeTypeArgs(string[] childArgs, string[]? outerMap)
        {
            if (childArgs.Length == 0)
            {
                return null;
            }

            if (outerMap == null)
            {
                return childArgs;
            }

            var result = new string[childArgs.Length];
            for (var i = 0; i < childArgs.Length; i++)
            {
                result[i] = SubstituteTypeArgs(childArgs[i], outerMap);
            }

            return result;
        }

        private static string SubstituteTypeArgs(string typeName, string[]? argMap)
        {
            if (argMap == null || argMap.Length == 0 || !typeName.Contains('!'))
            {
                return typeName;
            }

            // Replace !0, !1, ... (generic type parameters) while preserving !!0 (generic method params).
            return Regex.Replace(typeName, @"(?<!!)!(\d+)", match =>
            {
                var idx = int.Parse(match.Groups[1].Value);
                return idx >= 0 && idx < argMap.Length ? argMap[idx] : match.Value;
            });
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
                index.LoadAssembly(path, isTargetAssembly: true);
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
            var defFullNames = new Dictionary<TypeDefinitionHandle, string>();
            foreach (var handle in reader.TypeDefinitions)
            {
                ResolveDefFullName(reader, handle, defFullNames);
            }

            var refCache = new Dictionary<TypeReferenceHandle, (string? Assembly, string FullName)>();
            var provider = new CanonicalTypeProvider(defFullNames, refCache);

            // Collect target base types subclassed by the mod (e.g., Godot.CanvasLayer / Godot.Control)
            // so protected (Family / FamORAssem) members are permitted only on genuinely derived hierarchies.
            var modSubclassedTargets = CollectModSubclassedTargets(reader, targets, defFullNames, refCache, provider, index);

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
                var allowProtected = modSubclassedTargets.Contains(fullName);
                var sigReader = reader.GetBlobReader(member.Signature);
                var header = sigReader.ReadSignatureHeader();
                if (header.Kind == SignatureKind.Method)
                {
                    var decoded = member.DecodeMethodSignature(provider, null);
                    var isStatic = !header.IsInstance;
                    var paramTypes = decoded.ParameterTypes.ToArray();
                    if (!index.HasMethodInHierarchy(
                        asm,
                        fullName,
                        memberName,
                        isStatic,
                        decoded.GenericParameterCount,
                        decoded.ReturnType,
                        paramTypes,
                        allowProtected))
                    {
                        var prefix = isStatic ? "static " : string.Empty;
                        var genericSuffix = decoded.GenericParameterCount > 0 ? $"<{decoded.GenericParameterCount}>" : string.Empty;
                        missingMembers.Add($"{asm}::{fullName}.{prefix}{memberName}{genericSuffix}({string.Join(", ", paramTypes)}) -> {decoded.ReturnType}");
                    }
                }
                else if (header.Kind == SignatureKind.Field)
                {
                    var fieldType = member.DecodeFieldSignature(provider, null);
                    if (!index.HasFieldInHierarchy(asm, fullName, memberName, fieldType, allowProtected))
                    {
                        missingMembers.Add($"{asm}::{fullName}.{memberName} : {fieldType} (field)");
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
                var isStatic = string.Equals(match.Groups[4].Value, "true", StringComparison.Ordinal);
                checkedReflected++;

                if (!index.BySimpleName.TryGetValue(typeName, out var candidates) || candidates.Count == 0)
                {
                    missingReflected.Add($"{typeName}.{memberName} ({kind}, static={isStatic}: type not found)");
                    continue;
                }

                var found = candidates.Any(meta => kind switch
                {
                    "Field" => meta.DeclaredFields.Any(f =>
                        string.Equals(f.Name, memberName, StringComparison.Ordinal) && f.IsStatic == isStatic),
                    // ReflectedMemberResolver.Method(type, name, isStatic) returns null on AmbiguousMatchException,
                    // so a valid entry must match exactly one overload with the declared staticness.
                    "Method" => meta.DeclaredMethods.Count(m =>
                        string.Equals(m.Name, memberName, StringComparison.Ordinal) && m.IsStatic == isStatic) == 1,
                    "Property" => meta.DeclaredProperties.Any(p =>
                        string.Equals(p.Name, memberName, StringComparison.Ordinal) && p.IsStatic == isStatic && p.HasGetter),
                    _ => false
                });
                if (!found)
                {
                    missingReflected.Add($"{typeName}.{memberName} ({kind}, static={isStatic}: missing or ambiguous on declaring type)");
                }
            }

            // CardModel.UpgradeInternal (public non-static void, 0 params or (bool)) is probed alongside ReflectedGameMembers.
            if (index.TargetLoadedAssemblies.Contains("sts2"))
            {
                checkedReflected++;
                var hasUpgrade = index.TryGetType("sts2", "MegaCrit.Sts2.Core.Models.CardModel", out var cardModel)
                    && cardModel.DeclaredMethods.Any(m =>
                        string.Equals(m.Name, "UpgradeInternal", StringComparison.Ordinal)
                        && !m.IsStatic
                        && m.IsPublic
                        && string.Equals(m.ReturnType, "System.Void", StringComparison.Ordinal)
                        && (m.ParamTypes.Length == 0
                            || (m.ParamTypes.Length == 1 && string.Equals(m.ParamTypes[0], "System.Boolean", StringComparison.Ordinal))));
                if (!hasUpgrade)
                {
                    missingReflected.Add("CardModel.UpgradeInternal (Method: expected public instance void UpgradeInternal() or (bool))");
                }
            }
        }

        var capabilities = index.TargetLoadedAssemblies.Contains("sts2")
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

        if (!Directory.Exists(dataDir) || !File.Exists(Path.Combine(dataDir, "sts2.dll")))
        {
            Console.Error.WriteLine($"sts2.dll not found in game data directory: {dataDir}");
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
        if (registrySource != null && report.CheckedReflected < MinimumReflectedEntries)
        {
            Console.Error.WriteLine($"[game-abi] Registry parse sanity check failed: expected at least {MinimumReflectedEntries} reflected entries, parsed {report.CheckedReflected}.");
            return 1;
        }

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

    private static HashSet<string> CollectModSubclassedTargets(
        MetadataReader reader,
        HashSet<string> targets,
        Dictionary<TypeDefinitionHandle, string> defFullNames,
        Dictionary<TypeReferenceHandle, (string? Assembly, string FullName)> refCache,
        CanonicalTypeProvider provider,
        AssemblyIndex index)
    {
        var subclassed = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string Assembly, string FullName)>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var def = reader.GetTypeDefinition(handle);
            var baseRef = ResolveTypeRefWithArgs(reader, def.BaseType, string.Empty, defFullNames, refCache, provider);
            if (baseRef != null && targets.Contains(baseRef.Assembly) && subclassed.Add(baseRef.FullName))
            {
                queue.Enqueue((baseRef.Assembly, baseRef.FullName));
            }
        }

        while (queue.Count > 0)
        {
            var (asm, full) = queue.Dequeue();
            if (index.TryGetType(asm, full, out var meta) && meta.BaseType != null && subclassed.Add(meta.BaseType.FullName))
            {
                queue.Enqueue((meta.BaseType.Assembly, meta.BaseType.FullName));
            }
        }

        return subclassed;
    }

    private static IReadOnlyList<CapabilityAudit> AuditCapabilities(AssemblyIndex index)
    {
        bool HasPublicProp(string fullType, string prop, bool isStatic = false) =>
            index.TryGetType("sts2", fullType, out var meta)
            && meta.DeclaredProperties.Any(p =>
                string.Equals(p.Name, prop, StringComparison.Ordinal) && p.IsStatic == isStatic && p.HasPublicGetter);

        bool HasField(string fullType, string field, bool isStatic = false) =>
            index.TryGetType("sts2", fullType, out var meta)
            && meta.DeclaredFields.Any(f =>
                string.Equals(f.Name, field, StringComparison.Ordinal) && f.IsStatic == isStatic);

        bool HasEnumValue(string fullType, string fieldName) =>
            HasField(fullType, fieldName, isStatic: true);

        bool HasPublicMethod(string fullType, string method, bool isStatic, int arity) =>
            index.TryGetType("sts2", fullType, out var meta)
            && meta.DeclaredMethods.Any(m =>
                string.Equals(m.Name, method, StringComparison.Ordinal)
                && m.IsStatic == isStatic
                && m.IsPublic
                && m.ParamTypes.Length == arity);

        bool HasType(string fullType) => index.TryGetType("sts2", fullType, out _);

        var hasMerchantSyncProp = HasPublicProp("MegaCrit.Sts2.Core.Runs.RunManager", "MerchantSynchronizer");
        var merchant = hasMerchantSyncProp
            && HasPublicMethod("MegaCrit.Sts2.Core.Multiplayer.Game.MerchantSynchronizer", "PurchaseLocalEntry", isStatic: false, arity: 1)
            ? new CapabilityAudit("merchant_purchase", "available", "MerchantSynchronizer.PurchaseLocalEntry")
            : !hasMerchantSyncProp
                && HasPublicMethod("MegaCrit.Sts2.Core.Entities.Merchant.MerchantEntry", "OnTryPurchaseWrapper", isStatic: false, arity: 2)
                ? new CapabilityAudit("merchant_purchase", "fallback", "MerchantEntry.OnTryPurchaseWrapper")
                : new CapabilityAudit("merchant_purchase", "missing", "Neither MerchantSynchronizer.PurchaseLocalEntry nor legacy fallback resolved");

        var rewards = HasPublicMethod("MegaCrit.Sts2.Core.Runs.RunManager", "ProceedFromTerminalRewardsScreen", isStatic: false, arity: 0)
            ? new CapabilityAudit("terminal_rewards_proceed", "available", null)
            : new CapabilityAudit("terminal_rewards_proceed", "missing", "RunManager.ProceedFromTerminalRewardsScreen not found");

        var hasAscTitle = HasPublicMethod("MegaCrit.Sts2.Core.Helpers.AscensionHelper", "GetTitle", isStatic: true, arity: 2)
            || HasPublicMethod("MegaCrit.Sts2.Core.Helpers.AscensionHelper", "GetTitle", isStatic: true, arity: 1);
        var hasAscDesc = HasPublicMethod("MegaCrit.Sts2.Core.Helpers.AscensionHelper", "GetDescription", isStatic: true, arity: 2)
            || HasPublicMethod("MegaCrit.Sts2.Core.Helpers.AscensionHelper", "GetDescription", isStatic: true, arity: 1);
        var ascension = (HasField("MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer", "maxStandardAscension")
                || HasField("MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer", "maxMultiplayerAscensionUnlocked"))
            && hasAscTitle && hasAscDesc
            ? new CapabilityAudit("ascension_metadata", "available", null)
            : new CapabilityAudit("ascension_metadata", "missing", "StartRunLobbyPlayer ascension field or AscensionHelper text method missing");

        var hasArrowType = HasType("MegaCrit.Sts2.Core.Entities.FoolArrows.ArrowSlot");
        var arrows = !hasArrowType
            ? new CapabilityAudit("fool_arrows", "not_in_branch", null)
            : HasPublicProp("MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState", "Arrows")
                && HasPublicProp("MegaCrit.Sts2.Core.Entities.FoolArrows.ArrowSlot", "Status")
                && HasPublicProp("MegaCrit.Sts2.Core.Entities.FoolArrows.ArrowSlot", "IsAvailable")
                && HasPublicProp("MegaCrit.Sts2.Core.Models.CharacterModel", "BaseArrowSlotCount")
                && HasPublicMethod("MegaCrit.Sts2.Core.Commands.ArrowCmd", "GetAvailableArrowCount", isStatic: true, arity: 1)
                && HasEnumValue("MegaCrit.Sts2.Core.Entities.Cards.CardTag", "Shoot")
                ? new CapabilityAudit("fool_arrows", "available", null)
                : new CapabilityAudit("fool_arrows", "missing", "ArrowSlot present but required arrow members or CardTag.Shoot missing");

        var hasMelody = HasType("MegaCrit.Sts2.Core.Commands.MelodyCmd");
        var melody = !hasMelody
            ? new CapabilityAudit("fool_melody", "not_in_branch", null)
            : HasPublicMethod("MegaCrit.Sts2.Core.Commands.MelodyCmd", "PlayedWahThisTurn", isStatic: true, arity: 1)
                && HasPublicMethod("MegaCrit.Sts2.Core.Commands.MelodyCmd", "PlayedChoThisTurn", isStatic: true, arity: 1)
                && HasPublicMethod("MegaCrit.Sts2.Core.Commands.MelodyCmd", "PlayedKiThisTurn", isStatic: true, arity: 1)
                && HasEnumValue("MegaCrit.Sts2.Core.Entities.Cards.CardTag", "Wah")
                && HasEnumValue("MegaCrit.Sts2.Core.Entities.Cards.CardTag", "Cho")
                && HasEnumValue("MegaCrit.Sts2.Core.Entities.Cards.CardTag", "Ki")
                ? new CapabilityAudit("fool_melody", "available", null)
                : new CapabilityAudit("fool_melody", "missing", "MelodyCmd present but Played*ThisTurn methods or melody CardTags missing");

        var hasBlitz = HasType("MegaCrit.Sts2.Core.Nodes.Screens.Blitz.NBlitzScreen");
        var hasDread = HasType("MegaCrit.Sts2.Core.Nodes.Screens.Dread.NDreadScreen");
        const string blitzEntry = "MegaCrit.Sts2.Core.Entities.Merchant.BlitzMerchantEntry";
        var blitzDread = !hasBlitz && !hasDread
            ? new CapabilityAudit("blitz_dread_modes", "not_in_branch", null)
            : hasBlitz && hasDread
                && HasPublicProp("MegaCrit.Sts2.Core.Nodes.Screens.Blitz.NBlitzScreen", "Lobby")
                && HasPublicProp("MegaCrit.Sts2.Core.Nodes.Screens.Dread.NDreadScreen", "Lobby")
                && HasPublicProp("MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory", "IsBlitzShop")
                && HasPublicProp("MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory", "BlitzChoicesRemaining")
                && HasPublicProp("MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory", "BlitzEntries")
                && HasType(blitzEntry)
                && HasPublicProp(blitzEntry, "MerchantEntryType")
                && HasPublicProp(blitzEntry, "Title")
                && HasPublicProp(blitzEntry, "Description")
                && HasPublicProp(blitzEntry, "Status")
                && HasPublicProp(blitzEntry, "IsCursed")
                && HasPublicProp(blitzEntry, "IsRare")
                && HasType("MegaCrit.Sts2.Core.Models.Characters.RotatingCharacter")
                && HasField("MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer", "maxBlitzAscension")
                && HasField("MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer", "isRotatingDread")
                && HasPublicProp("MegaCrit.Sts2.Core.Runs.RunState", "Dominion")
                && HasPublicProp("MegaCrit.Sts2.Core.Runs.RunState", "IsCustomGame")
                && HasPublicProp("MegaCrit.Sts2.Core.Entities.Players.ExtraPlayerFields", "IsPlayingRotatingCharacter")
                ? new CapabilityAudit("blitz_dread_modes", "available", null)
                : new CapabilityAudit("blitz_dread_modes", "missing", "Blitz/Dread screens partially present but lobby, Dread, or Blitz shop members missing");

        var preBlock = HasPublicMethod("MegaCrit.Sts2.Core.Hooks.Hook", "ModifyPreBlockDamageTarget", isStatic: true, arity: 5)
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

    private static TypeRefWithArgs? ResolveTypeRefWithArgs(
        MetadataReader reader,
        EntityHandle handle,
        string currentAssembly,
        Dictionary<TypeDefinitionHandle, string> defFullNames,
        Dictionary<TypeReferenceHandle, (string? Assembly, string FullName)> refCache,
        CanonicalTypeProvider provider)
    {
        if (handle.IsNil)
        {
            return null;
        }

        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return new TypeRefWithArgs(currentAssembly, ResolveDefFullName(reader, (TypeDefinitionHandle)handle, defFullNames), Array.Empty<string>());
            case HandleKind.TypeReference:
            {
                var resolved = ResolveTypeRef(reader, (TypeReferenceHandle)handle, refCache);
                return resolved.Assembly != null ? new TypeRefWithArgs(resolved.Assembly, resolved.FullName, Array.Empty<string>()) : null;
            }
            case HandleKind.TypeSpecification:
            {
                var spec = reader.GetTypeSpecification((TypeSpecificationHandle)handle);
                var blob = reader.GetBlobReader(spec.Signature);
                var unwrapped = UnwrapGenericSpec(reader, blob, currentAssembly, defFullNames, refCache);
                if (unwrapped.Assembly == null || unwrapped.FullName == null)
                {
                    return null;
                }

                var decoded = spec.DecodeSignature(provider, null);
                var args = ParseTopLevelGenericArgs(decoded);
                return new TypeRefWithArgs(unwrapped.Assembly, unwrapped.FullName, args);
            }
            default:
                return null;
        }
    }

    private static string[] ParseTopLevelGenericArgs(string decoded)
    {
        var open = decoded.IndexOf('<');
        var close = decoded.LastIndexOf('>');
        if (open < 0 || close <= open)
        {
            return Array.Empty<string>();
        }

        var args = new List<string>();
        var depth = 0;
        var start = open + 1;
        for (var i = open + 1; i < close; i++)
        {
            var ch = decoded[i];
            if (ch == '<') depth++;
            else if (ch == '>') depth--;
            else if (ch == ',' && depth == 0)
            {
                args.Add(decoded[start..i]);
                start = i + 1;
            }
        }

        args.Add(decoded[start..close]);
        return args.ToArray();
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
}
