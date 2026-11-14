import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import {
  Alert,
  AuthLayout,
  Button,
  FormField,
  Input,
  PasswordInput,
} from '../../../ui'
import { validateEmail, validatePassword } from '../forms/form-errors'
import { safeReturnPath } from '../../../routes/auth-return-path'
import { useAuth } from '../state/AuthContext'
import './AuthPages.scss'

type LoginErrors = { email?: string; password?: string }

export function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<LoginErrors>({})
  const [requestError, setRequestError] = useState<string>()
  const [submitting, setSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const nextErrors = {
      email: validateEmail(email),
      password: validatePassword(password, 1),
    }
    setErrors(nextErrors)
    if (Object.values(nextErrors).some(Boolean)) return

    setSubmitting(true)
    setRequestError(undefined)
    try {
      await login(email, password)
      navigate(safeReturnPath(searchParams.get('returnTo')), { replace: true })
    } catch {
      setRequestError('Email or password is incorrect.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <AuthLayout
      title="Sign in"
      description="Continue to your TempoDesk workspace."
      footer={
        <>
          New to TempoDesk? <Link to="/register">Create an account</Link>
        </>
      }
    >
      <form className="td-auth-form" aria-label="Sign in" onSubmit={submit}>
        {requestError && (
          <Alert role="alert" variant="error">
            {requestError}
          </Alert>
        )}
        <FormField label="Email" error={errors.email} required>
          {(props) => (
            <Input
              {...props}
              autoComplete="email"
              disabled={submitting}
              maxLength={256}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
          )}
        </FormField>
        <FormField label="Password" error={errors.password} required>
          {(props) => (
            <PasswordInput
              {...props}
              autoComplete="current-password"
              disabled={submitting}
              maxLength={1024}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          )}
        </FormField>
        <div className="td-auth-form__links">
          <Link to="/forgot-password">Forgot password?</Link>
        </div>
        <div className="td-auth-form__actions">
          <Button type="submit" fullWidth loading={submitting}>
            Sign in
          </Button>
          <div className="td-auth-divider">or</div>
          <Button
            variant="secondary"
            fullWidth
            disabled
            aria-label="Continue with Google — unavailable"
          >
            Continue with Google · Chưa khả dụng
          </Button>
        </div>
      </form>
    </AuthLayout>
  )
}
