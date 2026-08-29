namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// A PSMPE event or workshop (national convention, chapter seminar, technical workshop). Runs
/// face-to-face and via Zoom simultaneously, and each modality is accredited separately, so
/// CpdUnitsOnsite/CpdUnitsOnline (and FeeOnsite/FeeOnline, CpdCodeOnsite/CpdCodeOnline) are
/// independently nullable/settable - see add-events-cpd-tracker/proposal.md's 2026-08-29 revision
/// against PRC's public accreditation data. Chapter is null for a national/all-chapters event.
/// </summary>
public class Event : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Same shape/validation as Description - long text, informational only, shown on the
    /// event detail view and the certificate. Added per PRC's public program listings, which
    /// always carry a stated objective.</summary>
    public string? Objectives { get; set; }

    /// <summary>Free text against the EventTypes constants list (Conference, Seminar, Technoforum,
    /// Convention, Symposium, Expo) - mirrors Member.MemberType/MemberTypes exactly. Nothing
    /// validates the column itself; EventService.ValidateCore is what checks it against
    /// EventTypes.All.</summary>
    public string? Type { get; set; }

    public string? Chapter { get; set; }
    public string? Venue { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }

    /// <summary>PRC's declared hour count for the program - a single value shared across both
    /// modalities (PRC's own data shows the same hour count regardless of Onsite/Online).</summary>
    public decimal? Hours { get; set; }

    /// <summary>Informational planning target only - EventService.RegisterAsync never reads this
    /// field, so reaching it never blocks a new registration. See proposal.md's "Not Built".</summary>
    public int? Capacity { get; set; }

    /// <summary>Independent per-modality fee, replacing the original single Fee field - PRC's
    /// public accreditation data shows PSMPE's Onsite and Online programs are priced independently
    /// (e.g. PHP 3,000 Onsite vs PHP 900 Online for the same physical event).</summary>
    public decimal FeeOnsite { get; set; }
    public decimal FeeOnline { get; set; }

    public decimal? CpdUnitsOnsite { get; set; }
    public decimal? CpdUnitsOnline { get; set; }

    /// <summary>PRC's own accreditation reference for each modality's program - informational only,
    /// never validated against PRC. Independently nullable/settable exactly like CpdUnitsOnsite/
    /// CpdUnitsOnline, for the same reason: each modality is its own separate CPDAS submission.</summary>
    public string? CpdCodeOnsite { get; set; }
    public string? CpdCodeOnline { get; set; }

    /// <summary>Same shape as MemberUpload.StorageKey/Payment.ProofStorageKey - set by
    /// EventPosterService, never directly through CreateEventRequest/UpdateEventRequest. Null means
    /// no poster has been uploaded yet.</summary>
    public string? PosterImageStorageKey { get; set; }

    /// <summary>Always at least one row, even for an event with no separate lectures (a single
    /// session spanning StartsAt/EndsAt) - see EventService.CreateAsync. Attendance and CPD credit
    /// are tracked per session, never per event, so there is no special case for a single-session
    /// event anywhere else in the model.</summary>
    public ICollection<EventSession> Sessions { get; set; } = new List<EventSession>();
}
