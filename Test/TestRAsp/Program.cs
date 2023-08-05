using Net.Leksi.RuntimeAssemblyCompiler;
using System.Diagnostics;
using System.Reflection;

namespace TestRAC;

internal class Program
{
//    static void Main2(string[] args)
//    {
//        var builder = WebApplication.CreateBuilder(args);
//        var app = builder.Build();
//        using Project config = new("Config")
//        {
//            Sdk = "Microsoft.NET.Sdk.Web",
//            IsExecutable = false,
//        };
//        File.WriteAllText(Path.Combine(config.TargetDirectory, "Configure.cs"), @"
//using System.Reflection;

//public static class Configure
//{
//    public static void Application(WebApplication app)
//    {
//        app.Run(
//            async context =>  await context.Response.WriteAsync(
//                File.ReadAllText(
//                    Path.Combine(
//                        Path.GetDirectoryName(
//                            Assembly.GetAssembly(typeof(Configure)).Location
//                        ), 
//                        ""Hello.txt""
//                    )
//                )
//            )
//        );
//    }

//}
//");
//        File.WriteAllText(Path.Combine(config.TargetDirectory, "Hello.txt"), @"
//Hello World!
//");
//        config.AddContent("Hello.txt");
//        if (!config.Compile())
//        {
//            Console.WriteLine(config.LastOutput);
//            return;
//        }
//        Assembly assConfig = Assembly.LoadFile(config.LibraryFile!);
//        Type type = assConfig.GetType("Configure")!;
//        MethodInfo mi = type.GetMethod("Application");

//        mi.Invoke(null, new object[] { app });
//        app.Run();
//    }

    static void Main(string[] args)
    {
        using Project server = Project.Create(new ProjectOptions
        {
            Name = "Server",
            Sdk = "Microsoft.NET.Sdk.Web",
            TargetFramework = "net6.0-windows",
            IsExecutable = true,
        });
        server.AddPackage("NUnit", "3.13.3");
        File.WriteAllText(Path.Combine(server.SourceDirectory!, "Program.cs"), @"
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
        using Project config = Project.Create(new ProjectOptions
        {
            Name = "Config",
            Sdk = "Microsoft.NET.Sdk.Web",
        });
        File.WriteAllText(Path.Combine(config.SourceDirectory!, "Configure.cs"), @"
using System.Reflection;

public static class Configure
{
    public static void Application(WebApplication app)
    {
        app.Run(
            async context =>  await context.Response.WriteAsync(
                File.ReadAllText(
                    Path.Combine(
                        Path.GetDirectoryName(
                            Assembly.GetAssembly(typeof(Configure)).Location
                        ), 
                        ""Hello.txt""
                    )
                )
            )
        );
    }

}
");
        File.WriteAllText(Path.Combine(config.SourceDirectory!, "Hello.txt"), @"
Hello World!
");
        config.AddContent("Hello.txt");
        server.AddProject(config);
        server.DotnetEvent += (o, a) =>
        {
            if (!a.Success)
            {
                Console.WriteLine($"{a.Arguments}\n{a.Output}\n{a.Error}");
            }
        };
        bool ok = server.Compile();
        Console.WriteLine(ok);
        if (ok)
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