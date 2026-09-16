import type { Certification } from '../types'
import { get } from '../http'

/** Certifications are seeded and read-only, so this is a list and an item. */
export const certificationsApi = {
  list: () => get<Certification[]>('/api/certifications'),

  getByCode: (code: string) => get<Certification>(`/api/certifications/${encodeURIComponent(code)}`),
}
