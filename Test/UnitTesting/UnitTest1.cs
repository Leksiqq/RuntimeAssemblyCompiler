using Net.Leksi.RuntimeAssemblyCompiler;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Text;

namespace Net.Leksi.Rac.UnitTesting;

public class Tests
{
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
        if (seed == -1)
        {
            seed = (int)(long.Parse(
                new string(
                    DateTime.UtcNow.Ticks.ToString().Reverse().ToArray()
                )
            ) % int.MaxValue);
        }
        Assert.Warn($"seed: {seed}");
        Random rnd = new Random(seed);
        Dictionary<int, List<Node>> nodesByLevel = new();
        List<Node> nodes = new();

        Action<Node, int> onNewNode = (node, level) =>
        {
            if(!nodesByLevel.TryGetValue(level, out List<Node>? list))
            {
                list = new List<Node>();
                nodesByLevel.Add(level, list);
            }
            list.Add(node);
            nodes.Add(node);
            Assert.Warn($"new node: {level}: {node.Name}, {node.IsPackageable}, [{string.Join(',', node.Children.Where(n => n.IsPackageable).Select(n => n.Name))}]");
        };
        Node root = CreateDependencyTree(null, rnd, 0, onNewNode);

        foreach (Node node in nodes)
        {
            CreateClassSource(node);
        }
        foreach (Node node in nodes)
        {
            foreach (Node child in node.Children)
            {
                if (child._project.GeneratePackage)
                {
                    node._project.AddPackage(child.Name, "1.0.0", child._project.OutputDirectory);
                }
                else
                {
                    node._project.AddProject(child._project);
                }
            }
        }

        for (int level = 4; level > 0; --level) 
        { 
            foreach(Node node in nodesByLevel[level].Where(n => n.IsPackageable))
            {
                Assert.Warn($"compiling: {node._project.Name}, {node._project.SourceDirectory}");
                node._project.DotnetEvent += _project_DotnetEvent;
                node._project.Compile();
                node._project.DotnetEvent -= _project_DotnetEvent;
            }
        }
        Assert.Warn($"compiling: {root._project.Name}");
        root._project.DotnetEvent += _project_DotnetEvent;
        root._project.Compile();
        root._project.DotnetEvent -= _project_DotnetEvent;
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
        node._project = new Project(new ProjectOptions
        {
            Name = node.Name,
            GeneratePackage = node.IsPackageable,
            TargetFramework = "net6.0-windows7.0",
        });
        File.WriteAllText(Path.Combine(node._project.SourceDirectory, "magic.txt"), node.MagicWord);
        node._project.AddContent("magic.txt");
        FileStream fileStream = File.Create(Path.Combine(node._project.SourceDirectory, $"{node.Name}.cs"));
        StreamWriter sw = new(fileStream);
        sw.WriteLine("using System.Reflection;");
        sw.WriteLine($"public class {node.Name}");
        sw.WriteLine("{");
        int i = 0;
        if (node.Children.Any())
        {
            for (; i < 3; ++i)
            {
                sw.WriteLine($"    public {node.Children[i].Name} Prop{i} {{ get; set; }}");
            }
        }
        for (; i < 10; ++i)
        {
            sw.WriteLine($"    public string Prop{i} {{ get; set; }}");
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
            IsPackageable = (level > 0 && rnd.Next() % 3 == 1),
            Parent = parent,
            MagicWord = MakeMagicWord(rnd),
        };
        if(level < 4)
        {
            for(int i = 0; i < 3; ++i)
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
        internal Node? Parent { get; init; } = null;
        internal List<Node> Children { get; init; } = new();
        internal string MagicWord { get; init; } = string.Empty;
        internal Node()
        {
            Name = $"Class{Interlocked.Increment(ref s_genId)}";
        }
    }
}

