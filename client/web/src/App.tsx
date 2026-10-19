import { Link, Route, Routes } from 'react-router'
import { HealthStatus } from './app/HealthStatus'
import { readClientConfig } from './config/env'

function Landing() {
  let config
  try {
    config = readClientConfig(import.meta.env)
  } catch {
    return (
      <section role="alert">
        <h1>Cấu hình ứng dụng không hợp lệ</h1>
        <p>
          Kiểm tra VITE_API_BASE_URL và VITE_SIGNALR_BASE_URL, sau đó khởi động
          lại ứng dụng.
        </p>
      </section>
    )
  }
  return (
    <>
      <div className="eyebrow">KHÔNG GIAN PHÁT TRIỂN · SPRINT 0</div>
      <h1>
        Nền tảng vững chắc.
        <br />
        <span>Sẵn sàng cho nhịp làm việc mới.</span>
      </h1>
      <p className="intro">
        TempoDesk kết nối công việc, thời gian và đội nhóm trong một không gian.
        Đây là điểm kiểm tra môi trường trước khi bắt đầu phát triển.
      </p>
      <section className="panel" aria-labelledby="health-heading">
        <div className="panel-heading">
          <h2 id="health-heading">Trạng thái hệ thống</h2>
          <span className="tag">MÔI TRƯỜNG PHÁT TRIỂN</span>
        </div>
        <HealthStatus baseUrl={config.apiBaseUrl} />
      </section>
      <section className="build-info" aria-labelledby="build-heading">
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

export default function App() {
  return (
    <div className="app-shell">
      <a className="skip-link" href="#main">
        Chuyển đến nội dung
      </a>
      <header>
        <Link className="brand" to="/" aria-label="TempoDesk trang chủ">
          <span className="brand-mark" aria-hidden="true">
            T
          </span>
          TempoDesk
        </Link>
        <span className="header-note">Developer workspace</span>
      </header>
      <main id="main">
        <Routes>
          <Route path="/" element={<Landing />} />
          <Route
            path="*"
            element={
              <section className="not-found">
                <div className="eyebrow">404</div>
                <h1>Không tìm thấy trang</h1>
                <p>Đường dẫn này chưa tồn tại trong TempoDesk.</p>
                <Link to="/">Về trang chủ →</Link>
              </section>
            }
          />
        </Routes>
      </main>
      <footer>
        <span>TempoDesk</span>
        <span>Foundation · Sprint 0</span>
      </footer>
    </div>
  )
}
