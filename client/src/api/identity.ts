/**
 * What the browser stores about who is using it.
 *
 * Two unrelated things live here. The access token is the learner's session: the API scopes
 * everything they own - exam sessions, history, readiness, lesson progress - to the account it
 * names. The operator key is not a user credential at all; it is the shared secret that gates the
 * destructive endpoints, and the Bank page reads and writes it directly.
 *
 * Both are kept out of the HTTP layer because they are about identity rather than transport.
 *
 * The token sits in localStorage, so an XSS bug in this SPA is a stolen session. That is the
 * accepted cost of not using an HttpOnly cookie across the two dev origins; the README says so
 * plainly rather than leaving it to be discovered.
 */

const TOKEN_STORAGE = 'awscert.token'
const ADMIN_KEY_STORAGE = 'awscert.adminKey'

/** The pre-authentication per-browser learner key. Removed on sight; see purgeLegacyIdentity. */
const LEGACY_USER_KEY_STORAGE = 'awscert.userKey'

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
    // The session will not survive a reload, but it still works until then.
  }
}

function removeStored(name: string): void {
  try {
    localStorage.removeItem(name)
  } catch {
    // Nothing to do: the value was already unreachable.
  }
}

/** The access token, or null when nobody is signed in. */
export function getToken(): string | null {
  return readStored(TOKEN_STORAGE)
}

export function setToken(value: string): void {
  writeStored(TOKEN_STORAGE, value)
}

export function clearToken(): void {
  removeStored(TOKEN_STORAGE)
}

/**
 * Drops the anonymous learner key this app used before it had accounts.
 *
 * Deliberately not sent as a fallback alongside the token: a request carrying both would let the
 * server choose which identity to believe, and the whole point of the change is that it no longer
 * has to. The historical progress those keys owned was merged into an account by
 * `Data/Scripts/merge-user-keys.sql` rather than carried in the browser.
 */
export function purgeLegacyIdentity(): void {
  removeStored(LEGACY_USER_KEY_STORAGE)
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

/**
 * Forgotten on sign-out. The operator key belongs to a person, not to the machine: leaving it
 * behind would hand whoever signs in next the ability to delete from the shared question bank.
 * The Bank page asks for it again the next time the API refuses a call.
 */
export function clearAdminKey(): void {
  removeStored(ADMIN_KEY_STORAGE)
}
