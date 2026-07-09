# AI Prompt Execution

## Purpose

Starter endpoint structure for triggering an OpenAI completion from the frontend, e.g.
for future AI-assisted content authoring. Deliberately minimal today — a single-shot
prompt-in, completion-out call.

## Endpoints

- `POST /api/ai/prompt` — execute a prompt against the configured OpenAI model
  - Auth: authenticated
  - Request: `{ prompt }`
  - Response: `{ completion, model }`
  - Returns `400` if `prompt` is empty/whitespace.

## Authorization rules

Any authenticated user may call this today. TODO: revisit — this is a starter contract,
not a finished one; consider restricting by role and/or adding per-user usage quotas
before exposing it broadly.

## Implementation notes

- `PSMPE.Portal.Application.AI.IPromptExecutionService` is the Application-facing
  contract; `PSMPE.Portal.Infrastructure.AI.OpenAiPromptExecutionService` is the concrete
  implementation using the official `OpenAI` NuGet SDK (`OpenAI.Chat.ChatClient`).
- Model and API key come from configuration (`OpenAI:Model`, `OpenAI:ApiKey`) — never
  hardcoded. See the root `.env.example`.

## Open questions / TODO

- Streaming responses (`IAsyncEnumerable<StreamingChatCompletionUpdate>`) once the
  frontend needs incremental token-by-token output instead of waiting for the full reply.
- Per-user rate limiting / usage tracking before this goes past internal testing.
- Prompt templates / system prompts for specific CMS authoring workflows.
