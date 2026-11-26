import { QueuePanel } from '@/features/dashboard/queue-panel'
import { RepairTrack } from '@/features/dashboard/repair-track'
import { SystemPanel } from '@/features/dashboard/system-panel'
import { ru } from '@/locales/ru'

export function DashboardPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight sm:text-[28px]">{ru.dashboard.title}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{ru.modules.dashboard.description}</p>
      </div>

      <RepairTrack />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_380px]">
        <QueuePanel />
        <SystemPanel />
      </div>
    </div>
  )
}
