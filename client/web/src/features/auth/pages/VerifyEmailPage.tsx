import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { Alert, AuthLayout, Button, FormField, Input } from '../../../ui'
import {
  fieldError,
  pageError,
  validateEmail,
} from '../forms/form-errors'
import { useAuth } from '../state/AuthContext'
import './AuthPages.scss'

type VerificationState = 'pending' | 'success' | 'error'

export function VerifyEmailPage() {
  const { api } = useAuth()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token')
  const validToken = Boolean(token && token.length <= 512)
  const started = useRef(false)
  const [state, setState] = useState<VerificationState>('pending')
  const [requestError, setRequestError] = useState<string>()
  const [email, setEmail] = useState('')
  const [emailError, setEmailError] = useState<string>()
  const [message, setMessage] = useState<string>()
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    if (!validToken || started.current || !token) return
    started.current = true
    void api
      .verifyEmail(token)
      .then(() => setState('success'))
      .catch((error: unknown) => {
        setRequestError(
          fieldError(error, 'token') ?? pageError(error, []),
        )
        setState('error')
      })
  }, [api, token, validToken])

  async function resend(event: FormEvent) {
    event.preventDefault()
    const nextError = validateEmail(email)
    setEmailError(nextError)
    if (nextError) return
    setSubmitting(true)
    setMessage(undefined)
    try {
      setMessage((await api.resendVerification(email)).message)
      setRequestError(undefined)
    } catch (error) {
      setEmailError(fieldError(error, 'email'))
      setRequestError(pageError(error, ['email']))
    } finally {
      setSubmitting(false)
    }
  }

  if (state === 'pending' && validToken) {
    return (
      <AuthLayout title="Verify your email">
        <div role="status">Verifying your email…</div>
      </AuthLayout>
    )
  }

  if (state === 'success') {
    return (
      <AuthLayout title="Email verified">
        <Alert variant="success">Your email address is ready to use.</Alert>
        <p><Link to="/login">Sign in</Link></p>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      title="We could not verify your email"
      description="Request a new link below."
      footer={<Link to="/login">Back to sign in</Link>}
    >
      <form className="td-auth-form" aria-label="Resend verification" onSubmit={resend}>
        {(requestError || !validToken) && (
          <Alert role="alert" variant="error">
            {requestError ?? 'The verification link is invalid or expired.'}
          </Alert>
        )}
        {message && <Alert role="alert" variant="success">{message}</Alert>}
        <FormField label="Email" error={emailError} required>
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
        <Button type="submit" fullWidth loading={submitting}>
          Resend verification
        </Button>
      </form>
    </AuthLayout>
  )
}
