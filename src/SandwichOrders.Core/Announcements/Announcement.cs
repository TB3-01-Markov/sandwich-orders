namespace SandwichOrders.Core.Announcements;

// One notice from the canteen, for example "Closed from 5 to 15 August (holidays)".
// It is ONE record that changes its role by itself as the days pass, so no background job is needed:
//
//   before ShowFrom              -> Hidden
//   ShowFrom .. PeriodStart - 1  -> Upcoming : informs ("we will be closed from ... to ...")
//   PeriodStart .. PeriodEnd     -> Active   : blocks ordering when BlocksOrdering is true
//   after PeriodEnd              -> Expired  : not shown any more
//
// During the period the separate "closed" text is shown; when it is empty the normal text is used
// (the blocking notice "inherits" the text of the informing one).
public class Announcement
{
    public const int MaxMessageLength = 500;

    public int Id { get; set; }

    public string MessageEn { get; set; } = "";
    public string MessageNl { get; set; } = "";

    // Optional: other text during the period itself, e.g. "The canteen is closed until 16 August".
    public string? PeriodMessageEn { get; set; }
    public string? PeriodMessageNl { get; set; }

    public DateOnly ShowFrom { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; } // inclusive

    public bool BlocksOrdering { get; set; }

    public AnnouncementPhase PhaseOn(DateOnly day) =>
        day < ShowFrom ? AnnouncementPhase.Hidden
        : day < PeriodStart ? AnnouncementPhase.Upcoming
        : day <= PeriodEnd ? AnnouncementPhase.Active
        : AnnouncementPhase.Expired;

    public bool IsVisibleOn(DateOnly day) => PhaseOn(day) is AnnouncementPhase.Upcoming or AnnouncementPhase.Active;

    public bool BlocksOrderingOn(DateOnly day) => BlocksOrdering && PhaseOn(day) == AnnouncementPhase.Active;

    public string MessageFor(string culture, DateOnly day)
    {
        if (PhaseOn(day) == AnnouncementPhase.Active)
        {
            var periodText = Localized.PickAny(culture, PeriodMessageEn, PeriodMessageNl);
            if (!string.IsNullOrWhiteSpace(periodText))
            {
                return periodText;
            }
        }

        return Localized.PickAny(culture, MessageEn, MessageNl);
    }
}

public enum AnnouncementPhase
{
    Hidden,
    Upcoming,
    Active,
    Expired
}
