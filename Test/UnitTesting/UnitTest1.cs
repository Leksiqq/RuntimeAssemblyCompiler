using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Net.Leksi.RACWebApp.Common;
using Net.Leksi.RuntimeAssemblyCompiler;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Net.Leksi.Rac.UnitTesting;

public class Tests
{
    const int s_maxLevel = 1;
    const int s_numTreeChildren = 3;
    const int s_numOtherProperties = 3;
    const int s_isPackableBase = 1;
    const int s_maxObjectsOfType = 3;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Trace.Listeners.Clear();
        Trace.Listeners.Add(new ConsoleTraceListener());
        Trace.AutoFlush = true;
    }

    [Test]
    [TestCase(1356655665)]
    public void Test1(int seed)
    {
        DateTime start = DateTime.Now;
        Project.IsUnitTesting = true;
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

        string commonVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<MyAttribute>()!.Version;

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
            Assert.That(Directory.Exists(node.Project!.SourceDirectory), node.Project.SourceDirectory);
        }

        foreach (Node node in nodes.Where(n => n.IsPackable))
        {
            node.Project!.AddPackage("Net.Leksi.RACWebApp.Common", commonVersion, Path.GetDirectoryName(GetType().Assembly.Location));
            node.Project!.DotnetEvent += ProjectDotnetEvent;
            node.Project.Compile();
            node.Project.DotnetEvent -= ProjectDotnetEvent;
        }

        Console.WriteLine($"{DateTime.Now - start}: Packages compiled");

        foreach (Node node in nodes.Where(n => !n.IsPackable))
        {
            node.Project!.AddPackage("Net.Leksi.RACWebApp.Common", commonVersion, Path.GetDirectoryName(GetType().Assembly.Location));
            foreach (Node child in node.Children.Where(n => n != node))
            {
                if (child.Project!.GeneratePackage)
                {
                    node.Project!.AddPackage(child.Project);
                }
                else
                {
                    node.Project!.AddProject(child.Project);
                }
            }
        }

        root.Project!.DotnetEvent += ProjectDotnetEvent;
        root.Project.Compile();
        root.Project.DotnetEvent -= ProjectDotnetEvent;

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

        object nodeObject = host.Services.GetRequiredService(root.Type!);

        Dictionary<Type, HashSet<object>> foundObjects = new();

        WalkAssert(root, foundObjects, nodes, host);

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
                mgc.SameTypeProperty = host.Services.GetRequiredService(obj.GetType());
                Node? node = nodes.Where(n => n.Type == obj.GetType()).FirstOrDefault();

                Assert.That(node, Is.Not.Null);

                Assert.That(mgc.Magic, Is.EqualTo(node.MagicWord));

                foreach(PropertyInfo pi in obj.GetType().GetProperties())
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
                    nodesToFindAliens[i].Children.Add(aliens[0]);
                    if (!indirectDependents.ContainsKey(aliens[0]))
                    {
                        indirectDependents.Add(aliens[0], new List<Node>());
                    }
                    indirectDependents[aliens[0]].Add(nodesToFindAliens[i]);
                    if (aliens.Count == 1)
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

    private static void ExtendDependencyTreeToGraph1(List<Node> nodes, Random rnd)
    {
        List<int>[] g = new List<int>[nodes.Count + 1];
        for (int i = 1; i < g.Length; ++i)
        {
            g[i] = new List<int>();
        }
        int[] cl = new int[nodes.Count + 1];

        foreach (Node node in nodes)
        {
            if (node.Parent is { })
            {
                g[node.Id]!.Add(node.Parent.Id);
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (Node to in nodes.Where(n => !n.IsPackable))
            {
                foreach (Node from in nodes.Where(n =>
                    n != to
                    && !to.Children.Contains(n)
                    && !n.Children.Contains(to)
                ))
                {
                    g[from.Id]!.Add(to.Id);
                    Array.Fill(cl, 0);
                    cl[from.Id] = 1;
                    if (!Dfs(to.Id, g, cl))
                    {
                        to.Children.Add(from);
                        changed = true;
                        break;
                    }
                    g[from.Id]!.RemoveAt(g[from.Id]!.Count - 1);
                }
            }
        }

    }

    private static bool Dfs(int from, List<int>[] g, int[] cl)
    {
        bool result = false;
        cl[from] = 1;
        foreach (int to in g[from]!)
        {
            if (
                cl[to] == 1
                || (
                    cl[to] == 0
                    && Dfs(to, g, cl)
                )
            )
            {
                result = true;
                break;
            }
        }
        cl[from] = 2;
        return result;
    }

    private static void ProjectDotnetEvent(object? sender, DotnetEventArgs args)
    {
        if (!args.Success)
        {
            Assert.Fail($"dotnet: {args.Arguments}\n{args.Output}\n{args.Error}");
        }
    }

    private static void CreateClassSource(Node node, Random rnd)
    {
        if (node.Project is null)
        {
            ProjectOptions po = new()
            {
                GeneratePackage = node.IsPackable,
                TargetFramework = "net6.0-windows",
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
            File.WriteAllText(Path.Combine(node.Project.SourceDirectory!, $"{node.Project.FullName}.magic.txt"), node.MagicWord);
            node.Project.AddContent($"{node.Project.FullName}.magic.txt");
            FileStream fileStream = File.Create(Path.Combine(node.Project.SourceDirectory!, $"{node.Project.Name}.cs"));
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

