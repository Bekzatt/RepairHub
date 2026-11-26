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

export type { PagedResult } from '@/lib/paged'

export interface UserFilters {
  search: string
  role: UserRole | 'all'
  status: 'all' | 'active' | 'inactive'
  page: number
}
