namespace VibeSwitcher.Helpers;

// Static service locators used ONLY by code that cannot receive constructor injection:
// RelayCommand (instantiated inline in every ViewModel) and IconHelper (static utility class).
// All other code injects IAppLogger / ISessionErrorTracker directly.

internal static class AppLog
{
    private static volatile IAppLogger? _instance;

    internal static void Register(IAppLogger? instance) => _instance = instance;

    internal static void Info(string context, string message)    => _instance?.Info(context, message);
    internal static void Warning(string context, string message) => _instance?.Warning(context, message);
    internal static void Error(string context, string message)   => _instance?.Error(context, message);
    internal static void Error(string context, Exception ex)     => _instance?.Error(context, ex);
    internal static void Debug(string context, string message)   => _instance?.Debug(context, message);
}

internal static class AppErrors
{
    private static volatile ISessionErrorTracker? _instance;

    internal static void Register(ISessionErrorTracker? instance) => _instance = instance;

    internal static void Record(ErrorCode code, string title, string message)
        => _instance?.Record(code, title, message);
}
