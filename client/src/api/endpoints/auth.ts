import { get, post } from '../http'

/** What the server returns from registering or signing in. */
export interface AuthResult {
  token: string
  /** When the token stops being accepted, so the SPA can renew before a call fails. */
  expiresAtUtc: string
  userId: string
  email: string
}

/** Who the bearer token belongs to. */
export interface CurrentUser {
  userId: string
  email: string
}

export interface Credentials {
  email: string
  password: string
}

/**
 * Accounts and access tokens.
 *
 * These are the only calls that may be made without a token, which is why they live together:
 * `http.ts` treats a 401 from anywhere under /api/auth as an answer rather than as an expired
 * session, and that exemption is keyed on the path prefix these share.
 *
 * There is no `logout`. The token is stateless, so signing out is deleting it locally.
 */
export const authApi = {
  register: (body: Credentials) => post<AuthResult>('/api/auth/register', body),

  login: (body: Credentials) => post<AuthResult>('/api/auth/login', body),

  /** Turns a stored token back into a session on start-up, and proves it is still good. */
  me: () => get<CurrentUser>('/api/auth/me'),

  /** Exchanges a still-valid token for a fresh one. */
  refresh: () => post<AuthResult>('/api/auth/refresh'),
}
