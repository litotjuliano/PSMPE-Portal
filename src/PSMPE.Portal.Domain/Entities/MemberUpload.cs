using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Domain.Entities;

/// <summary>
/// A pointer to a member-uploaded file (photo or PRC ID) - not the file's bytes. Keyed by UserId
/// (not MemberId) so a file can be uploaded before any Member row exists yet (before Personal
/// Info is saved). The actual bytes live wherever IFileStorageService is configured to put them;
/// this row stays a few bytes regardless of the file's size, keeping Postgres storage flat.
/// </summary>
public class MemberUpload : BaseEntity
{
    public Guid UserId { get; set; }
    public UploadKind Kind { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}
