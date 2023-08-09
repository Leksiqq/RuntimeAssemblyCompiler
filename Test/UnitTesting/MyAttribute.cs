namespace Net.Leksi.Rac.UnitTesting;

[AttributeUsage(AttributeTargets.Assembly)]
public class MyAttribute: Attribute
{
    public string Version { get; set; }

    public MyAttribute(string version)
    {
        Version = version;
    }
}
