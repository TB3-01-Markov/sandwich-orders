using Microsoft.Extensions.Logging;

namespace SandwichOrders.Core.Announcements;

public interface IAnnouncementRepository
{
    // All announcements, newest period first.
    Task<IReadOnlyList<Announcement>> GetAllAsync(CancellationToken ct = default);

    Task<Announcement?> GetAsync(int id, CancellationToken ct = default);

    void Add(Announcement announcement);

    void Remove(Announcement announcement);

    Task SaveChangesAsync(CancellationToken ct = default);
}

// What the canteen employee typed in the announcement form.
public record AnnouncementInput(
    int? Id,
    string? MessageEn,
    string? MessageNl,
    string? PeriodMessageEn,
    string? PeriodMessageNl,
    DateOnly ShowFrom,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    bool BlocksOrdering);

public enum AnnouncementError
{
    NotFound,
    MessageRequired,
    MessageTooLong,
    InvalidDates
}

public class AnnouncementException(AnnouncementError error) : Exception($"Announcement refused: {error}")
{
    public AnnouncementError Error { get; } = error;
}

public class AnnouncementService(IAnnouncementRepository announcements, TimeProvider clock, ILogger<AnnouncementService> logger)
{
    // What visitors see today (upcoming and current notices), the soonest period first.
    public async Task<IReadOnlyList<Announcement>> GetVisibleTodayAsync(CancellationToken ct = default)
    {
        var today = BrusselsTime.Today(clock);
        return (await announcements.GetAllAsync(ct))
            .Where(a => a.IsVisibleOn(today))
            .OrderBy(a => a.PeriodStart)
            .ToList();
    }

    public Task<IReadOnlyList<Announcement>> GetAllAsync(CancellationToken ct = default) =>
        announcements.GetAllAsync(ct);

    public Task<Announcement?> GetAsync(int id, CancellationToken ct = default) =>
        announcements.GetAsync(id, ct);

    public async Task<Announcement> SaveAsync(AnnouncementInput input, CancellationToken ct = default)
    {
        static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

        var messageEn = Clean(input.MessageEn);
        var messageNl = Clean(input.MessageNl);
        var periodEn = Clean(input.PeriodMessageEn);
        var periodNl = Clean(input.PeriodMessageNl);

        // At least one language is required; the other one falls back to it.
        if (messageEn is null && messageNl is null)
        {
            throw new AnnouncementException(AnnouncementError.MessageRequired);
        }

        if (new[] { messageEn, messageNl, periodEn, periodNl }.Any(t => t?.Length > Announcement.MaxMessageLength))
        {
            throw new AnnouncementException(AnnouncementError.MessageTooLong);
        }

        if (input.ShowFrom > input.PeriodStart || input.PeriodStart > input.PeriodEnd)
        {
            throw new AnnouncementException(AnnouncementError.InvalidDates);
        }

        Announcement announcement;
        if (input.Id is null)
        {
            announcement = new Announcement();
            announcements.Add(announcement);
        }
        else
        {
            announcement = await announcements.GetAsync(input.Id.Value, ct)
                           ?? throw new AnnouncementException(AnnouncementError.NotFound);
        }

        announcement.MessageEn = messageEn ?? "";
        announcement.MessageNl = messageNl ?? "";
        announcement.PeriodMessageEn = periodEn;
        announcement.PeriodMessageNl = periodNl;
        announcement.ShowFrom = input.ShowFrom;
        announcement.PeriodStart = input.PeriodStart;
        announcement.PeriodEnd = input.PeriodEnd;
        announcement.BlocksOrdering = input.BlocksOrdering;

        await announcements.SaveChangesAsync(ct);
        logger.LogInformation("Announcement {Id} saved: {Start} to {End}, blocks ordering {Blocks}",
            announcement.Id, announcement.PeriodStart, announcement.PeriodEnd, announcement.BlocksOrdering);
        return announcement;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var announcement = await announcements.GetAsync(id, ct)
                           ?? throw new AnnouncementException(AnnouncementError.NotFound);
        announcements.Remove(announcement);
        await announcements.SaveChangesAsync(ct);
        logger.LogInformation("Announcement {Id} deleted", id);
    }
}
