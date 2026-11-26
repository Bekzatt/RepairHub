import { Link } from 'react-router'
import { ArrowLeft } from 'lucide-react'
import { Button } from '@/components/ui/button'
import type { NavItem } from '@/config/navigation'
import { ru } from '@/locales/ru'

export function ModulePlaceholderPage({ module }: { module: NavItem }) {
  const Icon = module.icon
  const text = ru.modules[module.key]

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight sm:text-[28px]">{text.title}</h1>

      <section className="rounded-xl border bg-card px-6 py-12 sm:px-10 sm:py-16">
        <div className="mx-auto flex max-w-lg flex-col items-center text-center">
          <div className="mb-5 flex size-14 items-center justify-center rounded-2xl bg-accent text-accent-foreground">
            <Icon className="size-6" strokeWidth={1.75} />
          </div>
          <h2 className="text-lg font-semibold">{ru.placeholder.status}</h2>
          <p className="mt-2 text-sm leading-relaxed text-muted-foreground">{text.description}</p>
          <p className="mt-4 text-sm text-muted-foreground">{ru.placeholder.note}</p>
          <Button asChild variant="outline" className="mt-8">
            <Link to="/">
              <ArrowLeft />
              {ru.placeholder.back}
            </Link>
          </Button>
        </div>
      </section>
    </div>
  )
}
