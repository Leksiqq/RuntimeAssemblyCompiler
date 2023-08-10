namespace Net.Leksi.Demo.RACWebApp.Starter;

[AttributeUsage(AttributeTargets.Assembly)]
public class MyAttribute: Attribute
{
    public string CommonPackageName { get; set; }
    public string CommonPackageVersion { get; set; }

    public MyAttribute(string commonPackageName, string commonPackageVersion)
    {
        CommonPackageName = commonPackageName;
        CommonPackageVersion = commonPackageVersion;
    }
}
