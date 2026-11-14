import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { Alert, AuthLayout, Button, FormField, Input } from '../../../ui'
import { fieldError, pageError, validateEmail } from '../forms/form-errors'
import { useAuth } from '../state/AuthContext'
import './AuthPages.scss'

export function ForgotPasswordPage() {
  const { api } = useAuth()
  const [email, setEmail] = useState('')
  const [emailError, setEmailError] = useState<string>()
  const [requestError, setRequestError] = useState<string>()
  const [message, setMessage] = useState<string>()
  const [submitting, setSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const nextError = validateEmail(email)
    setEmailError(nextError)
    if (nextError) return

    setSubmitting(true)
    setRequestError(undefined)
    setMessage(undefined)
    try {
      setMessage((await api.forgotPassword(email)).message)
    } catch (error) {
      setEmailError(fieldError(error, 'email'))
      setRequestError(pageError(error, ['email']))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <AuthLayout
      title="Forgot your password?"
      description="Enter your email and we will send a reset link if the account exists."
      footer={<Link to="/login">Back to sign in</Link>}
    >
      <form
        className="td-auth-form"
        aria-label="Forgot password"
        onSubmit={submit}
      >
        {message && (
          <Alert role="alert" variant="success">
            {message}
          </Alert>
        )}
        {requestError && (
          <Alert role="alert" variant="error">
            {requestError}
          </Alert>
        )}
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
          Send reset link
        </Button>
      </form>
    </AuthLayout>
  )
}
