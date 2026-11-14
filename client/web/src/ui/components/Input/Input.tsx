import type { InputHTMLAttributes } from 'react'
import './Input.scss'

export type InputProps = InputHTMLAttributes<HTMLInputElement>

export function Input({
  className,
  'aria-invalid': invalid,
  ...props
}: InputProps) {
  const classes = ['td-input', invalid && 'td-input--invalid', className]
    .filter(Boolean)
    .join(' ')
  return <input className={classes} aria-invalid={invalid} {...props} />
}
