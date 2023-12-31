﻿namespace Net.Leksi.Rac.UnitTesting;

[AttributeUsage(AttributeTargets.Assembly)]
public class MyAttribute: Attribute
{
    public string CommonPackageName { get; set; }
    public string CommonPackageVersion { get; set; }
    public string ProjectDir { get; set; }

    public MyAttribute(string commonPackageName, string commonPackageVersion, string projectDir)
    {
        CommonPackageName = commonPackageName;
        CommonPackageVersion = commonPackageVersion;
        ProjectDir = projectDir;
    }
}
