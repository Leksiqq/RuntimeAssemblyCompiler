﻿using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace Net.Leksi.RuntimeAssemblyCompiler;

public class Project : IDisposable
{
    public event DotnetEventHandler? DotnetEvent;

    private const string s_defaultSdk = "Microsoft.NET.Sdk";

    private static string? s_appDataDirectory;
    private static bool s_isUnitTesting = false;
    private static bool s_projectCreated = false;

    private static Thread? s_cleanerThread = null;
    private static object s_lock = new();

    private const string s_libraryOutputType = "Library";
    private const string s_exeOutputType = "Exe";
    private const string s_true = "True";
    private const string s_false = "False";
    private const string s_outputDirName = "bin";
    private const string s_version = "1.0.0";
    private const string s_disposedFile = ".disposed";

    private readonly List<PackageHolder> _packages = new();
    private readonly List<ProjectHolder> _projects = new();
    private readonly List<string> _contents = new();
    private HashSet<string>? _allContents = null;

    private string _lockFile = string.Empty;

    public static bool IsUnitTesting
    {
        get 
        {
            lock (s_lock)
            {
                return s_isUnitTesting!;
            }
        }
        set {
            if(!s_projectCreated)
            {
                lock (s_lock)
                {
                    if (!s_projectCreated)
                    {
                        s_isUnitTesting = value;
                        if (value)
                        {
                            s_appDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), $"{typeof(Project).Namespace!}.Test");
                        }
                        else
                        {
                            s_appDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), typeof(Project).Namespace!);
                        }
                        if (!Directory.Exists(s_appDataDirectory))
                        {
                            Directory.CreateDirectory(s_appDataDirectory);

                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Cannot change {nameof(IsUnitTesting)}!");
                    }
                }
            }
            else
            {
                throw new InvalidOperationException($"Cannot change {nameof(IsUnitTesting)}!");
            }
        }
    }

    public string Name { get; private set; } = null!;
    public string Namespace { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
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

    static Project()
    {
        IsUnitTesting = false;
    }

    ~Project()
    {
        Dispose();
    }

    public static Project Create(ProjectOptions options)
    {
        lock (s_lock)
        {
            if (s_isUnitTesting)
            {
                if (s_cleanerThread is null)
                {
                    s_cleanerThread = new(() =>
                    {
                        ClearTemporary();
                        lock (s_lock)
                        {
                            s_cleanerThread = null;
                        }
                    });
                    s_cleanerThread.IsBackground = true;
                    s_cleanerThread.Start();
                }
            }
            else
            {
                ClearTemporary();
            }
            s_projectCreated = true;
        }

        string name = string.Empty;
        string @namespace = string.Empty;

        if (!string.IsNullOrEmpty(options.FullName))
        {
            int pos = options.FullName.LastIndexOf('.');
            if(pos == 0)
            {
                throw new ArgumentException($"{nameof(options)}.{nameof(options.FullName)}");
            }
            if(pos > 0)
            {
                name = options.FullName.Substring(pos + 1);
                @namespace = options.FullName.Substring(0, pos);
            }
            else
            {
                name = options.FullName;
            }
        }
        else
        {
            if (string.IsNullOrEmpty(options.Name))
            {
                throw new ArgumentNullException($"{nameof(options)}.{nameof(options.Name)}");
            }
            if (options.Name.Contains('.'))
            {
                throw new ArgumentException($"{nameof(options)}.{nameof(options.Name)}");
            }
            name = options.Name;
            if (!string.IsNullOrEmpty(options.Namespace))
            {
                @namespace = options.Namespace;
            }
        }

        Project project = new()
        {
            Name = name,
            Namespace = @namespace,
            FullName = $"{(string.IsNullOrEmpty(@namespace) ? string.Empty : $"{@namespace}.")}{name}",
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
        project._lockFile = Path.Combine(project.SourceDirectory, ".lock");
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
        AddPackage(project.FullName, s_version, project.OutputDirectory);
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

    public void AddContent(string path)
    {
        if (_contents.Contains(path))
        {
            throw new ArgumentException($"Content {path} is already added!");
        }
        _contents.Add(path);
    }

    public string? GetLibraryFile(Project project)
    {
        string result = Path.Combine(OutputDirectory!, $"{project.FullName}.dll");
        return File.Exists(result) ? result : null;
    }

    public bool Compile()
    {
        _allContents = new HashSet<string>();

        if (!CreateProjectFile(this))
        {
            return false;
        }

        bool success = RunDotnet($"build \"{ProjectFile}\" --configuration {Configuration} --output \"{OutputDirectory}\" --use-current-runtime");

        if (success)
        {
            LibraryFile = Path.Combine(OutputDirectory!, $"{FullName}.dll");
            if (IsExecutable)
            {
                ExeFile = Path.Combine(OutputDirectory!, $"{FullName}.exe");
            }
        }

        return success;
    }

    public static void ClearTemporary(bool unconditional = false)
    {
        foreach (string path in Directory.GetDirectories(s_appDataDirectory))
        {
            if (unconditional || File.Exists(Path.Combine(path, s_disposedFile)))
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
            File.WriteAllText(Path.Combine(SourceDirectory, s_disposedFile), string.Empty);
        }
    }

    private Project() { }

    private static string GetTemporaryDirectory()
    {
        string tempDirectory;
        for (
            tempDirectory = Path.Combine(s_appDataDirectory, Path.GetRandomFileName());
            Directory.Exists(tempDirectory); tempDirectory = Path.Combine(s_appDataDirectory,
            Path.GetRandomFileName())
        ) { }
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }

    private bool CreateProjectFile(Project root)
    {
        if (!File.Exists(_lockFile))
        {
            File.WriteAllText(_lockFile, string.Empty);
            XmlWriterSettings xws = new()
            {
                Indent = true,
            };
            XmlDocument xml = new();
            xml.LoadXml($@"<Project Sdk=""{Sdk}"">
    <PropertyGroup>
        <TargetFramework>{TargetFramework}</TargetFramework>
        <OutputType>{OutputType}</OutputType>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <AssemblyName>{FullName}</AssemblyName>
        <IsPackable>{(GeneratePackage ? s_true : s_false)}</IsPackable>
        <GeneratePackageOnBuild>{(GeneratePackage ? s_true : s_false)}</GeneratePackageOnBuild>
    </PropertyGroup>
</Project>");
            if (_contents.Any())
            {
                XPathNavigator nav = xml.DocumentElement!.CreateNavigator()!;
                nav.AppendChild("<ItemGroup/>");
                nav.MoveToChild("ItemGroup", string.Empty);
                foreach (string path in _contents)
                {
                    if (!root._allContents!.Add(path))
                    {
                        throw new InvalidOperationException($"Content '{path}' is duplicated!");
                    }
                    if (!File.Exists(Path.Combine(SourceDirectory!, path)))
                    {
                        string message = $"File to add to output '{path}' not found!";
                        if (IsVerbose)
                        {
                            Console.WriteLine(message);
                        }
                        else
                        {
                            DotnetEvent?.Invoke(this, new DotnetEventArgs(false, message, string.Empty, string.Empty));
                        }
                        return false;
                    }
                    nav.AppendChild(@$"<Content Include=""{path}"">
    <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
    <CopyToPublishDirectory>Always</CopyToPublishDirectory>
</Content>");
                }
            }
            if (_projects.Any())
            {
                XPathNavigator nav = xml.DocumentElement!.CreateNavigator()!;
                nav.AppendChild("<ItemGroup/>");
                nav.MoveToChild("ItemGroup", string.Empty);
                foreach (ProjectHolder project in _projects)
                {
                    if (project.Project is { })
                    {
                        if (!File.Exists(project.Project._lockFile))
                        {
                            project.Project.TargetFramework = TargetFramework;
                            project.Project.Configuration = Configuration;
                            project.Project.IsVerbose = IsVerbose;
                        }
                        if (!project.Project.CreateProjectFile(root))
                        {
                            return false;
                        }
                        nav.AppendChild(@$"<ProjectReference Include=""{project.Project.ProjectFile}"" />");
                    }
                    else
                    {
                        nav.AppendChild(@$"<ProjectReference Include=""{project.Path}"" />");
                    }
                }
            }
            XmlWriter xw = XmlWriter.Create(ProjectFile, xws);
            xml.WriteTo(xw);
            xw.Close();

            foreach (PackageHolder package in _packages)
            {
                if (!RunDotnet($"add \"{ProjectFile}\" package {package.Name} --version {package.Version}{(!string.IsNullOrEmpty(package.Source) ? $" --source \"{package.Source}\"" : string.Empty)}"))
                {
                    return false;
                }
            }
            if (IsTemporary)
            {
                string message = $"Temporary project {Name} was created at {SourceDirectory}.";
                if (IsVerbose)
                {
                    Console.WriteLine(message);
                }
                else
                {
                    DotnetEvent?.Invoke(this, new DotnetEventArgs(true, message, string.Empty, string.Empty));
                }
            }
        }
        return true;
    }

    private void OnDotnetEvent(object? sender, DotnetEventArgs args)
    {
        DotnetEvent?.Invoke(sender, args);
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
