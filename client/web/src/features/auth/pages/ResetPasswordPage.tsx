import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import {
  Alert,
  AuthLayout,
  Button,
  FormField,
  PasswordInput,
} from '../../../ui'
import {
  fieldError,
  pageError,
  validateConfirmation,
  validatePassword,
} from '../forms/form-errors'
import { useAuth } from '../state/AuthContext'
import './AuthPages.scss'

type ResetErrors = { password?: string; confirmation?: string }

export function ResetPasswordPage() {
  const { api } = useAuth()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token')
  const validToken = Boolean(token && token.length <= 512)
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [errors, setErrors] = useState<ResetErrors>({})
  const [requestError, setRequestError] = useState<string>()
  const [submitting, setSubmitting] = useState(false)
  const [complete, setComplete] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const passwordError = validatePassword(password)
    const confirmationError = validateConfirmation(password, confirmation)
    setErrors({ password: passwordError, confirmation: confirmationError })
    if (!token || !validToken || passwordError || confirmationError) return

    setSubmitting(true)
    setRequestError(undefined)
    try {
      await api.resetPassword(token, password)
      setComplete(true)
    } catch (error) {
      setErrors({ password: fieldError(error, 'newPassword') })
      setRequestError(
        fieldError(error, 'token') ??
          pageError(error, ['newPassword', 'token']),
      )
    } finally {
      setSubmitting(false)
    }
  }

  if (!validToken) {
    return (
      <AuthLayout title="Reset your password">
        <Alert role="alert" variant="error">
          The reset link is invalid or expired.
        </Alert>
        <p><Link to="/forgot-password">Request a new reset link</Link></p>
      </AuthLayout>
    )
  }

  if (complete) {
    return (
      <AuthLayout title="Password reset">
        <Alert variant="success">Your password has been updated.</Alert>
        <p><Link to="/login">Sign in</Link></p>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      title="Reset your password"
      description="Choose a new password for your TempoDesk account."
      footer={<Link to="/login">Back to sign in</Link>}
    >
      <form className="td-auth-form" aria-label="Reset password" onSubmit={submit}>
        {requestError && <Alert role="alert" variant="error">{requestError}</Alert>}
        <FormField
          label="New password"
          description="Use at least 12 characters."
          error={errors.password}
          required
        >
          {(props) => (
            <PasswordInput
              {...props}
              autoComplete="new-password"
              disabled={submitting}
              maxLength={1024}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          )}
        </FormField>
        <FormField label="Confirm password" error={errors.confirmation} required>
          {(props) => (
            <PasswordInput
              {...props}
              autoComplete="new-password"
              disabled={submitting}
              maxLength={1024}
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
            />
          )}
        </FormField>
        <Button type="submit" fullWidth loading={submitting}>
          Reset password
        </Button>
      </form>
    </AuthLayout>
  )
}
