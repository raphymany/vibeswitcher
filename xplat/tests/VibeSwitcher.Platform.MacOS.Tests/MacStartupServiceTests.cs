using System.Diagnostics;
using System.Xml.Linq;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform.MacOS.OS;
using VibeSwitcher.Tests;

namespace VibeSwitcher.Platform.MacOS.Tests;

public sealed class MacStartupServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Directory.CreateTempSubdirectory("vs-agents").FullName, "LaunchAgents");
    private readonly FakeSessionErrorTracker _errors = new();
    private string? _path = "/Applications/VibeSwitcher.app/Contents/MacOS/VibeSwitcher";

    private MacStartupService Service() => new(new FakeAppLogger(), _errors, _dir, () => _path);
    private string AgentFile => Path.Combine(_dir, MacStartupService.Label + ".plist");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_dir)!, true); } catch (Exception) { }
    }

    private static (string Label, string[] Args, bool RunAtLoad) ReadPlist(string file)
    {
        var dict = XDocument.Load(file).Root!.Element("dict")!.Elements().ToList();
        XElement ValueOf(string key) => dict[dict.FindIndex(e => e.Name == "key" && e.Value == key) + 1];
        return (ValueOf("Label").Value, ValueOf("ProgramArguments").Elements("string").Select(e => e.Value).ToArray(),
            ValueOf("RunAtLoad").Name == "true");
    }

    [Fact]
    public void Enable_WritesALoginAgent_Disable_RemovesIt()
    {
        var svc = Service();
        Assert.False(svc.IsStartupEnabled());

        svc.Enable();
        Assert.True(svc.IsStartupEnabled());
        var (label, args, runAtLoad) = ReadPlist(AgentFile);
        Assert.Equal(MacStartupService.Label, label);
        // The bundle is opened the way Finder/Dock open it, with no arguments (like the Windows startup entry).
        Assert.Equal(["/usr/bin/open", "-a", "/Applications/VibeSwitcher.app"], args);
        Assert.True(runAtLoad);
        Assert.Contains($"<string>{MacStartupService.BundleId}</string>", File.ReadAllText(AgentFile));
        if (!OperatingSystem.IsWindows()) // launchd refuses agents others can write to
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                File.GetUnixFileMode(AgentFile));

        svc.Disable();
        Assert.False(svc.IsStartupEnabled());
        Assert.Empty(_errors.Errors);
    }

    [Fact]
    public void PathWithXmlCharacters_StaysValid()
    {
        _path = "/Users/ann/Apps & <Tools>/VibeSwitcher.app/Contents/MacOS/VibeSwitcher";
        Service().Enable();
        Assert.Equal(["/usr/bin/open", "-a", "/Users/ann/Apps & <Tools>/VibeSwitcher.app"], ReadPlist(AgentFile).Args);
    }

    [Fact]
    public void DevelopmentBuild_RunsTheExecutableDirectly()
    {
        _path = "/Users/ann/src/bin/VibeSwitcher";
        Service().Enable();
        Assert.Equal([_path], ReadPlist(AgentFile).Args);
    }

    [Fact]
    public void DevelopmentBuild_StartedWithTheDotnetCommand_RunsTheAppsAssembly()
    {
        _path = "/usr/local/share/dotnet/dotnet";
        var svc = new MacStartupService(new FakeAppLogger(), _errors, _dir, () => _path, () => "/Users/ann/src/bin/VibeSwitcher.dll");
        svc.Enable();
        Assert.Equal(["/usr/local/share/dotnet/dotnet", "/Users/ann/src/bin/VibeSwitcher.dll"], ReadPlist(AgentFile).Args);
        // Run that way while an installed copy has its login item: that one is left alone.
        _path = "/Applications/VibeSwitcher.app/Contents/MacOS/VibeSwitcher";
        Service().Enable();
        _path = "/usr/local/share/dotnet/dotnet";
        svc.RefreshRegistryPath();
        Assert.Equal(["/usr/bin/open", "-a", "/Applications/VibeSwitcher.app"], ReadPlist(AgentFile).Args);
        // (A program merely named like the host isn't treated as one without an assembly to run; an
        // app bundle is opened as one whatever the assembly.)
        Assert.Equal(["/opt/dotnet"], MacStartupService.ProgramArguments("/opt/dotnet", null));
        Assert.Equal(["/usr/bin/open", "-a", "/Applications/VibeSwitcher.app"],
            MacStartupService.ProgramArguments("/Applications/VibeSwitcher.app/Contents/MacOS/VibeSwitcher", "/x/VibeSwitcher.dll"));
    }

    [Fact]
    public void Refresh_RepointsAMovedApp()
    {
        var svc = Service();
        svc.Enable();
        _path = "/Users/ann/Applications/VibeSwitcher.app/Contents/MacOS/VibeSwitcher";
        svc.RefreshRegistryPath();
        Assert.Equal(["/usr/bin/open", "-a", "/Users/ann/Applications/VibeSwitcher.app"], ReadPlist(AgentFile).Args);
    }

    [Fact]
    public void TranslocatedCopy_IsNeverSaved_AndTheUserIsTold()
    {
        var svc = Service();
        _path = "/private/var/folders/ab/cd/T/AppTranslocation/1F2E/d/VibeSwitcher.app/Contents/MacOS/VibeSwitcher";
        svc.Enable();
        Assert.False(svc.IsStartupEnabled());
        Assert.Contains("Applications", Assert.Single(_errors.Errors).Message);

        // An existing good login item isn't replaced by a translocated launch either.
        _path = "/Applications/VibeSwitcher.app/Contents/MacOS/VibeSwitcher";
        svc.Enable();
        _path = "/private/var/folders/ab/cd/T/AppTranslocation/9A8B/d/VibeSwitcher.app/Contents/MacOS/VibeSwitcher";
        svc.RefreshRegistryPath();
        Assert.Equal(["/usr/bin/open", "-a", "/Applications/VibeSwitcher.app"], ReadPlist(AgentFile).Args);
    }

    [Fact]
    public void NoPath_RecordsTheWindowsErrorCode()
    {
        _path = null;
        Service().Enable();
        Assert.Equal(ErrorCode.StartupPathResolutionFailed, Assert.Single(_errors.Errors).Code);
    }

    [Fact]
    public void OnARealMac_ThePlistPassesApplesOwnCheck()
    {
        if (!OperatingSystem.IsMacOS()) return;
        _path = "/Applications/Vibe & Switcher.app/Contents/MacOS/VibeSwitcher";
        Service().Enable();
        var lint = Process.Start(new ProcessStartInfo("/usr/bin/plutil", ["-lint", AgentFile]) { RedirectStandardOutput = true })!;
        var output = lint.StandardOutput.ReadToEnd();
        lint.WaitForExit();
        Assert.True(lint.ExitCode == 0, output);
    }
}
