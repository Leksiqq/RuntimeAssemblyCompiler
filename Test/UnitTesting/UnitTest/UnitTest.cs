using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using Net.Leksi.RACWebApp.Common;
using Net.Leksi.RuntimeAssemblyCompiler;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace Net.Leksi.Rac.UnitTesting;

public class Tests
{
    const int s_maxLevel = 4;
    const int s_numTreeChildren = 3;
    const int s_numOtherProperties = 3;
    const int s_isPackableBase = 1;
    const int s_maxObjectsOfType = 3;
    const int s_maxChildren = 10;
    const string s_external = "External";
    const string s_permanent = "Permanent";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Trace.Listeners.Clear();
        Trace.Listeners.Add(new ConsoleTraceListener());
        Trace.AutoFlush = true;
    }

    [Test]
    [TestCase(-1)]
    public void Test1(int seed)
    {
        DateTime start = DateTime.Now;
        Project.IsUnitTesting = true;

        Assert.That(Project.IsUnitTesting, Is.True);

        if (seed == -1)
        {
            seed = (int)(long.Parse(
                new string(
                    DateTime.UtcNow.Ticks.ToString().Reverse().ToArray()
                )
            ) % int.MaxValue);
        }
        Console.WriteLine($"seed: {seed}");
        Random rnd = new Random(seed);
        List<Node> nodes = new();

        string commonPackageName = Assembly.GetExecutingAssembly().GetCustomAttribute<MyAttribute>()!.CommonPackageName;
        string commonPackageVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<MyAttribute>()!.CommonPackageVersion;
        string projectDir = Assembly.GetExecutingAssembly().GetCustomAttribute<MyAttribute>()!.ProjectDir;

        using Project permanentProject = Project.Create(new ProjectOptions
        {
            Name = s_permanent,
            Namespace = GetType().Namespace!,
            ProjectDir = Path.Combine(projectDir, "..", s_permanent),
        });

        InvalidOperationException ex9 = Assert.Throws<InvalidOperationException>(() => Project.IsUnitTesting = false);
        Assert.That(ex9.Message, Is.EqualTo("Cannot change IsUnitTesting!"));

        ArgumentException ex = Assert.Throws<ArgumentException>(() => Project.Create(new ProjectOptions { FullName = ".FullName" }));
        Assert.That(ex.Message, Is.EqualTo("options.FullName"));

        ArgumentNullException ex1 = Assert.Throws<ArgumentNullException>(() => Project.Create(new ProjectOptions { Namespace = "qq" }));
        Assert.That(ex1.Message, Is.EqualTo("Value cannot be null. (Parameter 'options.Name')"));

        ArgumentException ex6 = Assert.Throws<ArgumentException>(() => Project.Create(new ProjectOptions { Namespace = "qq", FullName = "a.b" }));
        Assert.That(ex6.Message, Is.EqualTo("options.Namespace cannot be set with options.FullName!"));

        ArgumentException ex7 = Assert.Throws<ArgumentException>(() => Project.Create(new ProjectOptions { Name = "qq", FullName = "a.b" }));
        Assert.That(ex7.Message, Is.EqualTo("options.Name cannot be set with options.FullName!"));

        using (
            Project proj1 = Project.Create(
                new ProjectOptions 
                { 
                    FullName = "qq", 
                    OutputType = OutputType.Exe,
                    TargetFramework = "net6.0-windows",
                }
            )
        )
        {
            Assert.Multiple(() =>
            {
                Assert.That(proj1.Name, Is.EqualTo("qq"));
                Assert.That(proj1.FullName, Is.EqualTo("qq"));
                Assert.That(proj1.Namespace, Is.Empty);
            });
            proj1.AddProject(permanentProject);
            proj1.AddProject(Path.Combine(projectDir, "..", s_external, $"{s_external}.csproj"));
            proj1.AddProject(Path.Combine(projectDir, "..", $"{s_external}1", $"{s_external}1.csproj"));
            proj1.AddPackage(commonPackageName, commonPackageVersion, Path.GetDirectoryName(GetType().Assembly.Location));
            File.WriteAllText(Path.Combine(proj1.ProjectDir!, "Program.cs"), $@"
using {GetType().Namespace!};
using Net.Leksi.RACWebApp.Common;

Factory factory = new();
factory.Value = ""Hell"";
{s_permanent} val1 = new {s_permanent} {{ Value = factory.GetValue(typeof(string)).ToString() }};
factory.Value = ""o wo"";
{s_external} val2 = new {s_external} {{ Value = factory.GetValue(typeof(string)).ToString() }};
factory.Value = ""rld!"";
{s_external}1 val3 = new {s_external}1 {{ Value = factory.GetValue(typeof(string)).ToString() }};
Console.Write($""{{val1.Value}}{{val2.Value}}{{val3.Value}}"");

class Factory: IFactory
{{
    public string Value {{ get; set; }}
    public object? GetValue(Type type)
    {{
        return Value;
    }}
}}
"
            );
            proj1.Compile();
            Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = proj1.ExeFile,
                RedirectStandardOutput = true,
            };
            process.Start();

            string output = process.StandardOutput.ReadToEnd();

            process.WaitForExit();
            int res = process.ExitCode;

            Assert.That(output, Is.EqualTo("Hello world!"));

        }
        Action<Node, int> onNewNode = (node, level) =>
        {
            nodes.Add(node);
        };
        Node root = CreateDependencyTree(null, rnd, 0, onNewNode);

        Assert.That(nodes.Count, Is.EqualTo(((int)Math.Round(Math.Pow(s_numTreeChildren, s_maxLevel + 1))) / (s_numTreeChildren - 1)));

        Console.WriteLine($"{DateTime.Now - start}: Tree built: (nodes: {nodes.Count}, edges: {nodes.Select(n => n.Children.Count).Sum()})");

        ExtendDependencyTreeToGraph(nodes, rnd);

        Console.WriteLine($"{DateTime.Now - start}: Graph built: (nodes: {nodes.Count}, edges: {nodes.Select(n => n.Children.Count).Sum()})");

        foreach (Node node in nodes)
        {
            CreateClassSource(node, rnd);
            Assert.That(Directory.Exists(node.Project!.ProjectDir), node.Project.ProjectDir);
            node.Project.AddProject(permanentProject);
            ArgumentException ex2 = Assert.Throws<ArgumentException>(() => node.Project.AddProject(permanentProject));
            Assert.That(ex2.Message, Is.EqualTo($"Project {permanentProject.FullName} is already added!"));
            node.Project.AddProject(Path.Combine(projectDir, "..", s_external, $"{s_external}.csproj"));
            ArgumentException ex3 = Assert.Throws<ArgumentException>(() => node.Project.AddProject(Path.Combine(projectDir, "..", s_external, $"{s_external}.csproj")));
            Assert.That(ex3.Message, Is.EqualTo($"Project {Path.Combine(projectDir, "..", s_external, $"{s_external}.csproj")} is already added!"));
        }

        foreach (Node node in nodes.Where(n => n.IsPackable))
        {
            node.Project!.AddPackage(commonPackageName, commonPackageVersion, Path.GetDirectoryName(GetType().Assembly.Location));
            ArgumentException ex4 = Assert.Throws<ArgumentException>(() => node.Project!.AddPackage(commonPackageName, commonPackageVersion, Path.GetDirectoryName(GetType().Assembly.Location)));
            Assert.That(ex4.Message, Is.EqualTo($"Package {commonPackageName} is already added!"));
            node.Project.Compile();
        }

        Console.WriteLine($"{DateTime.Now - start}: Packages compiled");

        foreach (Node node in nodes.Where(n => !n.IsPackable))
        {
            node.Project!.AddPackage(commonPackageName, commonPackageVersion, Path.GetDirectoryName(GetType().Assembly.Location));
            foreach (Node child in node.Children.Where(n => n != node))
            {
                if (child.Project!.GeneratePackage)
                {
                    node.Project!.AddPackage(child.Project);
                    ArgumentException ex5 = Assert.Throws<ArgumentException>(() => node.Project!.AddPackage(child.Project));
                    Assert.That(ex5.Message, Is.EqualTo($"Package {child.Project.FullName} is already added!"));
                }
                else
                {
                    node.Project!.AddProject(child.Project);
                    ArgumentException ex5 = Assert.Throws<ArgumentException>(() => node.Project!.AddProject(child.Project));
                    Assert.That(ex5.Message, Is.EqualTo($"Project {child.Project.FullName} is already added!"));
                }
            }
        }

        Assert.That(root.Project!.GetLibraryFile(permanentProject), Is.Null);

        root.Project!.Compile();

        IHostBuilder hostBuilder = Host.CreateDefaultBuilder();
        hostBuilder.ConfigureServices(services => services.AddSingleton<IFactory, Factory>());

        foreach (Node node in nodes)
        {
            string? library = null;
            if (node.Project!.GeneratePackage || node == root)
            {
                library = node.Project.LibraryFile;
            }
            else
            {
                Assert.That(node.Project.LibraryFile, Is.Null);
                library = root.Project.GetLibraryFile(node.Project);
            }
            Assert.That(library, Is.Not.Null);
            node.Type = Assembly.LoadFrom(library)?.GetType(node.Project.FullName);
            Assert.That(node.Type, Is.Not.Null);
            hostBuilder.ConfigureServices(services => services.AddTransient(node.Type!));
        }

        IHost host = hostBuilder.Build();

        (host.Services.GetRequiredService<IFactory>() as Factory)!.MaxObjectsOfType = s_maxObjectsOfType;
        (host.Services.GetRequiredService<IFactory>() as Factory)!.Random = rnd;

        object nodeObject = host.Services.GetRequiredService<IFactory>().GetValue(root.Type!)!;

        Dictionary<Type, HashSet<object>> foundObjects = new();

        WalkAssert(nodeObject, foundObjects, nodes, host);

        Assert.That(foundObjects.Where(e => $"{GetType().Namespace!}.{s_permanent}".Equals(e.Key.FullName)).Count(), Is.EqualTo(1));
        Assert.That(foundObjects.Where(e => $"{GetType().Namespace!}.{s_permanent}".Equals(e.Key.FullName)).First().Value.Count(), Is.EqualTo(nodes.Count * s_maxObjectsOfType));
        Assert.That(foundObjects.Where(e => $"{GetType().Namespace!}.{s_external}".Equals(e.Key.FullName)).Count(), Is.EqualTo(1));
        Assert.That(foundObjects.Where(e => $"{GetType().Namespace!}.{s_external}".Equals(e.Key.FullName)).First().Value.Count(), Is.EqualTo(nodes.Count * s_maxObjectsOfType));

        foreach (Type? type in new Type[] {typeof(string)} .Concat(nodes.Select(n => n.Type)))
        {
            Assert.That(type, Is.Not.Null);
            Assert.That(foundObjects.ContainsKey(type), Is.True, type.ToString());
            Assert.That(foundObjects[type].Count, Is.EqualTo(s_maxObjectsOfType));
        }

        nodes.ForEach(n => n.Project!.Dispose());

    }

    private void WalkAssert(object? obj, Dictionary<Type, HashSet<object>> foundObjects, List<Node> nodes, IHost host)
    {
        Assert.That(obj, Is.Not.Null);

        if (!foundObjects.ContainsKey(obj.GetType()))
        {
            foundObjects.Add(obj.GetType(), new HashSet<object>());
        }
        if (foundObjects[obj.GetType()].Add(obj))
        {
            if (obj is IMagicable mgc)
            {
                mgc.SameTypeProperty = host.Services.GetRequiredService<IFactory>().GetValue(obj.GetType())!;

                Assert.That(mgc.SameTypeProperty, Is.EqualTo(obj.GetType().GetProperty("Prop0")!.GetValue(obj)));
                Node? node = nodes.Where(n => n.Type == obj.GetType()).FirstOrDefault();

                Assert.That(node, Is.Not.Null);

                Assert.That(mgc.Magic, Is.EqualTo(node.MagicWord));

                foreach(PropertyInfo pi in obj.GetType().GetProperties().Where(p => !"Magic".Equals(p.Name)))
                {
                    WalkAssert(pi.GetValue(obj), foundObjects, nodes, host);
                }
            }
        }
    }

    private static void ExtendDependencyTreeToGraph(List<Node> nodes, Random rnd)
    {
        Dictionary<Node, List<Node>> indirectDependents = new();

        foreach (Node node in nodes)
        {
            if (node.Parent is { })
            {
                indirectDependents.Add(node, new List<Node>() { node.Parent });
            }
        }

        List<Node> nodesToFindAliens = nodes.Where(n => !n.IsPackable).ToList();
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = nodesToFindAliens.Count - 1; i >= 0; --i)
            {
                List<Node> aliens = nodes.Where(n => !nodesToFindAliens[i].Children.Contains(n)).ToList();
                WalkDependents(nodesToFindAliens[i], aliens, indirectDependents);
                if (!aliens.Any())
                {
                    nodesToFindAliens.RemoveAt(i);
                }
                else
                {
                    int pos = rnd.Next(aliens.Count);
                    nodesToFindAliens[i].Children.Add(aliens[pos]);
                    if (!indirectDependents.ContainsKey(aliens[pos]))
                    {
                        indirectDependents.Add(aliens[pos], new List<Node>());
                    }
                    indirectDependents[aliens[pos]].Add(nodesToFindAliens[i]);
                    if (aliens.Count == 1 || nodesToFindAliens[i].Children.Count >= s_maxChildren)
                    {
                        nodesToFindAliens.RemoveAt(i);
                    }
                    changed = true;
                }
            }
        }
    }

    private static void WalkDependents(Node node, List<Node> aliens, Dictionary<Node, List<Node>> indirectDependents)
    {
        aliens.Remove(node);
        if (indirectDependents.TryGetValue(node, out List<Node>? indDeps))
        {
            indDeps.ForEach(e => WalkDependents(e, aliens, indirectDependents));
        }
    }

    private void CreateClassSource(Node node, Random rnd)
    {
        if (node.Project is null)
        {
            ProjectOptions po = new()
            {
                GeneratePackage = node.IsPackable,
                TargetFramework = "net6.0-windows",
                OutputType = OutputType.Library,
            };
            if (!string.IsNullOrEmpty(node.FullName))
            {
                po.FullName = node.FullName;
            }
            else
            {
                po.Name = node.Name!;
                if (!string.IsNullOrEmpty(node.Namespace))
                {
                    po.Namespace = node.Namespace;
                }
            }
            node.Project = Project.Create(po);

            File.WriteAllText(Path.Combine(node.Project.ProjectDir!, $"{node.Project.FullName}.magic.txt"), node.MagicWord);
            node.Project.AddContent($"{node.Project.FullName}.magic.txt");

            ArgumentException? ex7 = Assert.Throws<ArgumentException>(() => node.Project.AddContent($"{node.Project.FullName}.magic.txt"));
            Assert.That(ex7.Message, Is.EqualTo($"Content {node.Project.FullName}.magic.txt is already added!"));

            FileStream fileStream = File.Create(Path.Combine(node.Project.ProjectDir!, $"{node.Project.Name}.cs"));
            StreamWriter sw = new(fileStream);
            sw.WriteLine("using System.Reflection;");
            sw.WriteLine("using Net.Leksi.RACWebApp.Common;");
            sw.WriteLine($"namespace {node.Project.Namespace};");
            sw.WriteLine($"public class {node.Project.Name}: IMagicable");
            sw.WriteLine("{");
            int i = 0;
            foreach (Node child in (new Node[] { node }).Concat(node.Children))
            {
                CreateClassSource(child, rnd);
                sw.WriteLine($"    public {child.Project!.FullName} Prop{i} {{ get; set; }}");
                ++i;
            }
            sw.WriteLine($"    public {GetType().Namespace!}.{s_permanent} Prop{i} {{ get; set; }}");
            ++i;
            sw.WriteLine($"    public {GetType().Namespace!}.{s_external} Prop{i} {{ get; set; }}");
            ++i;
            for (int j = 0; j < s_numOtherProperties; ++j)
            {
                sw.WriteLine($"    public string Prop{i} {{ get; set; }}");
                ++i;
            }
            sw.WriteLine(@$"    public string Magic => File.ReadAllText(
            Path.Combine(
                Path.GetDirectoryName(
                    GetType().Assembly.Location
                ), 
                ""{node.Project.FullName}.magic.txt""
            )
        );");
            sw.WriteLine($"    object IMagicable.SameTypeProperty {{ get => Prop0; set => Prop0 = ({node.Project.Name})value; }}");
            sw.WriteLine($@"    public {node.Project.Name}(IFactory factory)
    {{");
            i = 1;
            foreach (Node child in node.Children)
            {
                sw.WriteLine($"        Prop{i} = ({child.Project!.FullName})factory.GetValue(typeof({child.Project!.FullName}));");
                ++i;
            }
            sw.WriteLine($"        Prop{i} = new {GetType().Namespace!}.{s_permanent} {{ Value = (string)factory.GetValue(typeof({GetType().Namespace!}.{s_permanent})) }};");
            ++i;
            sw.WriteLine($"        Prop{i} = new {GetType().Namespace!}.{s_external} {{ Value = (string)factory.GetValue(typeof({GetType().Namespace!}.{s_external})) }};");
            ++i;
            for (int j = 0; j < s_numOtherProperties; ++j)
            {
                sw.WriteLine($"        Prop{i} = (string)factory.GetValue(typeof(string));");
                ++i;
            }
            sw.WriteLine("    }");
            sw.WriteLine("}");
            sw.Close();
        }
    }

    private static Node CreateDependencyTree(Node? parent, Random rnd, int level, Action<Node, int> onNewNode)
    {
        Node result = new()
        {
            IsPackable = (s_isPackableBase > 0 && level == s_maxLevel && rnd.Next(s_isPackableBase) == s_isPackableBase - 1),
            MagicWord = MakeMagicWord(rnd),
            Parent = parent,
        };
        string name = $"Class{result.Id}";
        string @namespace = $"Net.Leksi.NS{result.Id}";

        if (rnd.Next(2) == 1)
        {
            result.FullName = $"{@namespace}.{name}";
        }
        else
        {
            result.Name = name;
            result.Namespace = @namespace;
        }
        if (level < s_maxLevel)
        {
            for (int i = 0; i < s_numTreeChildren; ++i)
            {
                result.Children.Add(CreateDependencyTree(result, rnd, level + 1, onNewNode));
            }
        }
        onNewNode?.Invoke(result, level);
        return result;
    }

    internal static string MakeMagicWord(Random rnd)
    {
        StringBuilder sb = new();
        for (int i = 0; i < 5; ++i)
        {
            char ch = (char)rnd.Next(33, 127);
            if (ch == '"' || ch == '\\')
            {
                sb.Append('\\');
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private class Node
    {
        internal static int s_genId = 0;
        internal int Id { get; init; } = Interlocked.Increment(ref s_genId);
        internal Project? Project { get; set; }
        internal string? Name { get; set; }
        internal bool IsPackable { get; init; } = false;
        internal List<Node> Children { get; init; } = new();
        internal Node? Parent { get; init; } = null;
        internal string MagicWord { get; init; } = string.Empty;
        internal string? FullName { get; set; }
        internal string? Namespace { get; set; }
        internal Type? Type { get; set; }
        internal string Label => FullName ?? (Name is { } ? $"{(string.IsNullOrEmpty(Namespace) ? string.Empty : $"{Namespace}.")}{Name}" : string.Empty);
    }
}

