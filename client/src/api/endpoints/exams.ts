import type {
  AnswerResponse,
  Difficulty,
  ExamMode,
  ExamResult,
  ExamSession,
  ReviewCount,
} from '../types'
import { get, post } from '../http'

export interface StartExamParams {
  certificationCode: string
  mode: ExamMode
  questionCount?: number | null
  domainId?: number | null
  difficulty?: Difficulty | null
}

export interface AnswerParams {
  questionId: string
  selectedLabels: string[]
  secondsSpent: number
}

/**
 * Exam sessions.
 *
 * A session is a resource: `start` creates one and answers and submission are sub-resources of
 * it, which is why they are addressed by session id rather than as free-standing calls.
 */
export const examsApi = {
  start: (params: StartExamParams) => post<ExamSession>('/api/exams', params),

  get: (id: string) => get<ExamSession>(`/api/exams/${encodeURIComponent(id)}`),

  answer: (id: string, body: AnswerParams) =>
    post<AnswerResponse>(`/api/exams/${encodeURIComponent(id)}/answers`, body),

  submit: (id: string) => post<ExamResult>(`/api/exams/${encodeURIComponent(id)}/submit`),

  history: (certificationCode?: string) =>
    get<ExamResult[]>('/api/exams/history', { certificationCode }),

  /** How many questions are waiting in review — drives the Review button's count. */
  reviewCount: (certificationCode: string) =>
    get<ReviewCount>('/api/exams/review-count', { certificationCode }),
}
