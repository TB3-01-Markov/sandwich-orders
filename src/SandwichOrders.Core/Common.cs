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

// Prepared = the canteen makes it from ingredients (stock counted per ingredient).
// Supplied = delivered ready-made by a supplier (stock counted per piece).
public enum SandwichKind
{
    Prepared,
    Supplied
}

// Placed -> Prepared ("ready, come and get it") -> Collected. Only the canteen employee changes the status.
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

    // Like Pick, but also falls back to Dutch when only the Dutch text was filled in (used for announcements).
    public static string PickAny(string culture, string? english, string? dutch)
    {
        english ??= "";
        dutch ??= "";
        var preferDutch = culture.StartsWith("nl", StringComparison.OrdinalIgnoreCase);
        var first = preferDutch ? dutch : english;
        var second = preferDutch ? english : dutch;
        return string.IsNullOrWhiteSpace(first) ? second : first;
    }
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

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(Now(clock));
}
