namespace Net.Leksi.RuntimeAssemblyCompiler;

public class ProjectOptions
{
    public string? Name { get; set; } = null;
    public string? Namespace { get; set; } = null;
    public string? FullName { get; set; } = null;
    public string? Sdk { get; set; } = null;
    public string? TargetFramework { get; set; } = null;
    public string? ProjectDir { get; set; } = null;
    public OutputType? OutputType { get; set; } = null;
    public bool? GeneratePackage { get; set; } = null;
    public string? PathToDotnetExe { get; set; } = null;
    public string? BuildOutputLang { get; set; } = null;
    public string? Configuration { get; set; } = null;

}
