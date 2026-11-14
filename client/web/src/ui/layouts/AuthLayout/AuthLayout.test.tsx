import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { AuthLayout } from './AuthLayout'

it('provides named authentication content and optional footer', () => {
  render(
    <AuthLayout
      title="Đăng nhập"
      description="Tiếp tục với tài khoản TempoDesk"
      footer={<a href="/privacy">Quyền riêng tư</a>}
    >
      <form aria-label="Đăng nhập" />
    </AuthLayout>,
  )
  expect(screen.getByRole('heading', { name: 'Đăng nhập' })).toBeVisible()
  expect(screen.getByRole('form', { name: 'Đăng nhập' })).toBeVisible()
  expect(screen.getByRole('link', { name: 'Quyền riêng tư' })).toBeVisible()
})
