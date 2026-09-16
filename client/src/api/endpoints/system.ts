import { get } from '../http'

export interface HealthStatus {
  status: string
  /** Gemini | Groq | DeepSeek | Offline — shown in the topbar badge. */
  aiProvider: string
}

/** Liveness plus which AI provider is configured. Deliberately unauthenticated. */
export const systemApi = {
  health: () => get<HealthStatus>('/api/health'),
}
