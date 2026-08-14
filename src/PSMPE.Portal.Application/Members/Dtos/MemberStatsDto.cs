namespace PSMPE.Portal.Application.Members.Dtos;

/// <summary>
/// Aggregated Membership statistics for the admin dashboard - one call replaces what used to be a
/// fully fake e-commerce template dashboard. Reuses GetAllAsync's own submitted/exclude-user-ids
/// filtering so the counts here always match what GetAllAsync's list would show.
/// </summary>
public record MemberStatsDto(
    MemberStatusCountsDto StatusCounts,
    /// <summary>Last 12 calendar months, oldest first, zero-filled for months with no submissions.</summary>
    IReadOnlyList<MonthlyRegistrationCountDto> RegistrationTrend,
    /// <summary>One row per Chapters.All, in that declared order, zero-filled for chapters with no members.</summary>
    IReadOnlyList<NamedCountDto> ByChapter,
    /// <summary>One row per MemberTypes.All, in that declared order, zero-filled for types with no members.</summary>
    IReadOnlyList<NamedCountDto> ByMemberType,
    MemberActionItemsDto ActionItems);

public record MemberStatusCountsDto(int Pending, int Active, int Expired, int Deactivated);

public record MonthlyRegistrationCountDto(int Year, int Month, int Count);

public record NamedCountDto(string Name, int Count);

public record MemberActionItemsDto(int PendingApprovals, int PendingPrcVerification, int RenewalsDueSoon);
