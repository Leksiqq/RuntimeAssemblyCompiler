namespace Net.Leksi.RuntimeAssemblyCompiler;

public class ProjectOptions
{
    public string? Name { get; set; } = null;
    public string? Namespace { get; set; } = null;
    public string? FullName { get; set; } = null;
    public string? Sdk { get; set; } = null;
    public string? TargetFramework { get; set; } = null;
    public string? ProjectDir { get; set; } = null;
    public OutputType OutputType { get; set; } = OutputType.Library;
    public bool GeneratePackage { get; set; } = false;
}
