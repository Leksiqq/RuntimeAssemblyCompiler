using Net.Leksi.RuntimeAssemblyCompiler;
using System.Diagnostics;
using System.Text;

namespace Net.Leksi.Rac.UnitTesting;

public class Tests
{
    const int s_maxLevel = 1;
    const int s_numTreeChildren = 3;
    const int s_numGraphChildren = 3;
    const int s_numOtherProperties = 3;
    const int s_selfDependentBase = 12;
    const int s_isPackableBase = 0;

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

        //ExtendDependencyTreeToGraph(nodes, rnd);

        foreach (Node node in nodes)
        {
            CreateClassSource(node);
            Assert.That(Directory.Exists(node._project.SourceDirectory), node._project.SourceDirectory);
        }

        Assert.Multiple(() => {
            foreach (Node node in nodes)
            {
                Assert.That(Directory.Exists(node._project.SourceDirectory), node._project.SourceDirectory);
            }
        });

        int i = 0;
        foreach (Node node in nodes.Where(n => n.IsPackageable))
        {
            node._project.DotnetEvent += _project_DotnetEvent;
            node._project.Compile();
            node._project.DotnetEvent -= _project_DotnetEvent;
        }

        Assert.Multiple(() => {
            foreach (Node node in nodes)
            {
                Assert.That(Directory.Exists(node._project.SourceDirectory), node._project.SourceDirectory);
            }
        });

        foreach (Node node in nodes)
        {
            foreach (Node child in node.Children.Where(n => n != node))
            {
                if (child._project.GeneratePackage)
                {
                    node._project.AddPackage(child._project);
                }
                else
                {
                    node._project.AddProject(child._project);
                }
            }
        }

        Assert.Multiple(() => {
            foreach (Node node in nodes)
            {
                Assert.That(Directory.Exists(node._project.SourceDirectory), node._project.SourceDirectory);
            }
        });

        root._project.DotnetEvent += _project_DotnetEvent;
        root._project.Compile();
        root._project.DotnetEvent -= _project_DotnetEvent;

        nodes.ForEach(n => n._project.Dispose());
    }

    private void ExtendDependencyTreeToGraph(List<Node> nodes, Random rnd)
    {
        foreach(Node node in nodes.Where(n => !n.IsPackageable))
        {
            if(rnd.Next(s_selfDependentBase) == 4)
            {
                node.Children.Add(node);
            }
            while(node.Children.Count < s_numTreeChildren + s_numGraphChildren)
            {
                Node[] avail = nodes.Where(
                    n => n != node
                        && !node.Children.Contains(n)
                        && !node.Ancestors.Contains(n)
                ).ToArray();
                if (!avail.Any())
                {
                    break;
                }
                Node taken = avail[rnd.Next(avail.Length)];
                node.Children.Add(taken);
                taken.Ancestors.Add(node);
                foreach (Node anc in node.Ancestors)
                {
                    taken.Ancestors.Add(anc);
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
        node._project = Project.Create(new ProjectOptions
        {
            Name = node.Name,
            GeneratePackage = node.IsPackageable,
            TargetFramework = "net6.0-windows",
        });
        File.WriteAllText(Path.Combine(node._project.SourceDirectory, "magic.txt"), node.MagicWord);
        node._project.AddContent("magic.txt");
        FileStream fileStream = File.Create(Path.Combine(node._project.SourceDirectory, $"{node.Name}.cs"));
        StreamWriter sw = new(fileStream);
        sw.WriteLine("using System.Reflection;");
        sw.WriteLine($"public class {node.Name}");
        sw.WriteLine("{");
        int i = 0;
        foreach (Node child in node.Children)
        {
            sw.WriteLine($"    public {child.Name} Prop{i} {{ get; set; }}");
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
                ""magic.txt""
            )
        );");
        sw.WriteLine("}");
        sw.Close();
    }

    private Node CreateDependencyTree(Node? parent, Random rnd, int level, Action<Node, int> onNewNode)
    {
        Node result = new()
        {
            IsPackageable = (s_isPackableBase > 0 && level == s_maxLevel && rnd.Next(s_isPackableBase) == 1),
            MagicWord = MakeMagicWord(rnd),
        };
        if(parent is { })
        {
            result.Ancestors.Add(parent);
            foreach(Node anc in parent.Ancestors)
            {
                result.Ancestors.Add(anc);
            }
        }
        if (level < s_maxLevel)
        {
            for(int i = 0; i < s_numTreeChildren; ++i)
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
        for(int i = 0; i < 5; ++i)
        {
            sb.Append((char)rnd.Next(33, 127));
        }
        return sb.ToString();
    }

    private class Node
    {
        internal static int s_genId = 0;
        internal Project _project = null!;
        internal string Name { get; init; }
        internal bool IsPackageable { get; init; } = false;
        internal List<Node> Children { get; init; } = new();
        internal HashSet<Node> Ancestors { get; init; } = new();
        internal string MagicWord { get; init; } = string.Empty;
        internal Node()
        {
            Name = $"Class{Interlocked.Increment(ref s_genId)}";
        }
    }
}

