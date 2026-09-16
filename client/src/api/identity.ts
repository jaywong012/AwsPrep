/**
 * Who the caller is, from the browser's point of view.
 *
 * There is no authentication in this app: the SPA mints a stable key per browser and the API
 * scopes everything a learner owns - exam sessions, history, readiness, lesson progress - to that
 * value. Kept apart from the HTTP layer because it is about identity, not transport, and because
 * the Bank page reads and writes the operator key directly.
 */

const USER_KEY_STORAGE = 'awscert.userKey'
const ADMIN_KEY_STORAGE = 'awscert.adminKey'

/** Storage throws in private-mode Safari and when site data is blocked, so every access is guarded. */
function readStored(name: string): string | null {
  try {
    return localStorage.getItem(name)
  } catch {
    return null
  }
}

function writeStored(name: string, value: string): void {
  try {
    localStorage.setItem(name, value)
  } catch {
    // Progress will not survive a reload, but the session still works.
  }
}

/** Stable per-browser learner id, so progress survives reloads without auth. */
export function getUserKey(): string {
  const stored = readStored(USER_KEY_STORAGE)
  if (stored) return stored

  // crypto.randomUUID needs a secure context; a plain-http deployment falls back.
  const random =
    typeof crypto !== 'undefined' && 'randomUUID' in crypto
      ? crypto.randomUUID().slice(0, 8)
      : Math.random().toString(36).slice(2, 10)

  const key = `user-${random}`
  writeStored(USER_KEY_STORAGE, key)
  return key
}

/**
 * Key for the endpoints the API gates behind Admin:ApiKey — deleting a question, rewriting shared
 * lesson notes, re-rating the bank. It is an operator's key, not a user credential, so it is
 * stored per browser and never bundled into the build.
 */
export function getAdminKey(): string {
  return readStored(ADMIN_KEY_STORAGE) ?? ''
}

export function setAdminKey(value: string): void {
  writeStored(ADMIN_KEY_STORAGE, value.trim())
}
