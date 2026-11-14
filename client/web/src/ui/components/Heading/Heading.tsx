import type { HTMLAttributes } from 'react'
import './Heading.scss'

export type HeadingProps = HTMLAttributes<HTMLHeadingElement> & {
  as?: 'h1' | 'h2' | 'h3'
  size?: '1' | '2' | '3'
}

export function Heading({
  as: Component = 'h2',
  size = '2',
  className,
  ...props
}: HeadingProps) {
  const classes = ['td-heading', `td-heading--${size}`, className]
    .filter(Boolean)
    .join(' ')
  return <Component className={classes} {...props} />
}
