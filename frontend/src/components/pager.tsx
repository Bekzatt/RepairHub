import { Button } from '@/components/ui/button'
import type { PagedResult } from '@/lib/paged'
import { t } from '@/locales'

/** "21–40 of 57" with previous/next buttons; hidden when everything fits on one page. */
export function Pager({ data, onPage }: { data: PagedResult<unknown>; onPage: (page: number) => void }) {
  if (data.totalCount <= data.pageSize) return null
  const from = (data.page - 1) * data.pageSize + 1
  const to = Math.min(data.page * data.pageSize, data.totalCount)

  return (
    <div className="flex items-center justify-between gap-3 border-t px-4 py-3 text-sm">
      <span className="text-muted-foreground tabular-nums">{t.common.pageInfo(from, to, data.totalCount)}</span>
      <div className="flex gap-2">
        <Button variant="outline" size="sm" disabled={data.page <= 1} onClick={() => onPage(data.page - 1)}>
          {t.common.prev}
        </Button>
        <Button variant="outline" size="sm" disabled={to >= data.totalCount} onClick={() => onPage(data.page + 1)}>
          {t.common.next}
        </Button>
      </div>
    </div>
  )
}
