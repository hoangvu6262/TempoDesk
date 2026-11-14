import type { ButtonHTMLAttributes } from 'react'
import type { ButtonProps } from '../Button/Button'
import { Spinner } from '../Spinner/Spinner'
import './IconButton.scss'

export type IconButtonProps = Omit<
  ButtonHTMLAttributes<HTMLButtonElement>,
  'aria-label'
> & {
  'aria-label': string
  variant?: ButtonProps['variant']
  size?: ButtonProps['size']
  loading?: boolean
}

export function IconButton({
  variant = 'ghost',
  size = 'medium',
  loading = false,
  disabled,
  className,
  children,
  type = 'button',
  ...props
}: IconButtonProps) {
  const classes = [
    'td-icon-button',
    `td-icon-button--${variant}`,
    `td-icon-button--${size}`,
    loading && 'td-icon-button--loading',
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
      <span className="td-icon-button__content" aria-hidden="true">
        {loading ? <Spinner size="small" /> : children}
      </span>
    </button>
  )
}
