using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Content.Dtos;

public record ContentItemDto(
    Guid Id,
    string Title,
    string Body,
    ContentStatus Status,
    Guid OwnerId,
    Guid? LayoutId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record CreateContentItemRequest(string Title, string Body, Guid? LayoutId);

public record UpdateContentItemRequest(string Title, string Body, ContentStatus Status, Guid? LayoutId);
