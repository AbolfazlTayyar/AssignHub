namespace AssignHub.IntegrationTests.Http;

internal static class Eventually
{
    // For work that runs on a background thread (the sweeper) after the fake clock is advanced.
    public static async Task<T> WaitForAsync<T>(
        Func<Task<T>> probe,
        Func<T, bool> isDone,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (true)
        {
            var value = await probe();
            if (isDone(value) || DateTime.UtcNow >= deadline)
            {
                return value;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }
}
