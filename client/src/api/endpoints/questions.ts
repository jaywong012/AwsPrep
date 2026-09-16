import type { Difficulty, GenerateRequest, GenerateResponse, Question } from '../types'
import { del, get, post } from '../http'

export interface BrowseQuestionsParams {
  certificationCode: string
  domainId?: number | null
  difficulty?: Difficulty | null
  includeRetired?: boolean
  skip?: number
  take?: number
}

/** A question whose difficulty label the recalculation would change. */
export interface DifficultyChange {
  questionId: string
  stem: string
  from: Difficulty
  to: Difficulty
}

export interface DifficultyTally {
  easy: number
  medium: number
  hard: number
}

export interface DifficultyRecalculation {
  certificationCode: string | null
  /** False for a dry run, where nothing was written. */
  applied: boolean
  examined: number
  changed: number
  before: DifficultyTally
  after: DifficultyTally
  /** A sample of the changes, not the whole list. */
  sample: DifficultyChange[]
}

/**
 * The question bank.
 *
 * `generate` and the two admin operations are the expensive or destructive ones; everything else
 * is a plain read. They live together because they are one resource, not because they are alike.
 */
export const questionsApi = {
  browse: ({
    certificationCode,
    domainId,
    difficulty,
    includeRetired = false,
    skip = 0,
    take = 25,
  }: BrowseQuestionsParams) =>
    get<Question[]>('/api/questions', {
      certificationCode,
      // Omitted rather than sent as null, so the query string stays clean.
      ...(domainId ? { domainId } : {}),
      ...(difficulty ? { difficulty } : {}),
      includeRetired,
      skip,
      take,
    }),

  generate: (body: GenerateRequest) => post<GenerateResponse>('/api/questions/generate', body),

  /** Needs the operator key. */
  remove: (id: string) => del(`/api/questions/${encodeURIComponent(id)}`),

  /** Dry run: reports what a difficulty recalculation would change, and writes nothing. */
  previewDifficulty: (certificationCode?: string) =>
    get<DifficultyRecalculation>('/api/questions/difficulty', { certificationCode }),

  /** Applies the recalculation. Needs the operator key: it rewrites the shared bank. */
  recalculateDifficulty: (certificationCode?: string) =>
    post<DifficultyRecalculation>('/api/questions/difficulty', undefined, { certificationCode }),
}
