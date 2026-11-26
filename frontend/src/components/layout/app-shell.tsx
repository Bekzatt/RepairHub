import { useState } from 'react'
import { Outlet, ScrollRestoration } from 'react-router'
import { Menu } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetTitle } from '@/components/ui/sheet'
import { useCurrentUser } from '@/features/auth/auth-store'
import { useSystemHealth } from '@/features/system/use-system-health'
import { formatLongDate } from '@/lib/format'
import { t } from '@/locales'
import { SidebarNav } from './sidebar-nav'
import { SystemStatusBadge } from './system-status-badge'
import { UserMenu } from './user-menu'

export function AppShell() {
  const user = useCurrentUser()
  const [menuOpen, setMenuOpen] = useState(false)
  const [today] = useState(() => formatLongDate(new Date(), user.workshop.timeZone))
  const { status } = useSystemHealth()

  return (
    <div className="min-h-dvh">
      {/* Desktop sidebar */}
      <aside className="fixed inset-y-0 left-0 z-30 hidden w-64 lg:block">
        <SidebarNav />
      </aside>

      {/* Mobile drawer */}
      <Sheet open={menuOpen} onOpenChange={setMenuOpen}>
        <SheetContent side="left" className="w-72 border-none p-0 sm:max-w-72" showCloseButton={false}>
          <SheetTitle className="sr-only">{t.nav.menuTitle}</SheetTitle>
          <SheetDescription className="sr-only">{t.app.name}</SheetDescription>
          <SidebarNav onNavigate={() => setMenuOpen(false)} />
        </SheetContent>
      </Sheet>

      <div className="lg:pl-64">
        <header className="sticky top-0 z-20 border-b bg-background/90 backdrop-blur supports-backdrop-filter:bg-background/75">
          <div className="mx-auto flex h-16 max-w-7xl items-center gap-3 px-4 sm:px-6 lg:px-8">
            <Button
              variant="ghost"
              size="icon"
              className="-ml-2 lg:hidden"
              onClick={() => setMenuOpen(true)}
              aria-label={t.nav.openMenu}
            >
              <Menu />
            </Button>
            <p className="truncate text-sm text-muted-foreground">{today}</p>
            <div className="ml-auto flex items-center gap-3">
              <div className="hidden sm:block">
                <SystemStatusBadge status={status} />
              </div>
              <UserMenu />
            </div>
          </div>
        </header>

        <main className="mx-auto max-w-7xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
          <Outlet />
          {/* New pages start at the top; back/forward restores the previous position. */}
          <ScrollRestoration />
        </main>
      </div>
    </div>
  )
}
