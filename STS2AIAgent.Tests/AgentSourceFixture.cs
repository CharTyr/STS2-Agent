using System.Text;

namespace STS2AIAgent.Tests;

internal static class AgentSourceFixture
{
    /// <summary>Repository root that holds both the mod and this test project.</summary>
    public static string Root => FindAgentRoot();

    /// <summary>Every C# file of the mod, for tests that audit the whole source tree.</summary>
    public static IEnumerable<string> SourceFiles()
    {
        return Directory.EnumerateFiles(Path.Combine(Root, "STS2AIAgent"), "*.cs", SearchOption.AllDirectories);
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
