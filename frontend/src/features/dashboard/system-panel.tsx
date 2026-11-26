import type { ReactNode } from 'react'
import { RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { useSystemHealth } from '@/features/system/use-system-health'
import { SHOP_TIME_ZONE, formatTime } from '@/lib/format'
import { cn } from '@/lib/utils'
import { ru } from '@/locales/ru'

export function SystemPanel() {
  const health = useSystemHealth()

  return (
    <section aria-labelledby="system-title" className="rounded-xl border bg-card">
      <div className="flex items-center justify-between gap-2 px-5 pt-5 sm:px-6">
        <h2 id="system-title" className="text-[15px] font-semibold">
          {ru.dashboard.system.title}
        </h2>
        <Button
          variant="ghost"
          size="sm"
          onClick={() => void health.refresh()}
          disabled={health.isFetching}
          className="-mr-2 text-muted-foreground"
        >
          <RefreshCw className={cn(health.isFetching && 'animate-spin')} />
          {ru.dashboard.system.checkAgain}
        </Button>
      </div>

      <dl className="mt-3 divide-y px-5 sm:px-6">
        <Row label={ru.dashboard.system.api}>
          <HealthValue up={health.apiUp} downLabel={ru.status.offline} />
        </Row>
        <Row label={ru.dashboard.system.database}>
          <HealthValue up={health.databaseUp} downLabel={ru.status.unavailable} />
        </Row>
        <Row label={ru.dashboard.system.timezone}>{SHOP_TIME_ZONE}</Row>
        <Row label={ru.dashboard.system.currency}>{ru.dashboard.system.currencyValue}</Row>
      </dl>

      <p className="px-5 pt-2 pb-5 text-xs text-muted-foreground sm:px-6" aria-live="polite">
        {health.checkedAt
          ? `${ru.dashboard.system.lastChecked} ${formatTime(health.checkedAt)}`
          : ru.status.checking}
      </p>
    </section>
  )
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4 py-3 text-sm">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right font-medium">{children}</dd>
    </div>
  )
}

function HealthValue({ up, downLabel }: { up: boolean | undefined; downLabel: string }) {
  if (up === undefined) return <span className="text-muted-foreground">{ru.status.checking}</span>
  return (
    <span className={cn('inline-flex items-center gap-2', up ? 'text-success' : 'text-destructive')}>
      <span aria-hidden className={cn('size-1.5 rounded-full', up ? 'bg-success' : 'bg-destructive')} />
      {up ? ru.status.online : downLabel}
    </span>
  )
}
