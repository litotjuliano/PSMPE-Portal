import { forwardRef, useState, type InputHTMLAttributes } from 'react'
import { LuEye, LuEyeOff } from 'react-icons/lu'

interface AuthTextInputProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string
  error?: string
}

export const AuthTextInput = forwardRef<HTMLInputElement, AuthTextInputProps>(
  ({ label, error, id, type, className, ...inputProps }, ref) => {
    const [visible, setVisible] = useState(false)
    const isPassword = type === 'password'
    const resolvedType = isPassword && visible ? 'text' : type

    return (
      <div className="mb-4">
        <label htmlFor={id} className="block font-semibold text-default-900 text-base mb-2">
          {label}
        </label>
        <div className="relative">
          <input
            id={id}
            ref={ref}
            type={resolvedType}
            className={`form-input auth-input ${isPassword ? 'pr-11' : ''} ${className ?? ''}`}
            {...inputProps}
          />
          {isPassword && (
            <button
              type="button"
              onClick={() => setVisible((v) => !v)}
              aria-label={visible ? 'Hide password' : 'Show password'}
              className="absolute right-3 top-1/2 -translate-y-1/2 text-default-400 hover:text-default-600"
            >
              {visible ? <LuEyeOff className="size-4.5" /> : <LuEye className="size-4.5" />}
            </button>
          )}
        </div>
        {error && <p className="text-xs text-danger mt-1">{error}</p>}
      </div>
    )
  },
)

AuthTextInput.displayName = 'AuthTextInput'
