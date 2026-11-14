import { Component, type ReactNode } from 'react'
export class ErrorBoundary extends Component<
  { children: ReactNode },
  { failed: boolean }
> {
  state = { failed: false }
  static getDerivedStateFromError() {
    return { failed: true }
  }
  render() {
    if (this.state.failed)
      return (
        <main className="td-error-fallback" role="alert">
          <h1>Something went wrong</h1>
          <p>Reload the page and try again.</p>
          <a href="/">Back to home</a>
        </main>
      )
    return this.props.children
  }
}
