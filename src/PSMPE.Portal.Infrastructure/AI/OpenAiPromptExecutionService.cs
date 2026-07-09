using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using PSMPE.Portal.Application.AI;
using PSMPE.Portal.Application.AI.Dtos;

namespace PSMPE.Portal.Infrastructure.AI;

/// <summary>
/// Thin wrapper around the official OpenAI .NET SDK. Single-shot completion only —
/// TODO: add streaming (IAsyncEnumerable&lt;StreamingChatCompletionUpdate&gt;) once the
/// frontend needs incremental token-by-token responses.
/// </summary>
public class OpenAiPromptExecutionService(IConfiguration configuration) : IPromptExecutionService
{
    public async Task<PromptResponseDto> ExecuteAsync(PromptRequestDto request, CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey configuration value is missing.");
        var model = configuration["OpenAI:Model"] ?? "gpt-4o-mini";

        var client = new ChatClient(model, apiKey);
        var completion = await client.CompleteChatAsync(
            [new UserChatMessage(request.Prompt)],
            cancellationToken: cancellationToken);

        var text = string.Concat(completion.Value.Content.Select(part => part.Text));
        return new PromptResponseDto(text, model);
    }
}
