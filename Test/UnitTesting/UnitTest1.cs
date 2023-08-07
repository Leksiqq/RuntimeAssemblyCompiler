using Net.Leksi.RuntimeAssemblyCompiler;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Net.Leksi.Rac.UnitTesting;

public class Tests
{
    const int s_maxLevel = 1;
    const int s_numTreeChildren = 3;
    const int s_numGraphChildren = 3;
    const int s_numOtherProperties = 3;
    const int s_selfDependentBase = 1;
    const int s_isPackableBase = 1;

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
        Project.IsUnitTesting = true;
        Project.ClearTemporary(true);
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

        Action<Node, int> onNewNode = (node, level) =>
        {
            nodes.Add(node);
        };
        Node root = CreateDependencyTree(null, rnd, 0, onNewNode);

        Assert.That(nodes.Count, Is.EqualTo(((int)Math.Round(Math.Pow(s_numTreeChildren, s_maxLevel + 1))) / (s_numTreeChildren - 1)));

        ExtendDependencyTreeToGraph(nodes, rnd);

        foreach (Node node in nodes)
        {
            CreateClassSource(node);
            Assert.That(Directory.Exists(node.Project.SourceDirectory), node.Project.SourceDirectory);
        }

        for(int i = 0; i < nodes.Count; ++i)
        {
            Console.WriteLine($"{i}) {nodes[i].Project.ProjectFile} [{string.Join(',', nodes[i].Children.Select(n => n.Project.Name))}]");
        }

        foreach (Node node in nodes.Where(n => n.IsPackable))
        {
            node.Project.DotnetEvent += _project_DotnetEvent;
            node.Project.Compile();
            node.Project.DotnetEvent -= _project_DotnetEvent;
        }

        foreach (Node node in nodes)
        {
            foreach (Node child in node.Children.Where(n => n != node))
            {
                if (child.Project.GeneratePackage)
                {
                    node.Project.AddPackage(child.Project);
                }
                else
                {
                    node.Project.AddProject(child.Project);
                }
            }
        }

        root.Project.DotnetEvent += _project_DotnetEvent;
        root.Project.Compile();
        root.Project.DotnetEvent -= _project_DotnetEvent;

        foreach(Node node in nodes)
        {
            string? library = null;
            if(node.Project.GeneratePackage || node == root) 
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
            object? nodeObject = Activator.CreateInstance(node.Type);
            Assert.That(nodeObject, Is.Not.Null);
            PropertyInfo? magicProp = node.Type.GetProperty("Magic");
            Assert.That(magicProp, Is.Not.Null);
            Assert.That(magicProp.GetValue(nodeObject), Is.EqualTo(node.MagicWord));
        }


        nodes.ForEach(n => n.Project.Dispose());
    }

    private void ExtendDependencyTreeToGraph(List<Node> nodes, Random rnd)
    {
        List<int>?[] g = new List<int>[nodes.Count + 1];
        int[] cl = new int[nodes.Count + 1];
        Array.Fill(g, null);

        foreach (Node node in nodes)
        {
            if (node.Parent is { })
            {
                if (g[node.Id] is null)
                {
                    g[node.Id] = new List<int>();
                }
                g[node.Id]!.Add(node.Parent.Id);
            }
        }

        Func<int, bool> dfs = i => false;
        dfs = from =>
        {
            cl[from] = 1;
            if(g[from] is { })
            {
                foreach (int to in g[from]!)
                {
                    if (cl[to] == 1)
                    {
                        return true;
                    }
                    if (cl[to] == 0)
                    {
                        return dfs(to);
                    }
                }
            }
            cl[from] = 2;
            return false;
        };

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (Node from in nodes)
            {
                foreach (Node to in nodes)
                {
                    if(
                        from != to
                        && (g[from.Id] is null || !g[from.Id]!.Contains(to.Id))
                        && (g[to.Id] is null || !g[to.Id]!.Contains(from.Id))
                    )
                    {
                        if(g[from.Id] is null)
                        {
                            g[from.Id] = new List<int>();
                        }
                        g[from.Id]!.Add(to.Id);
                        Array.Fill(cl, 0);
                        if (!dfs(from.Id))
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

    }

    private void _project_DotnetEvent(object? sender, DotnetEventArgs args)
    {
        if (!args.Success)
        {
            Assert.Fail($"dotnet: {args.Arguments}\n{args.Output}\n{args.Error}");
        }
    }

    private void CreateClassSource(Node node)
    {
        if(node.Project is null)
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
                po.Name = node.Name;
                if (!string.IsNullOrEmpty(node.Namespace))
                {
                    po.Namespace = node.Namespace;
                }
            }
            node.Project = Project.Create(po);
            File.WriteAllText(Path.Combine(node.Project.SourceDirectory, $"{node.Project.FullName}.magic.txt"), node.MagicWord);
            node.Project.AddContent($"{node.Project.FullName}.magic.txt");
            FileStream fileStream = File.Create(Path.Combine(node.Project.SourceDirectory, $"{node.Project.Name}.cs"));
            StreamWriter sw = new(fileStream);
            sw.WriteLine("using System.Reflection;");
            sw.WriteLine($"namespace {node.Project.Namespace};");
            sw.WriteLine($"public class {node.Project.Name}");
            sw.WriteLine("{");
            int i = 0;
            foreach (Node child in node.Children)
            {
                CreateClassSource(child);
                sw.WriteLine($"    public {child.Project.FullName} Prop{i} {{ get; set; }}");
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
            sw.WriteLine("}");
            sw.Close();
        }
    }

    private Node CreateDependencyTree(Node? parent, Random rnd, int level, Action<Node, int> onNewNode)
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

    private string MakeMagicWord(Random rnd)
    {
        StringBuilder sb = new();
        for (int i = 0; i < 5; ++i)
        {
            sb.Append((char)rnd.Next(33, 127));
        }
        return sb.ToString();
    }

    private class Node
    {
        internal static int s_genId = 0;
        internal int Id { get; init; } = Interlocked.Increment(ref s_genId);
        internal Project Project { get; set; }
        internal string Name { get; set; }
        internal bool IsPackable { get; init; } = false;
        internal List<Node> Children { get; init; } = new();
        internal Node? Parent { get; init; } = null;
        internal string MagicWord { get; init; } = string.Empty;
        internal string FullName { get; set; }
        internal string Namespace { get; set; }
        internal Type? Type { get; set; }
    }
}

