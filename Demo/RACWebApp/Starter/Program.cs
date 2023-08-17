using Net.Leksi.RACWebApp.Common;
using Net.Leksi.RuntimeAssemblyCompiler;
using System.Reflection;

namespace Net.Leksi.Demo.RACWebApp.Starter;

internal class Program
{
    static void Main(string[] args)
    {
        string commonPackageName = Assembly.GetExecutingAssembly().GetCustomAttribute<MyAttribute>()!.CommonPackageName;
        string commonPackageVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<MyAttribute>()!.CommonPackageVersion;

        using Project server = Project.Create(new ProjectOptions
        {
            Name = "Server",
            Sdk = "Microsoft.NET.Sdk.Web",
            TargetFramework = "net6.0-windows7.0",
        });
        server.IsVerbose = true;
        server.AddPackage(commonPackageName, commonPackageVersion, Path.GetDirectoryName(typeof(Program).Assembly.Location));
        File.WriteAllText(Path.Combine(server.ProjectDir!, "Server.cs"), @"
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
        using Project config = Project.Create(new ProjectOptions
        {
            Name = "Config",
            Sdk = "Microsoft.NET.Sdk.Web",
            TargetFramework = "net6.0-windows7.0",
        });
        File.WriteAllText(Path.Combine(config.ProjectDir, "Configure.cs"), @"
using System.Reflection;

public static class Configure
{
    public static void Application(WebApplication app)
    {
#if Defined
        Console.WriteLine(""Defined"");
#endif
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
        File.WriteAllText(Path.Combine(config.ProjectDir!, "Hello.txt"), @"
Hello World!
");
        config.AddContent("Hello.txt");

        config.Define("Defined");

        server.AddProject(config);

        server.Compile();
        IServer web = (Activator.CreateInstance(Assembly.LoadFile(server.LibraryFile!).GetType("Server")!) as IServer)!;
        web.Run();
    }

}