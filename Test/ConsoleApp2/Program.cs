using Net.Leksi.RuntimeAssemblyCompiler;

using Project project1 = Project.Create(new ProjectOptions
{
    Name = "Project1",
});
project1.AddPackage("NUnit", "3.13.3");

using Project project2 = Project.Create(new ProjectOptions
{
    Name = "Project2",
});

project1.AddProject(project2);

bool ok = project1.Compile();

Console.WriteLine(ok);

Console.WriteLine(project1.ProjectFile);
Console.WriteLine(File.ReadAllText(project1.ProjectFile));
Console.WriteLine(project2.ProjectFile);
Console.WriteLine(File.ReadAllText(project2.ProjectFile));
