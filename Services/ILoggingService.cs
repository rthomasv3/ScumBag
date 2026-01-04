namespace Scum_Bag.Services;

internal interface ILoggingService
{
    void LogError(string text);
    void LogInfo(string text);
}