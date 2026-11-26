/** Paged list response of the API (`PagedResult<T>` on the backend). */
export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export const PAGE_SIZE = 20
