using Microsoft.AspNetCore.Mvc;
using PSMPE.Portal.Application.AI;
using PSMPE.Portal.Application.AI.Dtos;
using PSMPE.Portal.WebAPI.Controllers;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.AI;

/// <summary>Fake stand-in so the AI endpoint's request handling can be tested without calling OpenAI.</summary>
file class FakePromptExecutionService : IPromptExecutionService
{
    public PromptRequestDto? LastRequest { get; private set; }

    public Task<PromptResponseDto> ExecuteAsync(PromptRequestDto request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(new PromptResponseDto($"echo: {request.Prompt}", "fake-model"));
    }
}

public class AiControllerTests
{
    [Fact]
    public async Task ExecutePrompt_WithEmptyPrompt_ReturnsBadRequest()
    {
        var controller = new AiController(new FakePromptExecutionService());

        var result = await controller.ExecutePrompt(new PromptRequestDto(""), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ExecutePrompt_WithValidPrompt_CallsServiceAndReturnsCompletion()
    {
        var fakeService = new FakePromptExecutionService();
        var controller = new AiController(fakeService);

        var result = await controller.ExecutePrompt(new PromptRequestDto("Hello"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PromptResponseDto>(ok.Value);
        Assert.Equal("echo: Hello", response.Completion);
        Assert.Equal("Hello", fakeService.LastRequest?.Prompt);
    }
}
