namespace SandwichOrders.Core;

public static class Roles
{
    public const string Customer = "Customer";
    public const string Staff = "Staff";
}

public enum Bread
{
    White,
    WholeGrain
}

public enum OrderStatus
{
    Placed,
    Prepared,
    Collected,
    Cancelled
}

public static class Localized
{
    // Picks the Dutch text for Dutch cultures; falls back to English when the Dutch text is empty (US-02).
    public static string Pick(string culture, string english, string dutch) =>
        culture.StartsWith("nl", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(dutch)
            ? dutch
            : english;
}

public static class BrusselsTime
{
    private static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        // "Europe/Brussels" works on Linux/macOS (and on Windows with ICU); the second id is the classic Windows name.
        foreach (var id in new[] { "Europe/Brussels", "Romance Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new InvalidOperationException("Time zone Europe/Brussels was not found on this machine.");
    }

    // Wall-clock time in Belgium for the instant given by the clock.
    public static DateTime Now(TimeProvider clock) =>
        TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime;
}
