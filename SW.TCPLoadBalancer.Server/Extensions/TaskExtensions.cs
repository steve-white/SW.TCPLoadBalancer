namespace SW.TCPLoadBalancer.Server.Extensions;

public static class TaskExtensions
{
    public static async Task WaitForCompletionAsync(this Task? task)
    {
        if (task != null)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException) { }
        }
    }
}
