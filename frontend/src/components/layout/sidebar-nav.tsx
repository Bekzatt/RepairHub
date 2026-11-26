import { NavLink } from 'react-router'
import { cn } from '@/lib/utils'
import { isVisibleTo, navGroups, settingsItem, type NavItem } from '@/config/navigation'
import { useCurrentUser } from '@/features/auth/auth-store'
import { ru } from '@/locales/ru'
import { BrandMark } from './brand-mark'

interface SidebarNavProps {
  /** Called after a link is chosen (closes the mobile drawer). */
  onNavigate?: () => void
}

export function SidebarNav({ onNavigate }: SidebarNavProps) {
  const user = useCurrentUser()
  const groups = navGroups
    .map((group) => ({ ...group, items: group.items.filter((item) => isVisibleTo(item, user.role)) }))
    .filter((group) => group.items.length > 0)

  return (
    <div className="flex h-full flex-col bg-sidebar text-sidebar-foreground">
      <div className="flex h-16 shrink-0 items-center gap-3 px-5">
        <BrandMark className="size-8 shrink-0 text-sidebar-primary" />
        <div className="min-w-0 leading-tight">
          <div className="text-[15px] font-semibold text-white">{ru.app.name}</div>
          <div className="truncate text-xs text-sidebar-muted" title={user.workshop.name}>
            {user.workshop.name}
          </div>
        </div>
      </div>

      <nav aria-label={ru.nav.menuTitle} className="flex-1 overflow-y-auto px-3 pb-4">
        {groups.map((group) => (
          <section key={group.label} className="mt-5 first:mt-2">
            <h2 className="px-2 pb-1.5 text-xs font-medium text-sidebar-muted">{group.label}</h2>
            <ul className="space-y-0.5">
              {group.items.map((item) => (
                <li key={item.key}>
                  <SidebarLink item={item} onNavigate={onNavigate} />
                </li>
              ))}
            </ul>
          </section>
        ))}
      </nav>

      <div className="border-t border-sidebar-border px-3 py-3">
        <SidebarLink item={settingsItem} onNavigate={onNavigate} />
        <p className="px-2 pt-3 text-xs text-sidebar-muted">{ru.app.version}</p>
      </div>
    </div>
  )
}

function SidebarLink({ item, onNavigate }: { item: NavItem; onNavigate?: () => void }) {
  const Icon = item.icon
  return (
    <NavLink
      to={item.path}
      end={item.path === '/'}
      onClick={onNavigate}
      className={({ isActive }) =>
        cn(
          'group relative flex h-9 items-center gap-3 rounded-md px-2 text-sm transition-colors',
          'hover:bg-sidebar-accent hover:text-sidebar-accent-foreground',
          'focus-visible:outline-sidebar-ring',
          isActive && 'bg-sidebar-accent font-medium text-sidebar-accent-foreground',
        )
      }
    >
      {({ isActive }) => (
        <>
          {isActive && (
            <span aria-hidden className="absolute inset-y-2 -left-3 w-[3px] rounded-r bg-sidebar-primary" />
          )}
          <Icon
            className={cn('size-[18px] shrink-0', isActive ? 'text-sidebar-primary' : 'text-sidebar-muted')}
            strokeWidth={1.75}
          />
          <span className="flex-1 truncate">{ru.modules[item.key].title}</span>
          {!item.available && <span className="text-[11px] text-sidebar-muted">{ru.nav.soon}</span>}
        </>
      )}
    </NavLink>
  )
}
