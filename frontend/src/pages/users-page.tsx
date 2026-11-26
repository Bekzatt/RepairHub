import { useEffect, useState } from 'react'
import { MoreHorizontal, Plus, Search, UserX } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useCurrentUser } from '@/features/auth/auth-store'
import { USER_ROLES, type UserRole } from '@/features/auth/types'
import { PAGE_SIZE, useSetUserActive, useUsers } from '@/features/users/api'
import { canManageRole } from '@/features/users/permissions'
import type { User, UserFilters } from '@/features/users/types'
import { DeactivateDialog, ResetPasswordDialog } from '@/features/users/user-action-dialogs'
import { UserFormDialog } from '@/features/users/user-form-dialog'
import { errorMessage } from '@/lib/form-errors'
import { cn } from '@/lib/utils'
import { t } from '@/locales'

const roleTone: Record<UserRole, string> = {
  Owner: 'bg-sidebar text-white',
  Admin: 'bg-accent text-accent-foreground',
  Technician: 'bg-secondary text-secondary-foreground',
  Receptionist: 'bg-secondary text-secondary-foreground',
}

export function UsersPage() {
  const me = useCurrentUser()
  const [searchInput, setSearchInput] = useState('')
  const [filters, setFilters] = useState<UserFilters>({ search: '', role: 'all', status: 'all', page: 1 })
  const [flash, setFlash] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [editing, setEditing] = useState<{ user: User | null } | null>(null)
  const [resetting, setResetting] = useState<User | null>(null)
  const [deactivating, setDeactivating] = useState<User | null>(null)

  const users = useUsers(filters)
  const setActive = useSetUserActive()

  // Debounce the search box so typing does not fire a request per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => setFilters((f) => (f.search === searchInput ? f : { ...f, search: searchInput, page: 1 })), 300)
    return () => clearTimeout(timer)
  }, [searchInput])

  useEffect(() => {
    if (!flash) return
    const timer = setTimeout(() => setFlash(null), 4000)
    return () => clearTimeout(timer)
  }, [flash])

  function showDone(message: string) {
    setActionError(null)
    setFlash(message)
  }

  async function activate(user: User) {
    try {
      await setActive.mutateAsync({ id: user.id, active: true })
      showDone(t.users.done.activated)
    } catch (error) {
      setActionError(errorMessage(error))
    }
  }

  const data = users.data
  const from = data && data.totalCount > 0 ? (data.page - 1) * PAGE_SIZE + 1 : 0
  const to = data ? Math.min(data.page * PAGE_SIZE, data.totalCount) : 0

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight sm:text-[28px]">{t.users.title}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t.users.description}</p>
        </div>
        <Button onClick={() => setEditing({ user: null })} className="self-start sm:self-auto">
          <Plus />
          {t.users.add}
        </Button>
      </div>

      <div aria-live="polite">
        {flash && (
          <p role="status" className="rounded-md bg-accent px-3 py-2 text-sm text-accent-foreground">
            {flash}
          </p>
        )}
        {actionError && (
          <p role="alert" className="rounded-md border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive">
            {actionError}
          </p>
        )}
      </div>

      <section className="rounded-xl border bg-card">
        {/* Filters */}
        <div className="flex flex-col gap-3 border-b p-4 sm:flex-row sm:items-center">
          <div className="relative flex-1">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder={t.users.searchPlaceholder}
              aria-label={t.users.searchPlaceholder}
              className="pl-9"
            />
          </div>
          <div className="flex gap-3">
            <Select
              value={filters.role}
              onValueChange={(role) => setFilters((f) => ({ ...f, role: role as UserFilters['role'], page: 1 }))}
            >
              <SelectTrigger className="w-full sm:w-44" aria-label={t.users.columns.role}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">{t.users.allRoles}</SelectItem>
                {USER_ROLES.map((role) => (
                  <SelectItem key={role} value={role}>
                    {t.roles[role]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Select
              value={filters.status}
              onValueChange={(status) => setFilters((f) => ({ ...f, status: status as UserFilters['status'], page: 1 }))}
            >
              <SelectTrigger className="w-full sm:w-40" aria-label={t.users.columns.status}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">{t.users.allStatuses}</SelectItem>
                <SelectItem value="active">{t.users.activeFilter}</SelectItem>
                <SelectItem value="inactive">{t.users.inactiveFilter}</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>

        {/* Table */}
        {users.isError ? (
          <div className="px-6 py-14 text-center">
            <p className="text-sm text-muted-foreground">{t.users.loadError}</p>
            <Button variant="outline" className="mt-4" onClick={() => void users.refetch()}>
              {t.users.retry}
            </Button>
          </div>
        ) : (
          <div className={cn('overflow-x-auto transition-opacity', users.isFetching && data && 'opacity-60')}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="pl-4">{t.users.columns.name}</TableHead>
                  <TableHead>{t.users.columns.role}</TableHead>
                  <TableHead className="hidden md:table-cell">{t.users.columns.phone}</TableHead>
                  <TableHead>{t.users.columns.status}</TableHead>
                  <TableHead className="w-12 pr-4">
                    <span className="sr-only">{t.users.columns.actions}</span>
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {!data
                  ? Array.from({ length: 4 }, (_, i) => <SkeletonRow key={i} />)
                  : data.items.map((user) => (
                      <TableRow key={user.id} className={cn(!user.isActive && 'text-muted-foreground')}>
                        <TableCell className="pl-4">
                          <div className="font-medium text-foreground">
                            {user.fullName}
                            {user.id === me.id && (
                              <span className="ml-2 text-xs font-normal text-muted-foreground">({t.users.you})</span>
                            )}
                          </div>
                          <div className="text-xs text-muted-foreground">{user.email}</div>
                        </TableCell>
                        <TableCell>
                          <Badge className={cn('rounded-full border-0 font-medium', roleTone[user.role])}>
                            {t.roles[user.role]}
                          </Badge>
                        </TableCell>
                        <TableCell className="hidden tabular-nums md:table-cell">{user.phone ?? '—'}</TableCell>
                        <TableCell>
                          <span className="inline-flex items-center gap-2 text-sm">
                            <span
                              aria-hidden
                              className={cn('size-1.5 rounded-full', user.isActive ? 'bg-success' : 'bg-muted-foreground/50')}
                            />
                            {user.isActive ? t.users.active : t.users.inactive}
                          </span>
                        </TableCell>
                        <TableCell className="pr-4 text-right">
                          <RowActions
                            user={user}
                            isSelf={user.id === me.id}
                            canManage={canManageRole(me.role, user.role)}
                            onEdit={() => setEditing({ user })}
                            onReset={() => setResetting(user)}
                            onDeactivate={() => setDeactivating(user)}
                            onActivate={() => void activate(user)}
                          />
                        </TableCell>
                      </TableRow>
                    ))}
              </TableBody>
            </Table>

            {data && data.items.length === 0 && (
              <div className="flex flex-col items-center px-6 py-14 text-center">
                <UserX className="mb-3 size-6 text-muted-foreground" strokeWidth={1.75} />
                <p className="text-sm text-muted-foreground">{t.users.empty}</p>
              </div>
            )}
          </div>
        )}

        {/* Pagination */}
        {data && data.totalCount > PAGE_SIZE && (
          <div className="flex items-center justify-between gap-3 border-t px-4 py-3 text-sm">
            <span className="text-muted-foreground tabular-nums">{t.users.pageInfo(from, to, data.totalCount)}</span>
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                disabled={data.page <= 1}
                onClick={() => setFilters((f) => ({ ...f, page: f.page - 1 }))}
              >
                {t.users.prev}
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={to >= data.totalCount}
                onClick={() => setFilters((f) => ({ ...f, page: f.page + 1 }))}
              >
                {t.users.next}
              </Button>
            </div>
          </div>
        )}
      </section>

      <UserFormDialog
        user={editing?.user ?? null}
        open={editing !== null}
        onOpenChange={(open) => !open && setEditing(null)}
        onDone={showDone}
      />
      <ResetPasswordDialog user={resetting} onClose={() => setResetting(null)} onDone={showDone} />
      <DeactivateDialog user={deactivating} onClose={() => setDeactivating(null)} onDone={showDone} />
    </div>
  )
}

interface RowActionsProps {
  user: User
  isSelf: boolean
  canManage: boolean
  onEdit: () => void
  onReset: () => void
  onDeactivate: () => void
  onActivate: () => void
}

function RowActions({ user, isSelf, canManage, onEdit, onReset, onDeactivate, onActivate }: RowActionsProps) {
  // Mirrors the API rules: others need a manageable role; on yourself only the profile can be edited.
  const canEdit = isSelf || canManage
  const canAdminister = !isSelf && canManage
  if (!canEdit) return null

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon-sm" aria-label={`${t.users.rowActions}: ${user.fullName}`}>
          <MoreHorizontal />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuItem onSelect={onEdit}>{t.users.edit}</DropdownMenuItem>
        {canAdminister && (
          <>
            <DropdownMenuItem onSelect={onReset}>{t.users.resetPassword}</DropdownMenuItem>
            <DropdownMenuSeparator />
            {user.isActive ? (
              <DropdownMenuItem variant="destructive" onSelect={onDeactivate}>
                {t.users.deactivate}
              </DropdownMenuItem>
            ) : (
              <DropdownMenuItem onSelect={onActivate}>{t.users.activate}</DropdownMenuItem>
            )}
          </>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function SkeletonRow() {
  return (
    <TableRow>
      {[48, 20, 24, 16, 6].map((width, i) => (
        <TableCell key={i} className={cn(i === 0 && 'pl-4', i === 2 && 'hidden md:table-cell')}>
          <span className="block h-4 animate-pulse rounded bg-muted" style={{ width: `${width * 4}px`, maxWidth: '100%' }} />
        </TableCell>
      ))}
    </TableRow>
  )
}
