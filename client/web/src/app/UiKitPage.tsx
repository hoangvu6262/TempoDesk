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
          <Button startIcon={<IconPlus />}>Create task</Button>
          <Button variant="secondary" endIcon={<IconArrowRight />}>
            Continue
          </Button>
          <Button variant="ghost">Skip</Button>
          <Button variant="danger" startIcon={<IconTrash />}>
            Delete
          </Button>
          <Button loading>Saving</Button>
          <Button disabled>Disabled</Button>
          <IconButton aria-label="Notifications">
            <IconBell />
          </IconButton>
          <IconButton aria-label="Processing" loading>
            <IconBell />
          </IconButton>
        </div>
      </section>

      <section className="td-ui-kit__section" aria-labelledby="forms-title">
        <Heading as="h2" size="2" id="forms-title">
          Form controls
        </Heading>
        <div className="td-ui-kit__form-grid">
          <FormField label="Email" description="Use your work email">
            {(props) => (
              <Input {...props} type="email" placeholder="you@company.com" />
            )}
          </FormField>
          <FormField label="Invalid email" error="Enter a valid email address.">
            {(props) => (
              <Input {...props} type="email" defaultValue="invalid" />
            )}
          </FormField>
          <FormField label="Password" required>
            {(props) => <PasswordInput {...props} />}
          </FormField>
          <FormField label="Disabled field">
            {(props) => (
              <Input {...props} disabled value="Read only" readOnly />
            )}
          </FormField>
        </div>
        <Checkbox
          checked={checked}
          onCheckedChange={setChecked}
          label="Remember preference"
        />
      </section>

      <section className="td-ui-kit__section" aria-labelledby="feedback-title">
        <Heading as="h2" size="2" id="feedback-title">
          Feedback
        </Heading>
        <div className="td-ui-kit__stack">
          <Alert title="Information">A new update is available.</Alert>
          <Alert variant="success" title="Success">
            Changes have been saved.
          </Alert>
          <Alert variant="warning" title="Warning">
            Your session is about to expire.
          </Alert>
          <Alert variant="error" title="Error">
            Unable to complete the action.
          </Alert>
          <div className="td-ui-kit__row">
            <Spinner size="small" label="Loading small" />
            <Spinner label="Loading" />
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
            title="Sign in"
            description="Continue with your TempoDesk account"
            footer={<a href="/privacy">Privacy</a>}
          >
            <form className="td-ui-kit__stack" aria-label="Sign-in example">
              <FormField label="Email">
                {(props) => <Input {...props} type="email" />}
              </FormField>
              <Button fullWidth>Continue</Button>
            </form>
          </AuthLayout>
        </div>
      </section>
    </main>
  )
}
