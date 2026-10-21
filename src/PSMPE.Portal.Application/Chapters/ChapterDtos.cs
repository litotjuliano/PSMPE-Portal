namespace PSMPE.Portal.Application.Chapters;

/// <summary>MemberCount/EventCount are how many members/events currently point at the chapter -
/// what decides whether it can be deleted or only deactivated.</summary>
public record ChapterDto(Guid Id, string Name, int SortOrder, bool IsActive, int MemberCount, int EventCount);

/// <summary>Null SortOrder on create means "after the last one"; null on update means "leave as is".
/// IsActive defaults to true on create and, when omitted on update, is left unchanged.</summary>
public record SaveChapterRequest(string Name, int? SortOrder, bool? IsActive);
