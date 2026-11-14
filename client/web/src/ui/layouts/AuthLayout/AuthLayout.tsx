import type { PropsWithChildren, ReactNode } from 'react'
import { Heading } from '../../components/Heading/Heading'
import { Text } from '../../components/Text/Text'
import './AuthLayout.scss'

export type AuthLayoutProps = PropsWithChildren<{
  title: string
  description?: string
  footer?: ReactNode
}>

export function AuthLayout({
  title,
  description,
  footer,
  children,
}: AuthLayoutProps) {
  return (
    <main className="td-auth-layout">
      <a
        className="td-auth-layout__brand"
        href="/"
        aria-label="TempoDesk trang chủ"
      >
        <span className="td-auth-layout__brand-mark" aria-hidden="true">
          T
        </span>
        TempoDesk
      </a>
      <section className="td-auth-layout__card" aria-labelledby="auth-title">
        <header className="td-auth-layout__header">
          <Heading as="h1" size="1" id="auth-title">
            {title}
          </Heading>
          {description && <Text tone="secondary">{description}</Text>}
        </header>
        <div className="td-auth-layout__content">{children}</div>
        {footer && <footer className="td-auth-layout__footer">{footer}</footer>}
      </section>
    </main>
  )
}
