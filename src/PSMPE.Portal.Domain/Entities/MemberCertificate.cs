namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// A member-uploaded certificate file - unlike MemberUpload (one row per UserId+Kind), a member
/// can have any number of certificates, so this is its own table rather than reusing MemberUpload's
/// uniqueness model. Keyed by UserId (not MemberId) for the same reason MemberUpload is - a
/// certificate can be uploaded before any Member row exists.
/// </summary>
public class MemberCertificate : BaseEntity
{
    public Guid UserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
}
