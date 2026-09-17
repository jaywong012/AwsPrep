import type { LessonsResponse } from '../api/types'

/**
 * The lessons list, kept for the life of the tab.
 *
 * The curriculum is 103 topics and it does not change while you read it, so coming back from a
 * lesson should not mean a spinner and a refetch. This lives at module scope precisely so it
 * survives the Lessons component unmounting, which is exactly what happens when you open a
 * lesson and press back.
 *
 * Its own file rather than an export beside the page component: a module that exports both a
 * component and a helper loses React Fast Refresh for the whole file.
 *
 * What does go stale is a topic's done flag and whether its notes exist, so the Lesson page
 * drops its certification from here whenever it changes either.
 */
const cache = new Map<string, LessonsResponse>()

export function getCachedLessons(code: string): LessonsResponse | undefined {
  return cache.get(code)
}

export function setCachedLessons(code: string, value: LessonsResponse) {
  cache.set(code, value)
}

export function invalidateLessonCache(code?: string) {
  if (code) cache.delete(code)
  else cache.clear()
}
