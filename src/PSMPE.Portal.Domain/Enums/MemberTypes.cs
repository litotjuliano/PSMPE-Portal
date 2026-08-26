namespace PSMPE.Portal.Domain.Enums;

public static class MemberTypes
{
    /// <summary>
    /// The original sole option, kept because every member predating New/Renewal/SeniorCitizen
    /// carries it - dropping it from the list would leave those records showing a value their own
    /// edit form doesn't offer. Nothing validates against this array; the column is free text
    /// (max 64), so these constants only drive the dropdowns and the seeder.
    /// </summary>
    public const string Regular = "Regular Member";
    public const string New = "New";
    public const string Renewal = "Renewal";
    public const string SeniorCitizen = "Senior Citizen";

    public static readonly string[] All = [Regular, New, Renewal, SeniorCitizen];
}
