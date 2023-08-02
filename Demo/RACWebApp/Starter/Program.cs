using Net.Leksi.RACWebApp.Common;
using Net.Leksi.RuntimeAssemblyCompiler;
using System.Reflection;

namespace Net.Leksi.RACWebApp.Starter;

internal class Program
{
    static void Main(string[] args)
    {
        Project server = new("Server")
        {
            Sdk = "Microsoft.NET.Sdk.Web",
            Configuration = "Debug",
        };
        server.TargetFramework += "-windows7.0";
        server.AddPackage("Net.Leksi.RACWebApp.Common", "1.0.0"/*, @"W:\C#\RuntimeAssemblyCompiler\Demo\RACWebApp\Common\bin\Debug"*/);
        File.WriteAllText(Path.Combine(server.ProjectDirectory, "Server.cs"), @"
using Net.Leksi.RACWebApp.Common;

public class Server: IServer
{
    public void Run()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        Configure.Application(app);
        app.Run();
    }
}
"
        );
        Project config = new("Config")
        {
            Sdk = "Microsoft.NET.Sdk.Web",
            IsExecutable = false,
        };
        File.WriteAllText(Path.Combine(config.ProjectDirectory, "Configure.cs"), @"
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
        File.WriteAllText(Path.Combine(config.ProjectDirectory, "Hello.txt"), @"
Hello World!
");
        config.AddContent("Hello.txt");
        server.AddProject(config);
        bool ok = server.Compile();

        if (!ok)
        {
            Console.WriteLine(server.LastOutput);
        }
        else
        {
            IServer web = (Activator.CreateInstance(Assembly.LoadFile(server.LibraryFile!).GetType("Server")!) as IServer)!;
            web.Run();
        }
    }

}