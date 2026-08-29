namespace PSMPE.Portal.Domain.Enums;

/// <summary>
/// Mirrors MemberTypes.cs exactly: free text against a constants list, not a validated C# enum -
/// see add-events-cpd-tracker/proposal.md's 2026-08-29 revision. Nothing validates Event.Type
/// itself; these constants only drive EventService.ValidateCore and the admin form's dropdown.
/// </summary>
public static class EventTypes
{
    public const string Conference = "Conference";
    public const string Seminar = "Seminar";
    public const string Technoforum = "Technoforum";
    public const string Convention = "Convention";
    public const string Symposium = "Symposium";
    public const string Expo = "Expo";

    public static readonly string[] All = [Conference, Seminar, Technoforum, Convention, Symposium, Expo];
}
