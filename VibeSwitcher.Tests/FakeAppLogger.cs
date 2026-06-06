using VibeSwitcher.Helpers;

namespace VibeSwitcher.Tests;

public class FakeAppLogger : IAppLogger
{
    public void Info(string context, string message) { }
    public void Warning(string context, string message) { }
    public void Error(string context, string message) { }
    public void Error(string context, Exception ex) { }
    public void Debug(string context, string message) { }
}
