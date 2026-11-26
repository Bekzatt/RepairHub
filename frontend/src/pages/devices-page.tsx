import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { Search } from 'lucide-react'
import { Pager } from '@/components/pager'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useCurrentUser } from '@/features/auth/auth-store'
import { useDevices, type DeviceFilters } from '@/features/devices/api'
import { DEVICE_TYPES } from '@/features/repair-orders/types'
import { formatDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import { t } from '@/locales'
import { ArchiveToggle, Empty, LoadError } from './customers-page'

export function DevicesPage() {
  const navigate = useNavigate()
  const { workshop } = useCurrentUser()
  const [searchInput, setSearchInput] = useState('')
  const [filters, setFilters] = useState<DeviceFilters>({ search: '', type: 'all', includeArchived: false, page: 1 })
  const devices = useDevices(filters)

  useEffect(() => {
    const timer = setTimeout(() => setFilters((f) => (f.search === searchInput ? f : { ...f, search: searchInput, page: 1 })), 300)
    return () => clearTimeout(timer)
  }, [searchInput])

  const data = devices.data

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight sm:text-[28px]">{t.devices.title}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{t.devices.description}</p>
      </div>

      <section className="rounded-xl border bg-card">
        <div className="flex flex-col gap-3 border-b p-4 sm:flex-row sm:items-center">
          <div className="relative flex-1">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder={t.devices.searchPlaceholder}
              aria-label={t.devices.searchPlaceholder}
              className="pl-9"
            />
          </div>
          <Select
            value={filters.type}
            onValueChange={(type) => setFilters((f) => ({ ...f, type: type as DeviceFilters['type'], page: 1 }))}
          >
            <SelectTrigger className="w-full sm:w-48" aria-label={t.devices.columns.type}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{t.devices.allTypes}</SelectItem>
              {DEVICE_TYPES.map((type) => (
                <SelectItem key={type} value={type}>
                  {t.deviceTypes[type]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <ArchiveToggle
            checked={filters.includeArchived}
            onChange={(includeArchived) => setFilters((f) => ({ ...f, includeArchived, page: 1 }))}
          />
        </div>

        {devices.isError ? (
          <LoadError text={t.devices.loadError} onRetry={() => void devices.refetch()} />
        ) : data && data.items.length === 0 ? (
          <Empty text={t.devices.empty} />
        ) : (
          <div className={cn('overflow-x-auto transition-opacity', devices.isFetching && data && 'opacity-60')}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="pl-4">{t.devices.columns.device}</TableHead>
                  <TableHead>{t.devices.columns.owner}</TableHead>
                  <TableHead className="hidden md:table-cell">{t.devices.columns.serial}</TableHead>
                  <TableHead className="hidden pr-4 lg:table-cell">{t.devices.columns.lastRepair}</TableHead>
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
                  : data.items.map((device) => (
                      <TableRow
                        key={device.id}
                        className={cn('cursor-pointer', device.isArchived && 'text-muted-foreground')}
                        onClick={() => navigate(`/devices/${device.id}`)}
                      >
                        <TableCell className="pl-4">
                          <span className="font-medium text-foreground">
                            {device.brand} {device.model}
                          </span>
                          <span className="block text-xs text-muted-foreground">
                            {t.deviceTypes[device.type]}
                            {device.isArchived && ` · ${t.common.archived}`}
                          </span>
                        </TableCell>
                        <TableCell>{device.customerName}</TableCell>
                        <TableCell className="hidden font-mono text-xs md:table-cell">
                          {device.serialNumber ?? device.imei ?? '—'}
                        </TableCell>
                        <TableCell className="hidden pr-4 text-muted-foreground tabular-nums lg:table-cell">
                          {device.lastReceivedAt ? formatDate(device.lastReceivedAt, workshop.timeZone) : '—'}
                        </TableCell>
                      </TableRow>
                    ))}
              </TableBody>
            </Table>
          </div>
        )}
        {data && <Pager data={data} onPage={(page) => setFilters((f) => ({ ...f, page }))} />}
      </section>
    </div>
  )
}
