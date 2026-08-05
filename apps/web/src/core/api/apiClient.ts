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
    // A 429 deliberately falls straight through to the caller. Being throttled is not being
    // signed out, so it must never take the branch above: clearing the session would turn a
    // short wait into a forced re-login, precisely when the server is already shedding load.
    // The user-facing message belongs in the pages, where it can be specific about what the
    // caller was doing - see LoginPage and ResetPasswordPage.
    return Promise.reject(error)
  },
)
