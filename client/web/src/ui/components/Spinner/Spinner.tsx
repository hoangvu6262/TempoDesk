import './Spinner.scss'

export type SpinnerProps = {
  label?: string
  size?: 'small' | 'medium'
}

export function Spinner({ label, size = 'medium' }: SpinnerProps) {
  return (
    <span
      className={`td-spinner td-spinner--${size}`}
      role={label ? 'status' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
    >
      <span className="td-spinner__circle" />
    </span>
  )
}
