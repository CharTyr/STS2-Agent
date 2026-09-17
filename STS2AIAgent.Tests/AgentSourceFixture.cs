using System.Text;

namespace STS2AIAgent.Tests;

internal static class AgentSourceFixture
{
    /// <summary>Repository root that holds both the mod and this test project.</summary>
    public static string Root => FindAgentRoot();

    /// <summary>
    /// Every C# source file of the mod, for tests that audit the whole source tree. Generated
    /// build output is skipped so a local <c>obj/</c> or <c>bin/</c> tree cannot inject files the
    /// audit never meant to see; a fresh checkout contains neither.
    /// </summary>
    public static IEnumerable<string> SourceFiles()
    {
        var modRoot = Path.Combine(Root, "STS2AIAgent");
        return Directory
            .EnumerateFiles(modRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutputPath(Path.GetRelativePath(modRoot, path)));
    }

    /// <summary>Build output under <c>obj/</c> or <c>bin/</c> is generated, never mod source.</summary>
    private static bool IsBuildOutputPath(string relativePath)
    {
        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("obj", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every file that declares <c>GameStateService</c>, concatenated in reading order.
    /// </summary>
    /// <remarks>
    /// <c>GameStateService</c> is one <c>partial</c> class split across files -- the raw
    /// <c>/state</c> builders in <c>GameStateService.cs</c> and the compact <c>agent_view</c>
    /// rewrite in <c>GameStateService.AgentView.cs</c>. A source contract that asks what the class
    /// says must read all of it, or a member that simply moved between its own files reads as
    /// deleted. Tests that mean one specific file still name that file.
    /// </remarks>
    public static string ReadStateService() => ReadPartialClass("GameStateService");

    /// <summary>
    /// Every file that declares <c>GameActionService</c>, concatenated in reading order.
    /// </summary>
    /// <remarks>
    /// One 7,061-line file until 2026-09-17, now the base file plus one partial per room. The
    /// reasoning is the same as <see cref="ReadStateService"/>: these contracts ask what the class
    /// says, not which of its own files a member currently sits in.
    /// </remarks>
    public static string ReadActionService() => ReadPartialClass("GameActionService");

    /// <summary>
    /// Reads every file declaring one partial class, base file first.
    /// </summary>
    /// <remarks>
    /// Order is not cosmetic: <see cref="MethodBody"/> resolves a name by its *last* occurrence, so
    /// a method has to be declared after it is called. The base file holds the call sites into the
    /// partials, so it comes first -- otherwise <c>MethodBody("BuildAgentViewPayload")</c> returns
    /// the body of whatever encloses its call.
    ///
    /// At least two files are required, so merging one of these classes back into a single file
    /// fails here rather than quietly halving what every contract above it can see.
    /// </remarks>
    private static string ReadPartialClass(string className)
    {
        var directory = Path.Combine(Root, "STS2AIAgent", "Game");
        var baseFile = className + ".cs";
        var files = Directory
            .EnumerateFiles(directory, className + ".*.cs", SearchOption.TopDirectoryOnly)
            .Append(Path.Combine(directory, baseFile))
            .Where(File.Exists)
            .OrderBy(path => Path.GetFileName(path) == baseFile ? 0 : 1)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (files.Length < 2)
        {
            throw new InvalidOperationException(
                $"{className} is declared in {files.Length} file(s) under {directory}. The class was "
                + "split on purpose; if it is being merged back, update the source contracts that "
                + "read it instead of leaving them reading part of a class.");
        }

        return string.Join("\n", files.Select(path => File.ReadAllText(path, Encoding.UTF8)));
    }

    public static string Read(string relativePath)
    {
        var root = FindAgentRoot();
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required STS2-Agent source file is missing: {path}", path);
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    public static string WithoutWhitespace(string source)
    {
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Body of the method declared as <paramref name="methodName"/>.
    /// </summary>
    /// <remarks>
    /// This used to take the name's last occurrence, which worked only because a method in a
    /// single file is declared before it is called. Across the files of a partial class that stops
    /// being true, and the failure is quiet in the worst way: the body of whatever encloses a call
    /// site comes back, so the contract still asserts -- against the wrong method. Declarations are
    /// now preferred, and only when none is found does the old rule apply, for a local function
    /// whose line carries no modifier.
    /// </remarks>
    public static string MethodBody(string source, string methodName)
    {
        var nameIndex = FindDeclarationIndex(source, methodName);
        if (nameIndex < 0)
        {
            throw new InvalidOperationException($"Method declaration is missing: {methodName}");
        }

        var openBrace = source.IndexOf('{', nameIndex);
        if (openBrace < 0)
        {
            throw new InvalidOperationException($"Method body is missing: {methodName}");
        }

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            switch (source[index])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return source[openBrace..(index + 1)];
                    }
                    break;
            }
        }

        throw new InvalidOperationException($"Method body is unterminated: {methodName}");
    }

    /// <summary>
    /// Index of the last occurrence of <paramref name="methodName"/> that opens a declaration
    /// rather than a call, or the last occurrence of any kind when none does.
    /// </summary>
    private static int FindDeclarationIndex(string source, string methodName)
    {
        var needle = $" {methodName}(";
        var fallback = -1;
        var declaration = -1;

        for (var index = source.IndexOf(needle, StringComparison.Ordinal);
             index >= 0;
             index = source.IndexOf(needle, index + 1, StringComparison.Ordinal))
        {
            fallback = index;

            var lineStart = source.LastIndexOf('\n', index) + 1;
            var prefix = source[lineStart..index];
            if (prefix.Contains("private ", StringComparison.Ordinal) ||
                prefix.Contains("public ", StringComparison.Ordinal) ||
                prefix.Contains("internal ", StringComparison.Ordinal) ||
                prefix.Contains("protected ", StringComparison.Ordinal))
            {
                declaration = index;
            }
        }

        return declaration >= 0 ? declaration : fallback;
    }

    /// <summary>
    /// Braced body that follows a declaration, located by its full declaration text. Unlike
    /// <see cref="MethodBody"/>, this never resolves to a call site, so it also works for a member
    /// whose name is called later in the file than its declaration.
    /// </summary>
    public static string DeclarationBody(string source, string declaration)
    {
        var start = source.IndexOf(declaration, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"Declaration is missing: {declaration}");
        }

        var openBrace = source.IndexOf('{', start);
        if (openBrace < 0)
        {
            throw new InvalidOperationException($"Declaration body is missing: {declaration}");
        }

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            switch (source[index])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return source[openBrace..(index + 1)];
                    }
                    break;
            }
        }

        throw new InvalidOperationException($"Declaration body is unterminated: {declaration}");
    }

    private static string FindAgentRoot()
    {
        foreach (var candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(candidate));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "STS2AIAgent", "Game", "GameStateService.cs")) &&
                    File.Exists(Path.Combine(directory.FullName, "STS2AIAgent.Tests", "STS2AIAgent.Tests.csproj")))
                {
                    return directory.FullName;
                }

                var nested = Path.Combine(
                    directory.FullName,
                    "sts2-ascend",
                    "third_party",
                    "STS2-Agent");
                if (File.Exists(Path.Combine(nested, "STS2AIAgent", "Game", "GameStateService.cs")) &&
                    File.Exists(Path.Combine(nested, "STS2AIAgent.Tests", "STS2AIAgent.Tests.csproj")))
                {
                    return nested;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the STS2-Agent source root from the current directory or test output directory.");
    }
}
