using System.IO;

public static class AndroidDeviceBuild
{
    public static void BuildApk()
    {
        ProjectPlayerBuild.BuildAndroidDevelopmentFromCommandLine(
            Path.Combine(".workspace", "builds", "android", "Unity6DeviceTest.apk"));
    }
}
