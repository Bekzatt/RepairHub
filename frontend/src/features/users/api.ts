import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { UserRole } from '@/features/auth/types'
import { apiRequest } from '@/lib/api-client'
import type { PagedResult, User, UserFilters } from './types'

export const PAGE_SIZE = 20

const usersKey = ['users'] as const

export function useUsers(filters: UserFilters) {
  return useQuery({
    queryKey: [...usersKey, filters],
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({ page: String(filters.page), pageSize: String(PAGE_SIZE) })
      if (filters.search.trim()) params.set('search', filters.search.trim())
      if (filters.role !== 'all') params.set('role', filters.role)
      if (filters.status !== 'all') params.set('isActive', String(filters.status === 'active'))
      return apiRequest<PagedResult<User>>(`/api/users?${params}`, { signal })
    },
    placeholderData: keepPreviousData,
  })
}

export interface CreateUserInput {
  fullName: string
  email: string
  phone: string | null
  role: UserRole
  password: string
}

export interface UpdateUserInput {
  fullName: string
  phone: string | null
  role: UserRole
}

function useUsersMutation<TVariables, TResult>(mutationFn: (variables: TVariables) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSettled: () => queryClient.invalidateQueries({ queryKey: usersKey }),
  })
}

export const useCreateUser = () =>
  useUsersMutation((input: CreateUserInput) => apiRequest<User>('/api/users', { method: 'POST', body: input }))

export const useUpdateUser = () =>
  useUsersMutation(({ id, ...input }: UpdateUserInput & { id: string }) =>
    apiRequest<User>(`/api/users/${id}`, { method: 'PUT', body: input }),
  )

export const useSetUserActive = () =>
  useUsersMutation(({ id, active }: { id: string; active: boolean }) =>
    apiRequest<User>(`/api/users/${id}/${active ? 'activate' : 'deactivate'}`, { method: 'POST' }),
  )

export const useResetPassword = () =>
  useUsersMutation(({ id, newPassword }: { id: string; newPassword: string }) =>
    apiRequest<void>(`/api/users/${id}/reset-password`, { method: 'POST', body: { newPassword } }),
  )
