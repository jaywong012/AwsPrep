import axios, { AxiosError, type AxiosInstance } from 'axios'
import { getAdminKey, getUserKey } from './identity'

/**
 * Where the API lives. A production build usually serves the SPA from the API's own origin, or
 * behind a proxy that forwards /api, so an unset VITE_API_BASE_URL means same-origin there -
 * defaulting to localhost would ship a broken bundle.
 */
const BASE = (
  import.meta.env.VITE_API_BASE_URL ?? (import.meta.env.DEV ? 'http://localhost:5176' : '')
).replace(/\/+$/, '')

/**
 * A failed request, in the shape the UI actually branches on.
 *
 * Kept as its own class rather than letting AxiosError escape: pages check `status === 429` and
 * show `message`, and they should not have to know which HTTP library is underneath. Swapping the
 * transport again would not touch a single catch block.
 */
export class ApiError extends Error {
  status: number
  title?: string

  constructor(message: string, status: number, title?: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.title = title
  }
}

/** The API's ProblemDetails shape, plus the plainer `{ message }` a few endpoints return. */
interface ProblemDetails {
  title?: string
  detail?: string
  message?: string
}

export const http: AxiosInstance = axios.create({
  baseURL: BASE,
  headers: { 'Content-Type': 'application/json' },
})

// Identity is attached per request, not at creation: the operator key can be set from the Bank
// page mid-session, and a client built once at import time would keep sending the old value.
http.interceptors.request.use((config) => {
  config.headers.set('X-User-Key', getUserKey())

  const adminKey = getAdminKey()
  if (adminKey) config.headers.set('X-Admin-Key', adminKey)

  return config
})

// One place turns a transport failure into an ApiError, so no endpoint module repeats it.
http.interceptors.response.use(
  (response) => response,
  (error: unknown) => {
    if (!(error instanceof AxiosError)) throw error

    const status = error.response?.status ?? 0
    const problem = error.response?.data as ProblemDetails | undefined

    // A request that never reached the server has no status: say so plainly rather than
    // reporting "status 0", which tells the learner nothing.
    if (!error.response) {
      throw new ApiError(
        'Could not reach the API. Check that it is running.',
        0,
        'API unreachable',
      )
    }

    const detail =
      problem?.detail ?? problem?.message ?? error.message ?? `Request failed with status ${status}`

    const title = problem?.title ?? (status === 429 ? 'Too many requests' : undefined)

    throw new ApiError(detail, status, title)
  },
)

/** Unwraps the body, so endpoint modules read as `get<Thing>(path)` rather than juggling responses. */
export async function get<T>(url: string, params?: Record<string, unknown>): Promise<T> {
  const { data } = await http.get<T>(url, { params })
  return data
}

export async function post<T>(url: string, body?: unknown, params?: Record<string, unknown>): Promise<T> {
  const { data } = await http.post<T>(url, body ?? null, { params })
  return data
}

export async function put<T>(url: string, body?: unknown, params?: Record<string, unknown>): Promise<T> {
  const { data } = await http.put<T>(url, body ?? null, { params })
  return data
}

export async function del(url: string): Promise<void> {
  await http.delete(url)
}
