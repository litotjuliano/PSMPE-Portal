using PSMPE.Portal.Application.AI.Dtos;

namespace PSMPE.Portal.Application.AI;

/// <summary>
/// Application-facing contract for executing an AI prompt. The concrete implementation
/// (backed by the OpenAI SDK) lives in Infrastructure to keep this layer free of
/// third-party/service dependencies.
/// TODO: extend with streaming (IAsyncEnumerable) once the frontend needs incremental responses.
/// </summary>
public interface IPromptExecutionService
{
    Task<PromptResponseDto> ExecuteAsync(PromptRequestDto request, CancellationToken cancellationToken = default);
}
