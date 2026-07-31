// Mirrors the backend's Identity password policy (see DependencyInjection.AddInfrastructure):
// RequiredLength = 8, RequireNonAlphanumeric = false; RequireDigit/Uppercase/Lowercase are
// Identity's unmodified defaults (true).
export function passwordErrors(password: string): string[] {
  const errors: string[] = []
  if (password.length < 8) errors.push('Password must be at least 8 characters.')
  if (!/[0-9]/.test(password)) errors.push('Password must contain at least one digit.')
  if (!/[A-Z]/.test(password)) errors.push('Password must contain at least one uppercase letter.')
  if (!/[a-z]/.test(password)) errors.push('Password must contain at least one lowercase letter.')
  return errors
}
