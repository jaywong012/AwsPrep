import type { Readiness } from '../types'
import { get } from '../http'

/** ML-backed readiness projection. Rate limited server-side: a cold call trains a model. */
export const insightsApi = {
  readiness: (certificationCode: string) =>
    get<Readiness>(`/api/insights/${encodeURIComponent(certificationCode)}/readiness`),
}
