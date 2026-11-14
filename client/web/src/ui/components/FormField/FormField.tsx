import { useId, type ReactNode } from 'react'
import './FormField.scss'

export type FieldControlProps = {
  id: string
  'aria-describedby'?: string
  'aria-invalid'?: true
}

export type FormFieldProps = {
  label: string
  description?: string
  error?: string
  required?: boolean
  children: (props: FieldControlProps) => ReactNode
}

export function FormField({
  label,
  description,
  error,
  required,
  children,
}: FormFieldProps) {
  const id = useId()
  const descriptionId = description ? `${id}-description` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [descriptionId, errorId].filter(Boolean).join(' ')

  return (
    <div className="td-form-field">
      <label className="td-form-field__label" htmlFor={id}>
        {label}
        {required && (
          <>
            <span className="td-form-field__required" aria-hidden="true">
              *
            </span>
            <span className="td-visually-hidden"> bắt buộc</span>
          </>
        )}
      </label>
      {description && (
        <span className="td-form-field__description" id={descriptionId}>
          {description}
        </span>
      )}
      {children({
        id,
        'aria-describedby': describedBy || undefined,
        'aria-invalid': error ? true : undefined,
      })}
      {error && (
        <span className="td-form-field__error" id={errorId} role="alert">
          {error}
        </span>
      )}
    </div>
  )
}
