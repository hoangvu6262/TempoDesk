import type { HTMLAttributes } from 'react'
import './Alert.scss'

export type AlertProps = HTMLAttributes<HTMLDivElement> & {
  variant?: 'info' | 'success' | 'warning' | 'error'
  title?: string
}

export function Alert({
  variant = 'info',
  title,
  className,
  children,
  ...props
}: AlertProps) {
  const classes = ['td-alert', `td-alert--${variant}`, className]
    .filter(Boolean)
    .join(' ')
  return (
    <div className={classes} {...props}>
      {title && <div className="td-alert__title">{title}</div>}
      <div className="td-alert__content">{children}</div>
    </div>
  )
}
