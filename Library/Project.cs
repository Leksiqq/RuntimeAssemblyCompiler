﻿using System.Diagnostics;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;

namespace Net.Leksi.RuntimeAssemblyCompiler;

public class Project : IDisposable
{
    private const string s_defaultSdk = "Microsoft.NET.Sdk";

    private static string? s_appDataDirectory;
    private static bool s_isUnitTesting = false;
    private static bool s_projectCreated = false;

    private static Thread? s_cleanerThread = null;
    private static object s_lock = new();

    private const string s_true = "True";
    private const string s_false = "False";
    private const string s_version = "1.0.0";
    private const string s_disposedFile = ".disposed";

    private readonly List<PackageHolder> _packages = new();
    private readonly List<ProjectHolder> _projects = new();
    private readonly List<string> _contents = new();
    private HashSet<string>? _allContents = null;
    private HashSet<string> _symbols = new();

    private string _lockFile = string.Empty;
    private string _projectFile = null!;
    private string _outDir = null!;

    public static bool IsUnitTesting
    {
        get
        {
            lock (s_lock)
            {
                return s_isUnitTesting!;
            }
        }
        set
        {
            if (!s_projectCreated)
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
    public string ProjectDir { get; private set; } = null!;
    public string TargetFramework { get; private set; } = null!;
    public bool IsVerbose { get; set; } = false;
    public Encoding LogEncoding { get; set; } = Encoding.UTF8;
    public string Configuration { get; private set; } = "RAC";
    public string? LibraryFile { get; private set; } = null;
    public string? ExeFile { get; private set; } = null;
    public OutputType OutputType { get; private set; } = OutputType.Library;
    public bool GeneratePackage { get; private set; } = false;
    public bool IsTemporary { get; private set; } = false;
    public string AdditionalDotnetOptions { get; set; } = string.Empty;
    public string NoWarn { get; set; } = string.Empty;
    public string LastBuildLog { get; private set; } = string.Empty;
    public string PathToDotnetExe { get; private set; } = string.Empty;
    public bool ThrowAtBuildWarnings { get; set; } = false;
    public string BuildOutputLang { get; private set; } = "en";

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
        if (!s_isUnitTesting)
        {
            if (s_cleanerThread is null)
            {
                lock (s_lock)
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
            }
        }
        else if (!s_projectCreated)
        {
            ClearTemporary(true);
        }
        s_projectCreated = true;

        string name = string.Empty;
        string @namespace = string.Empty;

        if (!string.IsNullOrEmpty(options.FullName))
        {
            int pos = options.FullName.LastIndexOf('.');
            if (pos == 0)
            {
                throw new ArgumentException($"{nameof(options)}.{nameof(options.FullName)}");
            }
            if (pos > 0)
            {
                name = options.FullName.Substring(pos + 1);
                @namespace = options.FullName.Substring(0, pos);
            }
            else
            {
                name = options.FullName;
            }
            if (!string.IsNullOrEmpty(options.Name))
            {
                throw new ArgumentException($"{nameof(options)}.{nameof(options.Name)} cannot be set with {nameof(options)}.{nameof(options.FullName)}!");
            }
            if (!string.IsNullOrEmpty(options.Namespace))
            {
                throw new ArgumentException($"{nameof(options)}.{nameof(options.Namespace)} cannot be set with {nameof(options)}.{nameof(options.FullName)}!");
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
        string? pathToDotnet = options.PathToDotnetExe;
        if (string.IsNullOrEmpty(pathToDotnet))
        {
            Process where = new();

            where.StartInfo = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = "dotnet",
                RedirectStandardOutput = true,
            };
            where.Start();
            pathToDotnet = where.StandardOutput.ReadToEnd().Trim();
        }
        if (string.IsNullOrEmpty(pathToDotnet))
        {
            throw new InvalidOperationException($"dotnet.exe is not found. Make sure it is in PATH or set {nameof(ProjectOptions)}.{nameof(options.PathToDotnetExe)} property!");
        }
        Project project = new()
        {
            Name = name,
            Namespace = @namespace,
            FullName = $"{(string.IsNullOrEmpty(@namespace) ? string.Empty : $"{@namespace}.")}{name}",
            Sdk = options.Sdk ?? s_defaultSdk,
            TargetFramework = options.TargetFramework ??
                $"net{Environment.Version.Major}.{Environment.Version.Minor}",
            IsTemporary = options.ProjectDir is null,
            ProjectDir = options.ProjectDir ?? GetTemporaryDirectory(),
            PathToDotnetExe = pathToDotnet,
        };
        if (!string.IsNullOrEmpty(options.BuildOutputLang))
        {
            project.BuildOutputLang = options.BuildOutputLang;
        }
        if (!string.IsNullOrEmpty(options.Configuration))
        {
            project.Configuration = options.Configuration;
        }
        if (options.OutputType is OutputType outputType)
        {
            project.OutputType = outputType;
        }
        if (options.GeneratePackage is bool generatePackage)
        {
            project.GeneratePackage = generatePackage;
        }
        if (!string.IsNullOrEmpty(options.AdditionalDotnetOptions))
        {
            project.AdditionalDotnetOptions = options.AdditionalDotnetOptions;
        }
        if (!string.IsNullOrEmpty(options.NoWarn))
        {
            project.NoWarn = options.NoWarn;
        }
        if (options.IsVerbose is bool isVerbose)
        {
            project.IsVerbose = isVerbose;
        }
        if (options.ThrowAtBuildWarnings is bool throwAtBuildWarnings)
        {
            project.ThrowAtBuildWarnings = throwAtBuildWarnings;
        }
        if (options.LogEncoding is Encoding logEncoding)
        {
            project.LogEncoding = logEncoding;
        }
        project._outDir = Path.Combine(project.ProjectDir, "bin", project.Configuration, project.TargetFramework);
        project._projectFile = Path.Combine(project.ProjectDir, $"{project.Name}.csproj");
        project._lockFile = Path.Combine(project.ProjectDir, ".lock");
        return project;
    }

    public void Define(string symbol)
    {
        _symbols.Add(symbol);
    }

    public void Undef(string symbol)
    {
        _symbols.Remove(symbol);
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
        AddPackage(project.FullName, s_version, project._outDir);
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
            throw new ArgumentException($"Project {project.FullName} is already added!");
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
        string result = Path.Combine(_outDir!, $"{project.FullName}.dll");
        return File.Exists(result) ? result : null;
    }

    public string? GetPackageFile(Project project)
    {
        string result = Path.Combine(_outDir!, "..", $"{project.FullName}.{s_version}.nupkg");
        return File.Exists(result) ? result : null;
    }

    public string? GetLibraryFile(string path)
    {
        string? assemblyFile = null;
        try
        {
            XmlDocument projectFile = new();
            projectFile.Load(path);
            if (projectFile.CreateNavigator()?.SelectSingleNode("/Project/PropertyGroup/AssemblyName") is XPathNavigator element)
            {
                assemblyFile = element.Value;
            }
            else
            {
                assemblyFile = Path.GetFileNameWithoutExtension(path);
            }
        }
        catch (FileNotFoundException) { }
        catch (XmlException) { }
        if (string.IsNullOrEmpty(assemblyFile) && string.IsNullOrEmpty(Path.GetDirectoryName(path)))
        {
            assemblyFile = Path.Combine(_outDir, Path.GetFileNameWithoutExtension(path));
        }
        string result = Path.Combine(_outDir!, $"{assemblyFile}.dll");
        return File.Exists(result) ? result : null;
    }

    public void Compile()
    {
        LastBuildLog = string.Empty;

        _allContents = new HashSet<string>();

        CreateProjectFile(this);

        RunDotnet($"build \"{_projectFile}\" --configuration {Configuration}{(!string.IsNullOrEmpty(AdditionalDotnetOptions) ? $" {AdditionalDotnetOptions}" : string.Empty)}");

        LibraryFile = Path.Combine(_outDir!, $"{FullName}.dll");
        if (OutputType is OutputType.Exe || OutputType is OutputType.WinExe)
        {
            ExeFile = Path.Combine(_outDir!, $"{FullName}.exe");
        }
        foreach (ProjectHolder ph in _projects)
        {
            if (ph.Project is { })
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(GetLibraryFile(ph.Project)!);
            }
            else
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(GetLibraryFile(ph.Path!)!);
            }
        }
    }

    private static void ClearTemporary(bool unconditional = false)
    {
        foreach (string path in Directory.GetDirectories(s_appDataDirectory!))
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
        if (IsTemporary && Directory.Exists(ProjectDir))
        {
            File.WriteAllText(Path.Combine(ProjectDir, s_disposedFile), string.Empty);
        }
        else if (!IsTemporary && File.Exists(_lockFile))
        {
            File.Delete(_lockFile);
        }
    }

    private Project() { }

    private static string GetTemporaryDirectory()
    {
        string tempDirectory;
        for (
            tempDirectory = Path.Combine(s_appDataDirectory!, Path.GetRandomFileName());
            Directory.Exists(tempDirectory); tempDirectory = Path.Combine(s_appDataDirectory!, Path.GetRandomFileName())
        ) { }
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }

    private void CreateProjectFile(Project root)
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
            if (_symbols.Any())
            {
                XPathNavigator nav = xml.DocumentElement!.CreateNavigator()!.SelectSingleNode("PropertyGroup")!;
                nav.AppendChild($"<DefineConstants>{string.Join(';', _symbols)}</DefineConstants>");
            }
            if (_contents.Any())
            {
                XPathNavigator nav = xml.DocumentElement!.CreateNavigator()!;
                nav.AppendChild("<ItemGroup/>");
                nav = nav.SelectSingleNode("ItemGroup[last()]")!;
                foreach (string path in _contents)
                {
                    if (!root._allContents!.Add(path))
                    {
                        throw new InvalidOperationException($"Content '{path}' is duplicated!");
                    }
                    if (!File.Exists(Path.Combine(ProjectDir!, path)))
                    {
                        throw new InvalidOperationException($"File to add to output '{path}' not found!");
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
                nav = nav.SelectSingleNode("ItemGroup[last()]")!;
                foreach (ProjectHolder project in _projects)
                {
                    if (project.Project is { })
                    {
                        if (!File.Exists(project.Project._lockFile))
                        {
                            project.Project.TargetFramework = TargetFramework;
                            project.Project.Configuration = Configuration;
                            project.Project.IsVerbose = IsVerbose;
                            project.Project.LogEncoding = LogEncoding;
                        }

                        project.Project.CreateProjectFile(root);

                        nav.AppendChild(@$"<ProjectReference Include=""{project.Project._projectFile}"" />");
                    }
                    else
                    {
                        nav.AppendChild(@$"<ProjectReference Include=""{project.Path}"" />");
                    }
                }
            }
            if (_packages.Any())
            {
                XPathNavigator nav = xml.DocumentElement!.CreateNavigator()!;
                nav.AppendChild("<ItemGroup/>");
                nav = nav.SelectSingleNode("ItemGroup[last()]")!;
                foreach (PackageHolder package in _packages.Where(p => string.IsNullOrEmpty(p.Source)))
                {
                    nav.AppendChild(@$"<PackageReference Include=""{package.Name}"" Version=""{package.Version}"" />");
                }
                if (_packages.Any(p => !string.IsNullOrEmpty(p.Source)))
                {
                    string packages = Path.Combine(ProjectDir, "packages");
                    if (!Directory.Exists(packages))
                    {
                        Directory.CreateDirectory(packages);
                    }
                    foreach (PackageHolder package in _packages.Where(p => !string.IsNullOrEmpty(p.Source)))
                    {
                        string targetPackage = Path.Combine(packages, $"{package.Name}.{package.Version}");
                        if (!Directory.Exists(targetPackage))
                        {
                            Directory.CreateDirectory(targetPackage);
                        }
                        foreach (string file in Directory.GetFiles(package.Source!))
                        {
                            if (nav.SelectSingleNode($"Reference[@Include='{Path.GetFileName(file)}']") is null)
                            {
                                nav.AppendChild(@$"<Reference Include=""{file}""/>");
                            }
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(NoWarn))
            {
                XPathNavigator nav = xml.DocumentElement!.CreateNavigator()!;
                nav.AppendChild(@$"<PropertyGroup>
    <NoWarn>{NoWarn}</NoWarn>
</PropertyGroup>
");
            }
            XmlWriter xw = XmlWriter.Create(_projectFile, xws);
            xml.WriteTo(xw);
            xw.Close();

            if (IsTemporary)
            {
                if (IsVerbose)
                {
                    Console.WriteLine($"Temporary project {Name} was created at {ProjectDir}.");
                }
            }
        }
    }

    private void RunDotnet(string arguments)
    {
        StringBuilder sbError = new();
        StringBuilder sbData = new();

        Process dotnet = new();

        bool throwAtWarnings = false;

        dotnet.StartInfo = new()
        {
            FileName = PathToDotnetExe,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = LogEncoding,
            StandardErrorEncoding = LogEncoding,
        };
        if (dotnet.StartInfo.EnvironmentVariables.ContainsKey("DOTNET_CLI_UI_LANGUAGE"))
        {
            dotnet.StartInfo.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE"] = BuildOutputLang;
        }
        else
        {
            dotnet.StartInfo.EnvironmentVariables.Add("DOTNET_CLI_UI_LANGUAGE", BuildOutputLang);
        }
        dotnet.ErrorDataReceived += (s, e) =>
        {
            sbError.AppendLine(e.Data);
            if (IsVerbose)
            {
                Console.Error.WriteLine(e.Data);
            }
        };
        dotnet.OutputDataReceived += (s, e) =>
        {
            sbData.AppendLine(e.Data);
            if (IsVerbose)
            {
                Console.Out.WriteLine(e.Data);
            }
            if (ThrowAtBuildWarnings && !throwAtWarnings && !string.IsNullOrEmpty(e.Data) && Regex.IsMatch(e.Data, "\\:\\swarning\\sCS\\d{4}:"))
            {
                throwAtWarnings = true;
            }
        };

        dotnet.Start();
        dotnet.BeginErrorReadLine();
        dotnet.BeginOutputReadLine();

        dotnet.WaitForExit();

        LastBuildLog = $"`dotnet {arguments}`\n{sbData}\n{sbError}";

        if (dotnet.ExitCode != 0)
        {
            throw new InvalidOperationException($"Ended with errors: {(IsVerbose ? $"`dotnet {arguments}`" : LastBuildLog)}");
        }
        if (throwAtWarnings)
        {
            throw new InvalidOperationException($"Ended with warnings {(IsVerbose ? $"`dotnet {arguments}`" : LastBuildLog)}");
        }
    }


}
