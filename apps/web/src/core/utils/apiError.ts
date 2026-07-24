import { isAxiosError } from 'axios'

/**
 * Surfaces the actual cause instead of a generic "something went wrong" - distinguishes a real
 * field-validation message from the server, an unrelated server error, and the backend being
 * unreachable entirely (e.g. Postgres/API not running).
 */
export function describeError(err: unknown, fallback: string): string {
  if (isAxiosError(err)) {
    if (err.response) {
      const message = (err.response.data as { message?: string } | undefined)?.message
      return message ?? `Server error (${err.response.status}). Please try again.`
    }
    return 'Could not reach the server. Please check your connection and try again.'
  }
  return fallback
}
