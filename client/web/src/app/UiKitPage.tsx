import {
  IconArrowRight,
  IconBell,
  IconPlus,
  IconTrash,
} from '@tabler/icons-react'
import { useState } from 'react'
import {
  Alert,
  AuthLayout,
  Button,
  Checkbox,
  FormField,
  Heading,
  IconButton,
  Input,
  PasswordInput,
  Spinner,
  Text,
  useTheme,
  type ThemePreference,
} from '../ui'
import './UiKitPage.scss'

const themes: Array<{ value: ThemePreference; label: string }> = [
  { value: 'system', label: 'System' },
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
]

export function UiKitPage() {
  const { preference, setPreference } = useTheme()
  const [checked, setChecked] = useState(false)

  return (
    <main className="td-ui-kit">
      <header className="td-ui-kit__hero">
        <div>
          <Text variant="caption" tone="secondary">
            DEVELOPMENT · SCRUM-31
          </Text>
          <Heading as="h1" size="1">
            TempoDesk UI Kit
          </Heading>
          <Text tone="secondary">
            Semantic tokens, accessible components, global SCSS and BEM.
          </Text>
        </div>
        <div className="td-ui-kit__theme" aria-label="Theme preference">
          {themes.map((theme) => (
            <Button
              key={theme.value}
              variant={preference === theme.value ? 'primary' : 'secondary'}
              size="small"
              aria-pressed={preference === theme.value}
              onClick={() => setPreference(theme.value)}
            >
              {theme.label}
            </Button>
          ))}
        </div>
      </header>

      <section
        className="td-ui-kit__section"
        aria-labelledby="typography-title"
      >
        <Heading as="h2" size="2" id="typography-title">
          Typography
        </Heading>
        <div className="td-ui-kit__stack">
          <Heading as="h3" size="1">
            Heading one
          </Heading>
          <Heading as="h3" size="2">
            Heading two
          </Heading>
          <Heading as="h3" size="3">
            Heading three
          </Heading>
          <Text>Body text for primary content.</Text>
          <Text variant="label">Label text</Text>
          <Text variant="caption" tone="secondary">
            Secondary caption
          </Text>
          <Text tone="danger">Validation message</Text>
        </div>
      </section>

      <section className="td-ui-kit__section" aria-labelledby="actions-title">
        <Heading as="h2" size="2" id="actions-title">
          Actions
        </Heading>
        <div className="td-ui-kit__row">
          <Button startIcon={<IconPlus />}>Tạo công việc</Button>
          <Button variant="secondary" endIcon={<IconArrowRight />}>
            Tiếp tục
          </Button>
          <Button variant="ghost">Bỏ qua</Button>
          <Button variant="danger" startIcon={<IconTrash />}>
            Xóa
          </Button>
          <Button loading>Đang lưu</Button>
          <Button disabled>Đã tắt</Button>
          <IconButton aria-label="Thông báo">
            <IconBell />
          </IconButton>
          <IconButton aria-label="Đang xử lý" loading>
            <IconBell />
          </IconButton>
        </div>
      </section>

      <section className="td-ui-kit__section" aria-labelledby="forms-title">
        <Heading as="h2" size="2" id="forms-title">
          Form controls
        </Heading>
        <div className="td-ui-kit__form-grid">
          <FormField label="Email" description="Dùng email công việc">
            {(props) => (
              <Input {...props} type="email" placeholder="you@company.com" />
            )}
          </FormField>
          <FormField label="Email lỗi" error="Email không hợp lệ">
            {(props) => (
              <Input {...props} type="email" defaultValue="invalid" />
            )}
          </FormField>
          <FormField label="Mật khẩu" required>
            {(props) => <PasswordInput {...props} />}
          </FormField>
          <FormField label="Trường bị tắt">
            {(props) => (
              <Input {...props} disabled value="Không thể sửa" readOnly />
            )}
          </FormField>
        </div>
        <Checkbox
          checked={checked}
          onCheckedChange={setChecked}
          label="Ghi nhớ lựa chọn"
        />
      </section>

      <section className="td-ui-kit__section" aria-labelledby="feedback-title">
        <Heading as="h2" size="2" id="feedback-title">
          Feedback
        </Heading>
        <div className="td-ui-kit__stack">
          <Alert title="Thông tin">Cập nhật mới đã sẵn sàng.</Alert>
          <Alert variant="success" title="Thành công">
            Thay đổi đã được lưu.
          </Alert>
          <Alert variant="warning" title="Lưu ý">
            Phiên làm việc sắp hết hạn.
          </Alert>
          <Alert variant="error" title="Có lỗi">
            Không thể hoàn thành thao tác.
          </Alert>
          <div className="td-ui-kit__row">
            <Spinner size="small" label="Đang tải nhỏ" />
            <Spinner label="Đang tải" />
          </div>
        </div>
      </section>

      <section
        className="td-ui-kit__section"
        aria-labelledby="auth-layout-title"
      >
        <Heading as="h2" size="2" id="auth-layout-title">
          Auth layout
        </Heading>
        <div className="td-ui-kit__auth-preview">
          <AuthLayout
            title="Đăng nhập"
            description="Tiếp tục với tài khoản TempoDesk"
            footer={<a href="/privacy">Quyền riêng tư</a>}
          >
            <form className="td-ui-kit__stack" aria-label="Đăng nhập mẫu">
              <FormField label="Email">
                {(props) => <Input {...props} type="email" />}
              </FormField>
              <Button fullWidth>Tiếp tục</Button>
            </form>
          </AuthLayout>
        </div>
      </section>
    </main>
  )
}
