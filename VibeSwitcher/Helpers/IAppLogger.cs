namespace VibeSwitcher.Helpers;

public interface IAppLogger
{
    void Info(string context, string message);
    void Warning(string context, string message);
    void Error(string context, string message);
    void Error(string context, Exception ex);
    void Debug(string context, string message);
}
