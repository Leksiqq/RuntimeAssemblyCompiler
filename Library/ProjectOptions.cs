namespace Net.Leksi.RuntimeAssemblyCompiler;

public class ProjectOptions
{
    public string Name { get; set; } = null!;
    public string Namespace { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? SourceDirectory { get; set; } = null;
    public string? Sdk { get; set; } = null;
    public string? TargetFramework { get; set; } = null;
    public bool IsExecutable { get; set; } = false;
    public bool GeneratePackage { get; set; } = false;
}
