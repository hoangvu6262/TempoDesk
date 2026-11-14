import { Checkbox as BaseCheckbox } from '@base-ui/react/checkbox'
import { IconCheck } from '@tabler/icons-react'
import './Checkbox.scss'

export type CheckboxProps = {
  checked: boolean
  onCheckedChange: (checked: boolean) => void
  label: string
  disabled?: boolean
  name?: string
  value?: string
}

export function Checkbox({
  checked,
  onCheckedChange,
  label,
  disabled,
  name,
  value,
}: CheckboxProps) {
  return (
    <label className="td-checkbox">
      <BaseCheckbox.Root
        className="td-checkbox__control"
        checked={checked}
        onCheckedChange={(nextChecked) => onCheckedChange(nextChecked)}
        disabled={disabled}
        name={name}
        value={value}
      >
        <BaseCheckbox.Indicator className="td-checkbox__indicator">
          <IconCheck aria-hidden="true" />
        </BaseCheckbox.Indicator>
      </BaseCheckbox.Root>
      <span className="td-checkbox__label">{label}</span>
    </label>
  )
}
