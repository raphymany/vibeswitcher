namespace VibeSwitcher.App.Tests;

public class CliTests
{
    private static (int? Code, string Out, string Err) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = Cli.Handle(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void Version_PrintsTheAppVersion()
    {
        var (code, output, _) = Run("--version");
        Assert.Equal(0, code);
        // "VibeSwitcher 2.0.1" or "VibeSwitcher 2.0.1-preview" — and no build hash.
        Assert.Matches(@"^VibeSwitcher \d+\.\d+\.\d+(-[0-9A-Za-z.]+)?\r?\n$", output);
    }

    [Fact]
    public void Help_PrintsTheUsage()
    {
        var (code, output, _) = Run("--help");
        Assert.Equal(0, code);
        Assert.Contains("--switch <profile>", output);
    }

    [Fact]
    public void ABadOption_IsExplained_WithExitCode2()
    {
        var (code, _, error) = Run("--frobnicate");
        Assert.Equal(2, code);
        Assert.Contains("Unknown option '--frobnicate'", error);
        Assert.Contains("Usage:", error);
    }

    [Theory]
    [InlineData]
    [InlineData("--open")]
    [InlineData("--switch", "Gaming")]
    public void EverythingElse_StartsTheApp(params string[] args) => Assert.Null(Run(args).Code);
}
