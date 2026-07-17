namespace PSMPE.Portal.Application.Members.Dtos;

public record MemberCertificateDto(Guid Id, string FileName, string ContentType, long FileSizeBytes, DateTimeOffset CreatedAt);
