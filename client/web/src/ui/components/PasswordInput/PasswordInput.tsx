import { IconEye, IconEyeOff } from '@tabler/icons-react'
import { useState, type InputHTMLAttributes } from 'react'
import { IconButton } from '../IconButton/IconButton'
import { Input } from '../Input/Input'
import './PasswordInput.scss'

export type PasswordInputProps = Omit<
  InputHTMLAttributes<HTMLInputElement>,
  'type'
> & {
  showPasswordLabel?: string
  hidePasswordLabel?: string
}

export function PasswordInput({
  showPasswordLabel = 'Hiện mật khẩu',
  hidePasswordLabel = 'Ẩn mật khẩu',
  className,
  disabled,
  ...props
}: PasswordInputProps) {
  const [visible, setVisible] = useState(false)
  return (
    <span className="td-password-input">
      <Input
        className={['td-password-input__control', className]
          .filter(Boolean)
          .join(' ')}
        type={visible ? 'text' : 'password'}
        disabled={disabled}
        {...props}
      />
      <span className="td-password-input__action">
        <IconButton
          aria-label={visible ? hidePasswordLabel : showPasswordLabel}
          size="small"
          disabled={disabled}
          onClick={() => setVisible((current) => !current)}
        >
          {visible ? <IconEyeOff /> : <IconEye />}
        </IconButton>
      </span>
    </span>
  )
}
