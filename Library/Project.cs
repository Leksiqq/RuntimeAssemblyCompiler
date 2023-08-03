
using System.Diagnostics;
using System.Runtime.Loader;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace Net.Leksi.RuntimeAssemblyCompiler;

public class Project : IDisposable
{
    public event DotnetEventHandler? DotnetEvent;

    private const string s_defaultSdk = "Microsoft.NET.Sdk";
    private const string s_libraryOutputType = "Library";
    private const string s_exeOutputType = "Exe";
    private const int s_packageItemGroup = 2;
    private const int s_projectItemGroup = 3;
    private const int s_contentItemGroup = 1;
    private const string s_projectFileName = "Project.csproj";
    private const string s_outputDirName = "bin";
    private const string s_true = "True";
    private const string s_false = "False";

    private readonly string _appDataDirectory;
    private readonly List<Project> _includes = new();

    private XmlDocument _project = new XmlDocument();
    private XPathNavigator _xpathNavigator;

    public string Name { get; private set; }
    public string SourceDirectory { get; private set; }
    public string OutputDirectory { get; private set; }
    public bool IsTemporary { get; private set; }

    public XmlDocument ProjectXml
    {
        get => _project;
        set
        {
            _project = value;
            _xpathNavigator = _project.CreateNavigator()!;
            _includes.Clear();
        }
    }

    public string Sdk
    {
        get => _xpathNavigator.SelectSingleNode("/Project/@Sdk")!.Value;
        set => ((XmlElement)_xpathNavigator.SelectSingleNode("/Project")!.UnderlyingObject!).SetAttribute("Sdk", value);
    }

    public string TargetFramework
    {
        get => _xpathNavigator.SelectSingleNode("/Project/PropertyGroup/TargetFramework")!.Value;
        set => ((XmlElement)_xpathNavigator.SelectSingleNode("/Project/PropertyGroup/TargetFramework")!.UnderlyingObject!).InnerText = value;
    }

    public bool IsExecutable
    {
        get => s_exeOutputType.Equals(_xpathNavigator.SelectSingleNode("/Project/PropertyGroup/OutputType")!.Value);
        set => ((XmlElement)_xpathNavigator.SelectSingleNode("/Project/PropertyGroup/OutputType")!.UnderlyingObject!).InnerText = value ? s_exeOutputType : s_libraryOutputType;
    }

    public string Configuration { get; set; } = "Release";
    public Encoding LogEncoding { get; init; } = Encoding.UTF8;
    public string? LibraryFile { get; private set; } = null;
    public string? ExeFile { get; private set; } = null;
    public string ProjectFileName => Path.Combine(SourceDirectory, s_projectFileName);
    public bool IsVerbose { get; set; } = false;
    public bool GeneratePackage
    {
        get => s_true.Equals(_xpathNavigator.SelectSingleNode("/Project/PropertyGroup/GeneratePackageOnBuild")!.Value)
            && s_true.Equals(_xpathNavigator.SelectSingleNode("/Project/PropertyGroup/IsPackable")!.Value);
        set
        {
            ((XmlElement)_xpathNavigator.SelectSingleNode("/Project/PropertyGroup/GeneratePackageOnBuild")!.UnderlyingObject!).InnerText = value ? s_true : s_false;
            ((XmlElement)_xpathNavigator.SelectSingleNode("/Project/PropertyGroup/IsPackable")!.UnderlyingObject!).InnerText = value ? s_true : s_false;
        }
    }

    public Project(string name, string? targetDirectory = null)
    {
        _appDataDirectory = GetType().Namespace!;
        IsTemporary = targetDirectory is null;
        Name = name;
        SourceDirectory = targetDirectory ?? GetTemporaryDirectory();
        OutputDirectory = Path.Combine(SourceDirectory, s_outputDirName);
        if (IsTemporary)
        {
            CleanPrevious();
        }
        _project.LoadXml($@"<Project Sdk=""{s_defaultSdk}"">
    <PropertyGroup>
        <TargetFramework>net{Environment.Version.Major}.{Environment.Version.Minor}</TargetFramework>
        <OutputType>{s_libraryOutputType}</OutputType>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <AssemblyName>{Name}</AssemblyName>
        <IsPackable>False</IsPackable>
        <GeneratePackageOnBuild>False</GeneratePackageOnBuild>
    </PropertyGroup>
    <ItemGroup/>
    <ItemGroup/>
    <ItemGroup/>
    <ItemGroup/>
</Project>");
        _xpathNavigator = _project.CreateNavigator()!;

    }

    private void CleanPrevious()
    {
        Thread cleaner = new(() =>
        {
            foreach(string path in Directory.GetDirectories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), _appDataDirectory)))
            {
                if(File.Exists(Path.Combine(path, _appDataDirectory)))
                {
                    try
                    {
                        Directory.Delete(path, true);
                    }
                    catch { }
                }
            }
        });
        cleaner.IsBackground = true;
        cleaner.Start();
    }

    ~Project()
    {
        Dispose();
    }

    List<Tuple<string, string, string?>> _packages = new();

    public void AddPackage(string name, string version, string? source = null)
    {
        _packages.Add(new Tuple<string, string, string>(name, version, source));
        //if (
        //    _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_packageItemGroup}]/PackageReference[@Include=\"{name}\"]") is XPathNavigator node
        //    && node.SelectSingleNode("@Version") is XPathNavigator versionAttr
        //    && node.SelectSingleNode("@Source") is XPathNavigator sourceAttr
        //)
        //{
        //    versionAttr.SetValue(version);
        //    sourceAttr.SetValue(source ?? string.Empty);
        //}
        //else
        //{
        //    _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_packageItemGroup}]")!.AppendChild($"<PackageReference Include=\"{name}\" Version=\"{version}\"  Source=\"{source ?? string.Empty}\" />");
        //}
    }

    List<string> _projects = new();

    public void AddProject(string path)
    {
        _projects.Add(path);
        //if (
        //    _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_projectItemGroup}]/ProjectReference[@Include=\"{path}\"]") is null
        //)
        //{
        //    _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_projectItemGroup}]")!.AppendChild($"<ProjectReference Include=\"{path}\" />");
        //}
    }

    public void AddProject(Project project)
    {
        string path = project.ProjectFileName;
        _projects.Add(path);
        //if (
        //    _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_projectItemGroup}]/ProjectReference[@Include=\"{path}\"]") is null
        //)
        //{
        //    _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_projectItemGroup}]")!.AppendChild($"<ProjectReference Include=\"{path}\" />");
        //    _includes.Add(project);
        //}
    }

    public void AddContent(string path)
    {
        if (
            _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_contentItemGroup}]/Content[@Include=\"{path}\"]") is null
        )
        {
            _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_contentItemGroup}]")!.AppendChild(@$"<Content Include=""{path}"">

    <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
    <CopyToPublishDirectory>Always</CopyToPublishDirectory>
</Content>");
        }
    }

    public bool Compile()
    {
        LibraryFile = null;
        ExeFile = null;

        CreateProjectFile();

        //StringBuilder sbSources = new();

        //XPathNodeIterator ni = _xpathNavigator.Select($"/Project/ItemGroup[{s_packageItemGroup}]/PackageReference");
        //while (ni.MoveNext())
        //{
        //    string source = ni.Current!.GetAttribute("Source", string.Empty);
        //    if(sbSources.Length > 0)
        //    {
        //        sbSources.Append(';');
        //    }
        //    sbSources.Append(source);
        //}

        //ni = _xpathNavigator.Select($"/Project/ItemGroup[{s_packageItemGroup}]/PackageReference");
        //while (ni.MoveNext())
        //{
        //    string package = ni.Current!.GetAttribute("Include", string.Empty);
        //    string version = ni.Current!.GetAttribute("Version", string.Empty);
        //    if (!RunDotnet($"add \"{ProjectFileName}\" package {package} --version {version}{(sbSources.Length > 0 ? $" --source \"{sbSources}\"" : string.Empty)}"))
        //    {
        //        return false;
        //    }
        //}

        foreach(var item in _projects)
        {
            if (!RunDotnet($"add \"{ProjectFileName}\" reference \"{item}\""))
            {
                return false;
            }
        }

        foreach (var item in _packages)
        {
            if (!RunDotnet($"add \"{ProjectFileName}\" package {item.Item1} --version {item.Item2}{(!string.IsNullOrEmpty(item.Item3) ? $" --source \"{item.Item3}\"" : string.Empty)}"))
            {
                return false;
            }
        }

        //foreach (Project include in _includes)
        //{
        //    include.DotnetEvent += Include_DotnetEvent;
        //    include.Configuration = Configuration;
        //    include.TargetFramework = TargetFramework;
        //    include.IsVerbose = IsVerbose;
        //    if (!include.Compile())
        //    {
        //        include.DotnetEvent -= Include_DotnetEvent;
        //        return false;
        //    }
        //    include.DotnetEvent -= Include_DotnetEvent;
        //}

        bool result = RunDotnet($"build \"{ProjectFileName}\" --configuration {Configuration} --output {OutputDirectory} --use-current-runtime");

        if (result)
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
        return result;
    }

    private void Include_DotnetEvent(object? sender, DotnetEventArgs args)
    {
        DotnetEvent?.Invoke(sender, args);
    }

    public void Dispose()
    {
        if (IsTemporary && Directory.Exists(SourceDirectory))
        {
            File.WriteAllText(Path.Combine(SourceDirectory, _appDataDirectory), string.Empty);
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

    private void CreateProjectFile()
    {
        XmlWriterSettings xws = new()
        {
            Indent = true,
        };
        XmlWriter xw = XmlWriter.Create(ProjectFileName, xws);
        _project.WriteTo(xw);
        xw.Close();
        if (IsTemporary && IsVerbose)
        {
            Console.WriteLine($"Temporary project {Name} was created at {SourceDirectory}.");
        }
    }

    public string ProjectFileToString()
    {
        StringBuilder sb = new();
        XmlWriterSettings xws = new()
        {
            Indent = true,
        };
        XmlWriter xw = XmlWriter.Create(sb, xws);
        _project.WriteTo(xw);
        xw.Flush();
        return sb.ToString();
    }

    private string GetTemporaryDirectory()
    {
        string appDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), _appDataDirectory);
        string tempDirectory;
        for(tempDirectory = Path.Combine(appDataDirectory, Path.GetRandomFileName()); Directory.Exists(tempDirectory); tempDirectory = Path.Combine(appDataDirectory, Path.GetRandomFileName())) { }
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }

}
