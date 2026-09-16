import type {
  LessonDetail,
  LessonProgress,
  LessonsResponse,
  TutorAnswer,
  TutorTurn,
} from '../types'
import { get, post, put } from '../http'

/**
 * Lessons, and the notes, progress and tutor that hang off one.
 *
 * Note the split between `get` and `writeNotes`: reading a lesson never calls the AI provider, so
 * it is free to repeat, while writing the notes costs a provider call and is rate limited. The
 * client mirrors that split rather than hiding it behind one method.
 */
export const lessonsApi = {
  list: (certificationCode: string) =>
    get<LessonsResponse>(`/api/lessons/${encodeURIComponent(certificationCode)}`),

  get: (certificationCode: string, slug: string) =>
    get<LessonDetail>(
      `/api/lessons/${encodeURIComponent(certificationCode)}/${encodeURIComponent(slug)}`,
    ),

  /**
   * Writes the notes for a lesson that has none yet. A lesson that already has them is returned
   * unchanged, so this is safe to call on every first open.
   */
  writeNotes: (certificationCode: string, slug: string) =>
    post<LessonDetail>(
      `/api/lessons/${encodeURIComponent(certificationCode)}/${encodeURIComponent(slug)}/notes`,
    ),

  /** Discards the cached notes and writes them again. Needs the operator key: notes are shared. */
  rewriteNotes: (certificationCode: string, slug: string) =>
    put<LessonDetail>(
      `/api/lessons/${encodeURIComponent(certificationCode)}/${encodeURIComponent(slug)}/notes`,
    ),

  setProgress: (certificationCode: string, slug: string, completed: boolean) =>
    put<LessonProgress>(
      `/api/lessons/${encodeURIComponent(certificationCode)}/${encodeURIComponent(slug)}/progress`,
      { completed },
    ),

  /** Asks about one lesson. `history` is posted back so follow-ups keep their thread. */
  ask: (certificationCode: string, slug: string, question: string, history: TutorTurn[]) =>
    post<TutorAnswer>(
      `/api/lessons/${encodeURIComponent(certificationCode)}/${encodeURIComponent(slug)}/ask`,
      { question, history },
    ),
}
