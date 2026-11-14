import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import {
  Alert,
  AuthLayout,
  Button,
  FormField,
  Input,
  PasswordInput,
} from '../../../ui'
import {
  fieldError,
  pageError,
  validateConfirmation,
  validateEmail,
  validatePassword,
} from '../forms/form-errors'
import { useAuth } from '../state/AuthContext'
import './AuthPages.scss'

type RegisterErrors = {
  email?: string
  password?: string
  confirmation?: string
}

export function RegisterPage() {
  const { api } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [errors, setErrors] = useState<RegisterErrors>({})
  const [requestError, setRequestError] = useState<string>()
  const [message, setMessage] = useState<string>()
  const [submitting, setSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const nextErrors = {
      email: validateEmail(email),
      password: validatePassword(password),
      confirmation: validateConfirmation(password, confirmation),
    }
    setErrors(nextErrors)
    if (Object.values(nextErrors).some(Boolean)) return

    setSubmitting(true)
    setRequestError(undefined)
    setMessage(undefined)
    try {
      setMessage((await api.register(email, password)).message)
    } catch (error) {
      setErrors({
        email: fieldError(error, 'email'),
        password: fieldError(error, 'password'),
      })
      setRequestError(pageError(error, ['email', 'password']))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <AuthLayout
      title="Create your account"
      description="Start using TempoDesk with your work email."
      footer={
        <>
          Already have an account? <Link to="/login">Sign in</Link>
        </>
      }
    >
      <form
        className="td-auth-form"
        aria-label="Create account"
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
        <FormField
          label="Password"
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
        <FormField
          label="Confirm password"
          error={errors.confirmation}
          required
        >
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
          Create account
        </Button>
      </form>
    </AuthLayout>
  )
}
