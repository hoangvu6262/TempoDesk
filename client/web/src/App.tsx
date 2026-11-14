import { Link, Route, Routes } from 'react-router'
import { HealthStatus } from './app/HealthStatus'
import { AuthenticatedHome } from './app/AuthenticatedHome'
import { UiKitPage } from './app/UiKitPage'
import { readClientConfig } from './config/env'
import { ForgotPasswordPage } from './features/auth/pages/ForgotPasswordPage'
import { GoogleCallbackPage } from './features/auth/pages/GoogleCallbackPage'
import { LoginPage } from './features/auth/pages/LoginPage'
import { RegisterPage } from './features/auth/pages/RegisterPage'
import { ResetPasswordPage } from './features/auth/pages/ResetPasswordPage'
import { VerifyEmailPage } from './features/auth/pages/VerifyEmailPage'
import { ProtectedRoute } from './features/auth/routing/ProtectedRoute'
import type { ReactNode } from 'react'

function Landing() {
  let config
  try {
    config = readClientConfig(import.meta.env)
  } catch {
    return (
      <section role="alert">
        <h1>Application configuration is invalid</h1>
        <p>
          Check VITE_API_BASE_URL and VITE_SIGNALR_BASE_URL, then restart the
          application.
        </p>
      </section>
    )
  }
  return (
    <>
      <div className="td-landing__eyebrow">
        KHÔNG GIAN PHÁT TRIỂN · SPRINT 0
      </div>
      <h1 className="td-landing__title">
        Nền tảng vững chắc.
        <br />
        <span>Sẵn sàng cho nhịp làm việc mới.</span>
      </h1>
      <p className="td-landing__intro">
        TempoDesk kết nối công việc, thời gian và đội nhóm trong một không gian.
        Đây là điểm kiểm tra môi trường trước khi bắt đầu phát triển.
      </p>
      <section className="td-landing__panel" aria-labelledby="health-heading">
        <div className="td-landing__panel-heading">
          <h2 id="health-heading">Trạng thái hệ thống</h2>
          <span className="td-landing__tag">MÔI TRƯỜNG PHÁT TRIỂN</span>
        </div>
        <HealthStatus baseUrl={config.apiBaseUrl} />
      </section>
      <section
        className="td-landing__build-info"
        aria-labelledby="build-heading"
      >
        <h2 id="build-heading">Thông tin bản dựng</h2>
        <dl>
          <div>
            <dt>Ứng dụng</dt>
            <dd>TempoDesk Web · 0.0.0</dd>
          </div>
          <div>
            <dt>Chế độ</dt>
            <dd>{import.meta.env.MODE}</dd>
          </div>
          <div>
            <dt>Nền tảng</dt>
            <dd>React 19 · TypeScript · Vite</dd>
          </div>
        </dl>
      </section>
    </>
  )
}

function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="td-app-shell">
      <a className="td-skip-link" href="#main">
        Chuyển đến nội dung
      </a>
      <header className="td-app-shell__header">
        <Link className="td-brand" to="/" aria-label="TempoDesk trang chủ">
          <span className="td-brand__mark" aria-hidden="true">
            T
          </span>
          TempoDesk
        </Link>
        <span className="td-app-shell__header-note">Developer workspace</span>
      </header>
      <main className="td-app-shell__main" id="main">
        {children}
      </main>
      <footer className="td-app-shell__footer">
        <span>TempoDesk</span>
        <span>Foundation · Sprint 0</span>
      </footer>
    </div>
  )
}

function NotFound() {
  return (
    <section className="td-not-found">
      <div className="td-not-found__eyebrow">404</div>
      <h1 className="td-not-found__title">Page not found</h1>
      <p className="td-not-found__description">
        This page does not exist in TempoDesk.
      </p>
      <Link to="/">Back to home →</Link>
    </section>
  )
}

export default function App() {
  return (
    <Routes>
      {import.meta.env.DEV && <Route path="/ui-kit" element={<UiKitPage />} />}
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/verify-email" element={<VerifyEmailPage />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="/auth/google/callback" element={<GoogleCallbackPage />} />
      <Route
        path="/app"
        element={
          <ProtectedRoute>
            <AppShell>
              <AuthenticatedHome />
            </AppShell>
          </ProtectedRoute>
        }
      />
      <Route
        path="/"
        element={
          <AppShell>
            <Landing />
          </AppShell>
        }
      />
      <Route
        path="*"
        element={
          <AppShell>
            <NotFound />
          </AppShell>
        }
      />
    </Routes>
  )
}
