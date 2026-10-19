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
        <main className="fallback" role="alert">
          <h1>Đã có lỗi xảy ra</h1>
          <p>Vui lòng tải lại trang để thử lại.</p>
          <a href="/">Về trang chủ</a>
        </main>
      )
    return this.props.children
  }
}
