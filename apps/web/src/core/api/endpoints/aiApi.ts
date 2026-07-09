import { apiClient } from '../apiClient'

export interface PromptRequest {
  prompt: string
}

export interface PromptResponse {
  completion: string
  model: string
}

// TODO: switch to a streaming-capable call once the backend exposes an SSE/streaming endpoint.
export const aiApi = {
  executePrompt: (request: PromptRequest) =>
    apiClient.post<PromptResponse>('/api/ai/prompt', request).then((res) => res.data),
}
