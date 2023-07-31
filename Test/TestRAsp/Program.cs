using Net.Leksi.RuntimeAssemblyCompiler;
using System.Diagnostics;
using System.Reflection;

namespace TestRAC;

internal class Program
{
    //static void Main(string[] args)
    //{
    //    using Project project = new("Test");
    //    project.AddPackage("NUnit", "3.13.3");
    //    Assembly ass = project.Compile();
    //    Console.WriteLine(ass);
    //    Console.WriteLine(project.LastOutput);
    //    Console.ReadKey();
    //}
    static void Main(string[] args)
    {
        using Project server = new("Server")
        {
            Sdk = "Microsoft.NET.Sdk.Web",
            IsExecutable = true,
        };
        server.AddPackage("NUnit", "3.13.3");
        Console.WriteLine(server.ProjectFileToString());
        File.WriteAllText(Path.Combine(server.TargetDirectory, "Program.cs"), @"
using NUnit.Framework;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
Configure.Application(app);
app.Run();

[Test]
void Test()
{
}
");
        using Project config = new("Config")
        {
            Sdk = "Microsoft.NET.Sdk.Web",
            IsExecutable = false,
        };
        File.WriteAllText(Path.Combine(config.TargetDirectory, "Configure.cs"), @"
public static class Configure
{
    public static void Application(WebApplication app)
    {
        app.MapGet(""/"", () => ""Hello world!"");
    }

}
");
        server.AddProject(config);
        bool ok = server.Compile();
        Console.WriteLine(ok);
        if (!ok)
        {
            Console.WriteLine(server.LastOutput);
            if(!string.IsNullOrEmpty(server.LastError))
            {
                Console.WriteLine(server.LastError);
            }
        }
        else
        {
            Process run = new();
            run.StartInfo = new()
            {
                FileName = server.ExeFile,
            };
            run.Start();
            run.WaitForExit();
        }
    }
}