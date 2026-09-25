import type { ReactNode } from 'react'
import { Link } from '../router'
import { AccountMenu } from './AccountMenu'
import type { Theme } from '../hooks/useTheme'

/**
 * The nav icons.
 *
 * Collapsed, the icon is the only thing left, so each one has to carry the whole meaning of its
 * destination. Drawn inline rather than pulled from a set, to match the handful of icons already
 * in this app (the PDF glyph on the lessons list, the service tiles) - same 1.8 stroke, same
 * currentColor, so they take the nav's own colour in either theme.
 */
const ICONS: Record<string, ReactNode> = {
  dashboard: <path d="M4 13h6V4H4v9Zm0 7h6v-5H4v5Zm10 0h6v-9h-6v9Zm0-16v5h6V4h-6Z" />,
  lessons: <path d="M4 5.5A1.5 1.5 0 0 1 5.5 4H11v16H5.5A1.5 1.5 0 0 1 4 18.5v-13ZM11 4h7.5A1.5 1.5 0 0 1 20 5.5v13a1.5 1.5 0 0 1-1.5 1.5H11" />,
  generate: <path d="m12 3 1.9 4.6L18.5 9.5l-4.6 1.9L12 16l-1.9-4.6L5.5 9.5l4.6-1.9L12 3Zm6 10 .9 2.1 2.1.9-2.1.9L18 19l-.9-2.1-2.1-.9 2.1-.9L18 13Z" />,
  bank: <path d="M4 7c0-1.1 3.6-2 8-2s8 .9 8 2-3.6 2-8 2-8-.9-8-2Zm0 0v10c0 1.1 3.6 2 8 2s8-.9 8-2V7M4 12c0 1.1 3.6 2 8 2s8-.9 8-2" />,
  history: <path d="M12 7v5l3.5 2M3.5 12a8.5 8.5 0 1 0 2.6-6.1M5 4v4h4" />,
}

export interface NavItem {
  to: string
  label: string
  icon: keyof typeof ICONS | string
}

function NavIcon({ name }: { name: string }) {
  const filled = name === 'dashboard'

  return (
    <svg
      className="nav-icon"
      width="19"
      height="19"
      viewBox="0 0 24 24"
      fill={filled ? 'currentColor' : 'none'}
      stroke={filled ? 'none' : 'currentColor'}
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {ICONS[name]}
    </svg>
  )
}

/** A chevron that points the way the sidebar will move. */
function CollapseIcon({ collapsed }: { collapsed: boolean }) {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={collapsed ? 'M9 6l6 6-6 6' : 'M15 6l-6 6 6 6'} />
    </svg>
  )
}

/**
 * The app's navigation, down the left.
 *
 * It holds everything that is about the app rather than the page: where you can go, who you are,
 * and whether the API is answering. Collapsed it keeps the icons and drops the labels, which is
 * the whole point - the five destinations do not change, so once they are learned the words are
 * just width the content could be using.
 */
export function Sidebar({
  items,
  currentPath,
  collapsed,
  collapseForced,
  onToggleCollapse,
  offlineBadge,
  user,
  theme,
  onToggleTheme,
  onSignOut,
}: {
  items: NavItem[]
  currentPath: string
  collapsed: boolean
  collapseForced: boolean
  onToggleCollapse: () => void
  offlineBadge?: ReactNode
  user: { email: string }
  theme: Theme
  onToggleTheme: () => void
  onSignOut: () => void
}) {
  return (
    <aside className="sidebar" data-collapsed={collapsed ? 'true' : 'false'}>
      <div className="sidebar-head">
        <Link to="/" className="brand" title="AWS CertPrep">
          <span className="brand-mark">AWS</span>
          <span className="sidebar-label">CertPrep</span>
        </Link>

        {/* Hidden when the viewport is what collapsed it: a button that cannot change anything
            is worse than no button, and there is nowhere to put the labels at this width. */}
        {!collapseForced && (
          <button
            type="button"
            className="sidebar-collapse"
            onClick={onToggleCollapse}
            aria-expanded={!collapsed}
            title={collapsed ? 'Expand the sidebar' : 'Collapse the sidebar'}
            aria-label={collapsed ? 'Expand the sidebar' : 'Collapse the sidebar'}
          >
            <CollapseIcon collapsed={collapsed} />
          </button>
        )}
      </div>

      <nav className="sidebar-nav" aria-label="Main">
        {items.map((item) => {
          const active = item.to === '/' ? currentPath === '/' : currentPath.startsWith(item.to)

          return (
            <Link
              key={item.to}
              to={item.to}
              className={`sidebar-link${active ? ' active' : ''}`}
              // The label is the accessible name when it is on screen; collapsed, the title is
              // both the tooltip and what a screen reader reads.
              title={collapsed ? item.label : undefined}
            >
              <NavIcon name={item.icon} />
              <span className="sidebar-label">{item.label}</span>
            </Link>
          )
        })}
      </nav>

      <div className="sidebar-foot">
        {offlineBadge}

        <AccountMenu
          email={user.email}
          theme={theme}
          onToggleTheme={onToggleTheme}
          onSignOut={onSignOut}
          compact={collapsed}
        />
      </div>
    </aside>
  )
}
