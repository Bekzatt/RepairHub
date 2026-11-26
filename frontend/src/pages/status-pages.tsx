import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { ru } from '@/locales/ru'

export function NotFoundPage() {
  return (
    <StatusMessage title={ru.notFound.title} text={ru.notFound.text}>
      <Button asChild variant="outline">
        <Link to="/">{ru.placeholder.back}</Link>
      </Button>
    </StatusMessage>
  )
}

/** Shown when rendering a route throws. Details stay in the console, not on screen. */
export function RouteErrorPage() {
  return (
    <div className="flex min-h-dvh items-center justify-center p-6">
      <StatusMessage title={ru.error.title} text={ru.error.text}>
        <Button onClick={() => window.location.reload()}>{ru.error.reload}</Button>
      </StatusMessage>
    </div>
  )
}

function StatusMessage({ title, text, children }: { title: string; text: string; children: ReactNode }) {
  return (
    <div className="mx-auto max-w-md py-16 text-center">
      <h1 className="text-xl font-semibold">{title}</h1>
      <p className="mt-2 text-sm text-muted-foreground">{text}</p>
      <div className="mt-6">{children}</div>
    </div>
  )
}
