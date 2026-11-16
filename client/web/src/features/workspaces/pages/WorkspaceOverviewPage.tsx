import { Link } from 'react-router'
import { useSelectedWorkspace } from '../routes/useSelectedWorkspace'
import './WorkspacePages.scss'

export function WorkspaceOverviewPage() {
  const workspace = useSelectedWorkspace()

  return (
    <section className="td-workspace-page" aria-labelledby="workspace-heading">
      <p className="td-workspace-page__eyebrow">
        {workspace.type === 'personal'
          ? 'Personal Workspace'
          : 'Team Workspace'}
      </p>
      <h1 id="workspace-heading">{workspace.name}</h1>
      <p className="td-workspace-page__intro">
        You have <strong>{workspace.role}</strong> access to this workspace.
      </p>
      {workspace.type === 'team' && (
        <Link to="/app/workspaces/new">Create another Team Workspace</Link>
      )}
    </section>
  )
}
