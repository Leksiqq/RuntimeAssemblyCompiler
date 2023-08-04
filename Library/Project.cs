using System.Diagnostics;
using System.IO;
using System.Runtime.Loader;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Net.Leksi.RuntimeAssemblyCompiler;

public class Project: IDisposable
{
    public event DotnetEventHandler? DotnetEvent;

    private const string s_defaultSdk = "Microsoft.NET.Sdk";

    private static readonly string _appDataDirectory = typeof(Project).Namespace!;
    private const string s_libraryOutputType = "Library";
    private const string s_exeOutputType = "Exe";
    private const string s_true = "True";
    private const string s_false = "False";
    private const string s_outputDirName = "bin";
    private const string s_version = "1.0.0";

    private readonly List<PackageHolder> _packages = new();
    private readonly List<ProjectHolder> _projects = new();

    public string Name { get; private set; } = null!;
    public string Sdk { get; private set; } = null!;
    public string? SourceDirectory { get; private set; } = null;
    public string? OutputDirectory { get; private set; } = null;
    public string? TargetFramework { get; private set; } = null;
    public bool IsExecutable { get; private set; } = false;
    public bool IsVerbose { get; set; } = false;
    public string Configuration { get; set; } = "Release";
    public Encoding LogEncoding { get; init; } = Encoding.UTF8;
    public string? LibraryFile { get; private set; } = null;
    public string? ExeFile { get; private set; } = null;
    public string OutputType { get; private set; } = null!;
    public string ProjectFile { get; private set; } = null!;
    public bool GeneratePackage { get; private set; } = false;
    public bool IsTemporary { get; private set; } = false;

    ~Project()
    {
        Dispose();
    }

    public static Project Create(ProjectOptions options)
    {
        if (string.IsNullOrEmpty(options.Name))
        {
            throw new ArgumentNullException(nameof(options.Name));
        }
        Project project = new()
        {
            Name = options.Name,
            Sdk = options.Sdk ?? s_defaultSdk,
            TargetFramework = options.TargetFramework ??
                $"net{Environment.Version.Major}.{Environment.Version.Minor}",
            IsTemporary = options.SourceDirectory is null,
            SourceDirectory = options.SourceDirectory ?? GetTemporaryDirectory(),
            IsExecutable = options.IsExecutable,
            OutputType = options.IsExecutable ? s_exeOutputType : s_libraryOutputType,
            GeneratePackage = options.GeneratePackage,
        };
        project.ProjectFile = Path.Combine(project.SourceDirectory, $"{project.Name}.csproj");
        project.OutputDirectory = Path.Combine(project.SourceDirectory, s_outputDirName);
        return project;
    }

    public void AddPackage(string name, string version, string? source = null)
    {
        if (_packages.Any(p => name.Equals(p.Name)))
        {
            throw new ArgumentException($"Package {name} is already added!");
        }
        _packages.Add(new PackageHolder
        {
            Name = name,
            Version = version,
            Source = source,
        });
    }

    public void AddPackage(Project project)
    {
        AddPackage(project.Name, s_version, project.OutputDirectory);
    }

    public void AddProject(string path)
    {
        if (_projects.Any(p => path.Equals(p.Path)))
        {
            throw new ArgumentException($"Project {path} is already added!");
        }
        _projects.Add(new ProjectHolder { Path = path });
    }

    public void AddProject(Project project)
    {
        if (_projects.Any(p => project == p.Project))
        {
            throw new ArgumentException($"Project {project} is already added!");
        }
        _projects.Add(new ProjectHolder { Project = project });
    }

    public bool Compile()
    {
        Thread cleaner = new(CleanTemporary);
        cleaner.IsBackground = true;
        cleaner.Start();

        CreateProjectFile();
        foreach (PackageHolder package in _packages)
        {
            if (!RunDotnet($"add \"{ProjectFile}\" package {package.Name} --version {package.Version}{(!string.IsNullOrEmpty(package.Source) ? $" --source \"{package.Source}\"" : string.Empty)}"))
            {
                return false;
            }
        }
        foreach (ProjectHolder project in _projects)
        {
            if (project.Project is { })
            {
                project.Project.CreateProjectFile();
                if (!RunDotnet($"add \"{ProjectFile}\" reference {project.Project.ProjectFile}"))
                {
                    return false;
                }
            }
            else
            {
                if (!RunDotnet($"add \"{ProjectFile}\" reference {project.Path}"))
                {
                    return false;
                }
            }
        }

        bool success = RunDotnet($"build \"{ProjectFile}\" --configuration {Configuration} --output \"{OutputDirectory}\" --use-current-runtime");

        if (success)
        {
            LibraryFile = Path.Combine(OutputDirectory, $"{Name}.dll");
            if (IsExecutable)
            {
                ExeFile = Path.Combine(OutputDirectory, $"{Name}.exe");
            }
            foreach (string path in Directory.GetFiles(OutputDirectory))
            {
                if ((".dll".Equals(Path.GetExtension(path)) || ".exe".Equals(Path.GetExtension(path))) && !path.Equals(LibraryFile))
                {
                    try
                    {
                        AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                    }
                    catch (Exception ex)
                    {
                        if (IsVerbose)
                        {
                            Console.WriteLine(ex);
                        }
                    }
                }
            }
        }

        return success;
    }

    public void CleanTemporary()
    {
        foreach (string path in Directory.GetDirectories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), _appDataDirectory)))
        {
            if (File.Exists(Path.Combine(path, _appDataDirectory)))
            {
                try
                {
                    Directory.Delete(path, true);
                }
                catch { }
            }
        }
    }

    public void Dispose()
    {
        if (IsTemporary && Directory.Exists(SourceDirectory))
        {
            File.WriteAllText(Path.Combine(SourceDirectory, _appDataDirectory), string.Empty);
        }
    }

    private Project() { }

    private static string GetTemporaryDirectory()
    {
        string appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            _appDataDirectory
        );
        string tempDirectory;
        for (
            tempDirectory = Path.Combine(appDataDirectory, Path.GetRandomFileName());
            Directory.Exists(tempDirectory); tempDirectory = Path.Combine(appDataDirectory,
            Path.GetRandomFileName())
        ) { }
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }

    private void CreateProjectFile()
    {
        XmlWriterSettings xws = new()
        {
            Indent = true,
        };
        File.WriteAllText(ProjectFile, $@"<Project Sdk=""{Sdk}"">
    <PropertyGroup>
        <TargetFramework>{TargetFramework}</TargetFramework>
        <OutputType>{OutputType}</OutputType>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <AssemblyName>{Name}</AssemblyName>
        <IsPackable>{(GeneratePackage ? s_true : s_false)}</IsPackable>
        <GeneratePackageOnBuild>{(GeneratePackage ? s_true : s_false)}</GeneratePackageOnBuild>
    </PropertyGroup>
    <ItemGroup/>
</Project>");
        if (IsTemporary && IsVerbose)
        {
            Console.WriteLine($"Temporary project {Name} was created at {SourceDirectory}.");
        }
    }

    private bool RunDotnet(string arguments)
    {
        StringBuilder? sbError = null;
        string? output = null;

        Process dotnet = new();

        dotnet.StartInfo = new()
        {
            FileName = @"C:\Program Files\dotnet\dotnet.exe",
            Arguments = arguments,
        };
        if (!IsVerbose)
        {
            dotnet.StartInfo.RedirectStandardOutput = true;
            dotnet.StartInfo.RedirectStandardError = true;
            dotnet.StartInfo.StandardOutputEncoding = LogEncoding;
            dotnet.StartInfo.StandardErrorEncoding = LogEncoding;
            sbError = new();

            dotnet.ErrorDataReceived += (s, e) => sbError.Append(e.Data);

        }
        dotnet.Start();

        if (!IsVerbose)
        {
            dotnet.BeginErrorReadLine();
            output = dotnet.StandardOutput.ReadToEnd();
        }


        if (!dotnet.HasExited)
        {
            dotnet.WaitForExit();
        }

        bool success = dotnet.ExitCode == 0;
        if (!IsVerbose)
        {
            DotnetEvent?.Invoke(this, new DotnetEventArgs(success, output!, sbError!.ToString(), arguments));
        }

        return success;
    }


}
