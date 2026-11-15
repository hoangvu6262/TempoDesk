import { Link } from 'react-router'
import { AuthLayout } from '../../../ui'

export function GoogleCallbackPage() {
  return (
    <AuthLayout
      title="Google sign-in is not configured"
      description="Google sign-in will be available after server integration."
    >
      <Link to="/login">Back to sign in</Link>
    </AuthLayout>
  )
}
