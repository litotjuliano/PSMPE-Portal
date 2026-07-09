namespace PSMPE.Portal.Application.AI.Dtos;

public record PromptRequestDto(string Prompt);

public record PromptResponseDto(string Completion, string Model);
