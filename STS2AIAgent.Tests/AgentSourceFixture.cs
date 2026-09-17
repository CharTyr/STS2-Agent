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
    /// Every file that declares <c>GameStateService</c>, concatenated in path order.
    /// </summary>
    /// <remarks>
    /// <c>GameStateService</c> is one <c>partial</c> class split across files -- the raw
    /// <c>/state</c> builders in <c>GameStateService.cs</c> and the compact <c>agent_view</c>
    /// rewrite in <c>GameStateService.AgentView.cs</c>. A source contract that asks what the class
    /// says must read all of it, or a member that simply moved between its own files reads as
    /// deleted. Tests that mean one specific file still name that file.
    ///
    /// At least two files are required, so merging the class back into one file fails here rather
    /// than quietly halving what every contract above sees.
    /// </remarks>
    public static string ReadStateService()
    {
        var directory = Path.Combine(Root, "STS2AIAgent", "Game");
        // GameStateService.cs first, then the partials. Order is not cosmetic here: MethodBody
        // resolves a name by its *last* occurrence, so a method must be declared after it is
        // called. The base file holds the call sites into the partials, so it has to come first
        // or MethodBody("BuildAgentViewPayload") returns the body of whatever encloses its call.
        var files = Directory
            .EnumerateFiles(directory, "GameStateService*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path) == "GameStateService.cs" ? 0 : 1)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (files.Length < 2)
        {
            throw new InvalidOperationException(
                $"GameStateService is declared in {files.Length} file(s) under {directory}. The class "
                + "was split on purpose; if it is being merged back, update the source contracts that "
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

    public static string MethodBody(string source, string methodName)
    {
        var nameIndex = source.LastIndexOf($" {methodName}(", StringComparison.Ordinal);
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
