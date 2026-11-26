import type { ReactNode } from 'react'
import { RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { useSystemHealth } from '@/features/system/use-system-health'
import { useCurrentUser } from '@/features/auth/auth-store'
import { formatTime } from '@/lib/format'
import { cn } from '@/lib/utils'
import { t } from '@/locales'

export function SystemPanel() {
  const health = useSystemHealth()
  const { workshop } = useCurrentUser()

  return (
    <section aria-labelledby="system-title" className="rounded-xl border bg-card">
      <div className="flex items-center justify-between gap-2 px-5 pt-5 sm:px-6">
        <h2 id="system-title" className="text-[15px] font-semibold">
          {t.dashboard.system.title}
        </h2>
        <Button
          variant="ghost"
          size="sm"
          onClick={() => void health.refresh()}
          disabled={health.isFetching}
          className="-mr-2 text-muted-foreground"
        >
          <RefreshCw className={cn(health.isFetching && 'animate-spin')} />
          {t.dashboard.system.checkAgain}
        </Button>
      </div>

      <dl className="mt-3 divide-y px-5 sm:px-6">
        <Row label={t.dashboard.system.api}>
          <HealthValue up={health.apiUp} downLabel={t.status.offline} />
        </Row>
        <Row label={t.dashboard.system.database}>
          <HealthValue up={health.databaseUp} downLabel={t.status.unavailable} />
        </Row>
        <Row label={t.dashboard.system.timezone}>{workshop.timeZone}</Row>
        <Row label={t.dashboard.system.currency}>{workshop.currency === 'KZT' ? t.dashboard.system.currencyValue : workshop.currency}</Row>
      </dl>

      <p className="px-5 pt-2 pb-5 text-xs text-muted-foreground sm:px-6" aria-live="polite">
        {health.checkedAt
          ? `${t.dashboard.system.lastChecked} ${formatTime(health.checkedAt, workshop.timeZone)}`
          : t.status.checking}
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
  if (up === undefined) return <span className="text-muted-foreground">{t.status.checking}</span>
  return (
    <span className={cn('inline-flex items-center gap-2', up ? 'text-success' : 'text-destructive')}>
      <span aria-hidden className={cn('size-1.5 rounded-full', up ? 'bg-success' : 'bg-destructive')} />
      {up ? t.status.online : downLabel}
    </span>
  )
}
