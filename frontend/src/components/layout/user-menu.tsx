import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { KeyRound, LogOut } from 'lucide-react'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { logout } from '@/features/auth/auth-api'
import { useCurrentUser } from '@/features/auth/auth-store'
import { ChangePasswordDialog } from '@/features/auth/change-password-dialog'
import { LanguageSwitcher } from '@/components/language-switcher'
import { initials } from '@/lib/format'
import { t } from '@/locales'

export function UserMenu() {
  const user = useCurrentUser()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [passwordOpen, setPasswordOpen] = useState(false)

  async function handleLogout() {
    await logout()
    queryClient.clear() // drop cached data of this user
    navigate('/login', { replace: true })
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger
          className="flex items-center gap-2.5 rounded-full py-1 pr-1 pl-1 text-left outline-none hover:bg-secondary focus-visible:ring-2 focus-visible:ring-ring sm:pr-3"
          aria-label={t.auth.userMenu}
        >
          <span className="flex size-8 items-center justify-center rounded-full bg-sidebar text-xs font-semibold text-white">
            {initials(user.fullName)}
          </span>
          <span className="hidden leading-tight sm:block">
            <span className="block max-w-40 truncate text-sm font-medium">{user.fullName}</span>
            <span className="block text-xs text-muted-foreground">{t.roles[user.role]}</span>
          </span>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-60">
          <DropdownMenuLabel className="font-normal">
            <span className="block truncate text-sm font-medium text-foreground">{user.fullName}</span>
            <span className="block truncate text-xs text-muted-foreground">{user.email}</span>
          </DropdownMenuLabel>
          <DropdownMenuSeparator />
          <div className="flex items-center justify-between gap-2 px-2 py-1.5 text-sm">
            <span className="text-muted-foreground">{t.common.language}</span>
            <LanguageSwitcher />
          </div>
          <DropdownMenuSeparator />
          <DropdownMenuItem onSelect={() => setPasswordOpen(true)}>
            <KeyRound />
            {t.auth.changePassword}
          </DropdownMenuItem>
          <DropdownMenuItem onSelect={() => void handleLogout()}>
            <LogOut />
            {t.auth.logout}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <ChangePasswordDialog open={passwordOpen} onOpenChange={setPasswordOpen} />
    </>
  )
}
