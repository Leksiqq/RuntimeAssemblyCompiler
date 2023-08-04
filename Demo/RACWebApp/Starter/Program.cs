using Net.Leksi.RACWebApp.Common;
using Net.Leksi.RuntimeAssemblyCompiler;
using System.Reflection;

namespace Net.Leksi.Demo.RACWebApp.Starter;

internal class Program
{
    static void Main(string[] args)
    {
        using Project1 server = new("Server")
        {
            Sdk = "Microsoft.NET.Sdk.Web",
            IsVerbose = true,
        };
        server.TargetFramework += "-windows7.0";
        server.AddPackage("Net.Leksi.RACWebApp.Common", "1.0.0", Path.GetDirectoryName(typeof(Program).Assembly.Location));
        File.WriteAllText(Path.Combine(server.SourceDirectory, "Server.cs"), @"
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
        using Project1 config = new("Config")
        {
            Sdk = "Microsoft.NET.Sdk.Web",
        };
        File.WriteAllText(Path.Combine(config.SourceDirectory, "Configure.cs"), @"
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
        File.WriteAllText(Path.Combine(config.SourceDirectory, "Hello.txt"), @"
Hello World!
");
        config.AddContent("Hello.txt");
        server.AddProject(config);

        server.DotnetEvent += (s, a) =>
        {
            if (!a.Success)
            {
                Console.WriteLine(a.Output);
                Console.WriteLine(a.Error);
            }
        };
        if (server.Compile())
        {
            IServer web = (Activator.CreateInstance(Assembly.LoadFile(server.LibraryFile!).GetType("Server")!) as IServer)!;
            web.Run();
        }
    }

}