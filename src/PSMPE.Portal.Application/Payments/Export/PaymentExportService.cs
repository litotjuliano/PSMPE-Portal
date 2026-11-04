using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Payments.Export;

public sealed record PaymentExport(byte[] Content, int RowCount);

public interface IPaymentExportService
{
    /// <summary>Every payment awaiting verification, oldest first, as an xlsx workbook.</summary>
    Task<PaymentExport> BuildQueueExportAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the Excel export of the payments queue. Proof storage keys stay inside this class: they
/// embed the member's name and birthdate. See openspecs/payments.md.
/// </summary>
public class PaymentExportService(IApplicationDbContext db, IMemberUploadService uploads) : IPaymentExportService
{
    private const string NoProofNote = "No proof on file";
    private const string MissingFileNote = "File missing – ask member to resubmit";
    private const string UnreadableFileNote = "Could not read file – open it in the portal";

    public async Task<PaymentExport> BuildQueueExportAsync(CancellationToken cancellationToken = default)
    {
        var payments = await db.Payments.AsNoTracking()
            .Include(p => p.Member).ThenInclude(m => m.User)
            .Include(p => p.EventRegistration).ThenInclude(er => er!.Event)
            .Where(p => p.Status == PaymentStatus.Submitted)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        var rows = new List<PaymentExportRow>(payments.Count);
        foreach (var payment in payments)
        {
            var (picture, note) = await LoadProofAsync(payment.ProofStorageKey, cancellationToken);
            rows.Add(new PaymentExportRow(
                FullName(payment.Member), payment.Member.User.Email, Address(payment.Member),
                payment.Member.MobileNumber, payment.Amount, payment.ReferenceNo, payment.PaidOn,
                picture, note, ForLabel(payment)));
        }

        return new PaymentExport(PaymentExportWorkbook.Build(rows), rows.Count);
    }

    private async Task<(ProofPicture? Picture, string? Note)> LoadProofAsync(string? storageKey, CancellationToken cancellationToken)
    {
        if (storageKey is null)
        {
            return (null, NoProofNote);
        }

        (Stream Content, string ContentType)? file;
        try
        {
            file = await uploads.OpenByKeyAsync(storageKey, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, UnreadableFileNote);
        }

        if (file is null)
        {
            return (null, MissingFileNote);
        }

        await using var content = file.Value.Content;
        var picture = PaymentProofPicture.TryCreate(content, file.Value.ContentType);
        return picture is null ? (null, UnreadableFileNote) : (picture, null);
    }

    private static string FullName(Member member) =>
        Join(" ", member.FirstName, member.MiddleName, member.LastName, member.Suffix);

    private static string Address(Member member) =>
        Join(", ",
            Join(" ", member.HouseNo, member.Street),
            member.Barangay,
            member.CityMunicipality,
            Join(" ", member.Province, member.ZipCode),
            member.Country);

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));

    // Same labels as KIND_LABELS in PaymentsQueueTable.tsx.
    private static string ForLabel(Payment payment) => payment.Kind switch
    {
        PaymentKind.NewMembership => "New membership",
        PaymentKind.Renewal => "Renewal",
        PaymentKind.PortalAccessOnly => "Portal access",
        PaymentKind.EventRegistration when payment.EventRegistration is not null =>
            $"Event registration — {payment.EventRegistration.Event.Title}",
        PaymentKind.EventRegistration => "Event registration",
        _ => payment.Kind.ToString(),
    };
}
