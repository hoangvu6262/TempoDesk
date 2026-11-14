import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { Spinner } from '../Spinner/Spinner'
import './Button.scss'

export type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger'
  size?: 'small' | 'medium'
  loading?: boolean
  fullWidth?: boolean
  startIcon?: ReactNode
  endIcon?: ReactNode
}

export function Button({
  variant = 'primary',
  size = 'medium',
  loading = false,
  fullWidth = false,
  startIcon,
  endIcon,
  disabled,
  className,
  children,
  type = 'button',
  ...props
}: ButtonProps) {
  const classes = [
    'td-button',
    `td-button--${variant}`,
    `td-button--${size}`,
    loading && 'td-button--loading',
    fullWidth && 'td-button--full-width',
    className,
  ]
    .filter(Boolean)
    .join(' ')

  return (
    <button
      className={classes}
      type={type}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...props}
    >
      <span className="td-button__content">
        {startIcon && (
          <span className="td-button__icon" aria-hidden="true">
            {startIcon}
          </span>
        )}
        {children}
        {endIcon && (
          <span className="td-button__icon" aria-hidden="true">
            {endIcon}
          </span>
        )}
      </span>
      {loading && (
        <span className="td-button__loader" aria-hidden="true">
          <Spinner size="small" />
        </span>
      )}
    </button>
  )
}
