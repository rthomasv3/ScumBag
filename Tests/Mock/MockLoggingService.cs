namespace ScumBag.Tests.Mock;

using Scum_Bag.Services;

internal class MockLoggingService : ILoggingService
{
    public void LogError(string text) { }
    public void LogInfo(string text) { }
}