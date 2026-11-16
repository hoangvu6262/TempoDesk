const WORKSPACE_DATE_FORMATTER = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
})

export function formatWorkspaceDate(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? 'Unknown date'
    : WORKSPACE_DATE_FORMATTER.format(date)
}
