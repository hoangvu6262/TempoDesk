import type { HTMLAttributes } from 'react'
import './Text.scss'

export type TextProps = HTMLAttributes<HTMLElement> & {
  as?: 'p' | 'span' | 'div'
  variant?: 'body' | 'label' | 'caption'
  tone?: 'primary' | 'secondary' | 'danger'
}

export function Text({
  as: Component = 'p',
  variant = 'body',
  tone = 'primary',
  className,
  ...props
}: TextProps) {
  const classes = [
    'td-text',
    `td-text--${variant}`,
    `td-text--${tone}`,
    className,
  ]
    .filter(Boolean)
    .join(' ')
  return <Component className={classes} {...props} />
}
