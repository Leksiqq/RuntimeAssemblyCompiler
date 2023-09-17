using System.Reflection;

namespace Net.Leksi.RuntimeAssemblyCompiler;

public class MissedTypeEventArgs
{
    public string MissedTypeName { get; internal set; } = null!;
    public Project Project { get; internal set; } = null!;
    public Assembly? Assembly { get; set; } = null;
}
