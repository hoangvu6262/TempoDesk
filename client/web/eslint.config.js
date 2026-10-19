import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    files: [
      'src/features/**/*.{ts,tsx}',
      'src/app/**/*.{ts,tsx}',
      'src/App.tsx',
    ],
    ignores: ['**/*.test.*'],
    rules: {
      'no-restricted-globals': [
        'error',
        { name: 'fetch', message: 'Use the shared API client.' },
      ],
      'no-restricted-syntax': [
        'error',
        {
          selector:
            "CallExpression[callee.object.name=/^(window|globalThis)$/][callee.property.name='fetch']",
          message: 'Use the shared API client.',
        },
      ],
    },
  },
])
