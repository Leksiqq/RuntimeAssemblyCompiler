using System.Reflection;

namespace Net.Leksi.RuntimeAssemblyCompiler;

internal class ProjectHolder
{
    internal string? Path { get; set; } = null;
    internal Project? Project { get; set; } = null;
    internal Dictionary<string, Assembly?> SuggestedAssemblies { get; private init; } = new();
}
