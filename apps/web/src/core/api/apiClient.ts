import axios from 'axios'

const TOKEN_STORAGE_KEY = 'psmpe.auth.token'

export const tokenStorage = {
  get: () => localStorage.getItem(TOKEN_STORAGE_KEY),
  set: (token: string) => localStorage.setItem(TOKEN_STORAGE_KEY, token),
  clear: () => localStorage.removeItem(TOKEN_STORAGE_KEY),
}

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000'

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
})

apiClient.interceptors.request.use((config) => {
  const token = tokenStorage.get()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

export type RateLimitListener = (retryAfterSeconds: number) => void

let rateLimitListener: RateLimitListener | null = null

/** Lets the UI surface a wait time without this module importing anything from the UI layer. */
export const onRateLimited = (listener: RateLimitListener | null) => {
  rateLimitListener = listener
}

// TODO: implement refresh-token rotation once the backend issues refresh tokens;
// for now a 401 simply clears the session and sends the user back to /login.
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      tokenStorage.clear()
      if (window.location.pathname !== '/login') {
        window.location.assign('/login')
      }
    }
    // Deliberately a separate branch, not an `else if` chained onto the 401 above: being
    // throttled is not being signed out. Clearing the session here would turn a brief wait
    // into a forced re-login, and would do it precisely when the server is already under load.
    if (error.response?.status === 429) {
      const header = error.response.headers?.['retry-after']
      const retryAfterSeconds = Number.parseInt(header ?? '', 10)
      rateLimitListener?.(Number.isFinite(retryAfterSeconds) ? retryAfterSeconds : 60)
    }
    return Promise.reject(error)
  },
)
