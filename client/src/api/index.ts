/**
 * The API surface, grouped the way the server is.
 *
 * Each module under `endpoints/` mirrors one controller, so finding the call for an endpoint is a
 * matter of knowing which resource it belongs to rather than scrolling one long file. Consumers
 * import `api` and reach a call through its area:
 *
 *     api.lessons.get(code, slug)
 *     api.exams.reviewCount(code)
 *     api.questions.browse({ certificationCode })
 *
 * Grouping also makes the boundary visible: `api.questions.recalculateDifficulty` sitting next to
 * `api.questions.browse` says plainly that one rewrites the shared bank and the other reads it.
 */
import { certificationsApi } from './endpoints/certifications'
import { examsApi } from './endpoints/exams'
import { insightsApi } from './endpoints/insights'
import { lessonsApi } from './endpoints/lessons'
import { questionsApi } from './endpoints/questions'
import { systemApi } from './endpoints/system'

export const api = {
  system: systemApi,
  certifications: certificationsApi,
  questions: questionsApi,
  exams: examsApi,
  lessons: lessonsApi,
  insights: insightsApi,
}

export { ApiError, http } from './http'
export { getAdminKey, setAdminKey, getUserKey } from './identity'

export type { BrowseQuestionsParams, DifficultyRecalculation } from './endpoints/questions'
export type { StartExamParams, AnswerParams } from './endpoints/exams'
export type { HealthStatus } from './endpoints/system'
