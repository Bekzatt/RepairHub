import type { UserRole } from '@/features/auth/types'

export interface User {
  id: string
  fullName: string
  email: string
  phone: string | null
  role: UserRole
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export interface UserFilters {
  search: string
  role: UserRole | 'all'
  status: 'all' | 'active' | 'inactive'
  page: number
}
