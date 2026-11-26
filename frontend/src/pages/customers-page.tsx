import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { Plus, Search, Users } from 'lucide-react'
import { Notice, useNotice } from '@/components/notice'
import { Pager } from '@/components/pager'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useCurrentUser } from '@/features/auth/auth-store'
import { useCustomers, type CustomerFilters } from '@/features/customers/api'
import { CustomerFormDialog } from '@/features/customers/customer-form-dialog'
import { formatDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import { t } from '@/locales'

export function CustomersPage() {
  const navigate = useNavigate()
  const { workshop } = useCurrentUser()
  const [searchInput, setSearchInput] = useState('')
  const [filters, setFilters] = useState<CustomerFilters>({ search: '', includeArchived: false, page: 1 })
  const [creating, setCreating] = useState(false)
  const notice = useNotice()
  const customers = useCustomers(filters)

  useEffect(() => {
    const timer = setTimeout(() => setFilters((f) => (f.search === searchInput ? f : { ...f, search: searchInput, page: 1 })), 300)
    return () => clearTimeout(timer)
  }, [searchInput])

  const data = customers.data

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight sm:text-[28px]">{t.customers.title}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t.customers.description}</p>
        </div>
        <Button onClick={() => setCreating(true)} className="self-start sm:self-auto">
          <Plus />
          {t.customers.add}
        </Button>
      </div>

      <Notice flash={notice.flash} error={notice.error} />

      <section className="rounded-xl border bg-card">
        <div className="flex flex-col gap-3 border-b p-4 sm:flex-row sm:items-center">
          <div className="relative flex-1">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder={t.customers.searchPlaceholder}
              aria-label={t.customers.searchPlaceholder}
              className="pl-9"
            />
          </div>
          <ArchiveToggle
            checked={filters.includeArchived}
            onChange={(includeArchived) => setFilters((f) => ({ ...f, includeArchived, page: 1 }))}
          />
        </div>

        {customers.isError ? (
          <LoadError text={t.customers.loadError} onRetry={() => void customers.refetch()} />
        ) : data && data.items.length === 0 ? (
          <Empty text={t.customers.empty} />
        ) : (
          <div className={cn('overflow-x-auto transition-opacity', customers.isFetching && data && 'opacity-60')}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="pl-4">{t.customers.columns.name}</TableHead>
                  <TableHead>{t.customers.columns.phone}</TableHead>
                  <TableHead className="hidden md:table-cell">{t.customers.columns.email}</TableHead>
                  <TableHead className="hidden pr-4 lg:table-cell">{t.customers.columns.created}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {!data
                  ? Array.from({ length: 5 }, (_, i) => (
                      <TableRow key={i}>
                        <TableCell colSpan={4} className="pl-4">
                          <span className="block h-4 animate-pulse rounded bg-muted" />
                        </TableCell>
                      </TableRow>
                    ))
                  : data.items.map((customer) => (
                      <TableRow
                        key={customer.id}
                        className={cn('cursor-pointer', customer.isArchived && 'text-muted-foreground')}
                        onClick={() => navigate(`/customers/${customer.id}`)}
                      >
                        <TableCell className="pl-4">
                          <Link
                            to={`/customers/${customer.id}`}
                            onClick={(e) => e.stopPropagation()}
                            className="font-medium text-foreground hover:underline"
                          >
                            {customer.fullName}
                          </Link>
                          {customer.isArchived && (
                            <span className="ml-2 rounded-full bg-secondary px-2 py-0.5 text-xs">{t.common.archived}</span>
                          )}
                        </TableCell>
                        <TableCell className="tabular-nums">
                          {customer.phone}
                          {customer.additionalPhone && (
                            <span className="block text-xs text-muted-foreground">{customer.additionalPhone}</span>
                          )}
                        </TableCell>
                        <TableCell className="hidden md:table-cell">{customer.email ?? '—'}</TableCell>
                        <TableCell className="hidden pr-4 text-muted-foreground tabular-nums lg:table-cell">
                          {formatDate(customer.createdAt, workshop.timeZone)}
                        </TableCell>
                      </TableRow>
                    ))}
              </TableBody>
            </Table>
          </div>
        )}
        {data && <Pager data={data} onPage={(page) => setFilters((f) => ({ ...f, page }))} />}
      </section>

      <CustomerFormDialog
        customer={null}
        open={creating}
        onOpenChange={setCreating}
        onSaved={(customer) => navigate(`/customers/${customer.id}`, { state: { flash: t.customers.done.created } })}
      />
    </div>
  )
}

export function ArchiveToggle({ checked, onChange }: { checked: boolean; onChange: (checked: boolean) => void }) {
  return (
    <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-muted-foreground select-none">
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        className="size-4 accent-primary"
      />
      {t.common.showArchived}
    </label>
  )
}

export function LoadError({ text, onRetry }: { text: string; onRetry: () => void }) {
  return (
    <div className="px-6 py-14 text-center">
      <p className="text-sm text-muted-foreground">{text}</p>
      <Button variant="outline" className="mt-4" onClick={onRetry}>
        {t.common.retry}
      </Button>
    </div>
  )
}

export function Empty({ text }: { text: string }) {
  return (
    <div className="flex flex-col items-center px-6 py-14 text-center">
      <Users className="mb-3 size-6 text-muted-foreground" strokeWidth={1.75} />
      <p className="text-sm text-muted-foreground">{text}</p>
    </div>
  )
}
