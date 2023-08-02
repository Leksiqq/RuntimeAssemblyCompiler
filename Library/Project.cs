
using System.Diagnostics;
using System.Runtime.Loader;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace Net.Leksi.RuntimeAssemblyCompiler;

public class Project : IDisposable
{
    private const string s_defaultSdk = "Microsoft.NET.Sdk";
    private const string s_libraryOutputType = "Library";
    private const string s_exeOutputType = "Exe";
    private const int s_packageItemGroup = 2;
    private const int s_projectItemGroup = 3;
    private const int s_contentItemGroup = 1;
    private const string s_projectFileName = "Project.csproj";
    private const string s_outputDirName = "bin";

    private readonly List<Project> _includes = new();

    private XmlDocument _project = new XmlDocument();
    private XPathNavigator _xpathNavigator;

    public string Name { get; private set; }
    public string ProjectDirectory { get; private set; }
    public bool IsTemporary { get; private set; }
    public string LastOutput { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;

    public XmlDocument ProjectXml
    {
        get => _project;
        set
        {
            _project = value;
            _xpathNavigator = _project.CreateNavigator()!;
            _includes.Clear();
            LastOutput = string.Empty;
            LastError = string.Empty;
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
    public string ProjectFileName => Path.Combine(ProjectDirectory, s_projectFileName);

    public Project(string name, string? targetDirectory = null)
    {
        IsTemporary = targetDirectory is null;
        Name = name;
        ProjectDirectory = targetDirectory ?? GetTemporaryDirectory();
        _project.LoadXml($@"<Project Sdk=""{s_defaultSdk}"">
    <PropertyGroup>
        <TargetFramework>net{Environment.Version.Major}.{Environment.Version.Minor}</TargetFramework>
        <OutputType>{s_libraryOutputType}</OutputType>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <AssemblyName>{Name}</AssemblyName>
    </PropertyGroup>
    <ItemGroup/>
    <ItemGroup/>
    <ItemGroup/>
    <ItemGroup/>
</Project>");
        _xpathNavigator = _project.CreateNavigator()!;

        Console.WriteLine(ProjectDirectory);
    }

    ~Project()
    {
        Dispose();
    }

    public void AddPackage(string name, string version, string? source = null)
    {
        if (
            _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_packageItemGroup}]/PackageReference[@Include=\"{name}\"]") is XPathNavigator node
            && node.SelectSingleNode("@Version") is XPathNavigator versionAttr
            && node.SelectSingleNode("@Source") is XPathNavigator sourceAttr
        )
        {
            versionAttr.SetValue(version);
            sourceAttr.SetValue(source ?? string.Empty);
        }
        else
        {
            _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_packageItemGroup}]")!.AppendChild($"<PackageReference Include=\"{name}\" Version=\"{version}\"  Source=\"{source ?? string.Empty}\" />");
        }
    }

    public void AddProject(string path)
    {
        if (
            _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_projectItemGroup}]/ProjectReference[@Include=\"{path}\"]") is null
        )
        {
            _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_projectItemGroup}]")!.AppendChild($"<ProjectReference Include=\"{path}\" />");
        }
    }

    public void AddProject(Project project)
    {
        string path = project.ProjectFileName;
        if (
            _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_projectItemGroup}]/ProjectReference[@Include=\"{path}\"]") is null
        )
        {
            _xpathNavigator.SelectSingleNode($"/Project/ItemGroup[{s_projectItemGroup}]")!.AppendChild($"<ProjectReference Include=\"{path}\" />");
            _includes.Add(project);
        }
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
        bool result = Compile(0);
        if (result)
        {
            foreach (string path in Directory.GetFiles(Path.Combine(ProjectDirectory, s_outputDirName)))
            {
                if ((".dll".Equals(Path.GetExtension(path)) || ".exe".Equals(Path.GetExtension(path))) && !path.Equals(LibraryFile))
                {
                    try
                    {
                        AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                    }
                    catch { }
                }
            }
        }
        return result;
    }

    public void Dispose()
    {
        if (IsTemporary && Directory.Exists(ProjectDirectory))
        {
            try
            {
                Directory.Delete(ProjectDirectory, true);
            }
            catch { }
        }
    }

    private bool Compile(int level)
    {
        LibraryFile = null;
        ExeFile = null;

        string outputDir = Path.Combine(ProjectDirectory, s_outputDirName);

        CreateProjectFile();

        XPathNodeIterator ni = _xpathNavigator.Select($"/Project/ItemGroup[{s_packageItemGroup}]/PackageReference");
        while (ni.MoveNext())
        {
            string package = ni.Current!.GetAttribute("Include", string.Empty);
            string version = ni.Current!.GetAttribute("Version", string.Empty);
            string source = ni.Current!.GetAttribute("Source", string.Empty);
            if (!RunDotnet($"add \"{ProjectFileName}\" package {package} -v {version}{(!string.IsNullOrEmpty(source) ? $" --source \"{source}\"" : string.Empty)}"))
            {
                return false;
            }
        }

        foreach (Project include in _includes)
        {
            include.Configuration = Configuration;
            if (!include.Compile(level + 1))
            {
                LastOutput = include.LastOutput;
                LastError = include.LastError;
                return false;
            }
        }

        bool result = RunDotnet($"build \"{ProjectFileName}\" -c {Configuration} -o {outputDir} --ucr");

        if (result)
        {
            LibraryFile = Path.Combine(outputDir, $"{Name}.dll");
            if (IsExecutable)
            {
                ExeFile = Path.Combine(outputDir, $"{Name}.exe");
            }
        }
        return result;
    }

    private bool RunDotnet(string arguments)
    {
        Process dotnet = new();

        dotnet.StartInfo = new()
        {
            FileName = @"C:\Program Files\dotnet\dotnet.exe",
            Arguments = arguments,
            //RedirectStandardOutput = true,
            //RedirectStandardError = true,
            //StandardOutputEncoding = LogEncoding,
            //StandardErrorEncoding = LogEncoding,
        };
        //LastOutput = string.Empty;
        //LastError = string.Empty;

        //StringBuilder sbError = new();

        //dotnet.ErrorDataReceived += (s, e) => sbError.Append(e.Data);

        dotnet.Start();

        //dotnet.BeginErrorReadLine();
        //LastOutput = dotnet.StandardOutput.ReadToEnd();

        if (!dotnet.HasExited)
        {
            dotnet.WaitForExit();
        }
        //LastError = sbError.ToString();

        return dotnet.ExitCode == 0;
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
        string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }

}
