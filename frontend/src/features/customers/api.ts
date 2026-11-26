import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { OrderListItem } from '@/features/repair-orders/types'
import { ApiError, apiRequest } from '@/lib/api-client'
import { PAGE_SIZE, type PagedResult } from '@/lib/paged'
import type { Customer, CustomerDetails, CustomerInput, CustomerMatch, Device } from './types'

export const customersKey = ['customers'] as const

export interface CustomerFilters {
  search: string
  includeArchived: boolean
  page: number
}

export function useCustomers(filters: CustomerFilters) {
  return useQuery({
    queryKey: [...customersKey, 'list', filters],
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({ page: String(filters.page), pageSize: String(PAGE_SIZE) })
      if (filters.search.trim()) params.set('search', filters.search.trim())
      if (filters.includeArchived) params.set('includeArchived', 'true')
      return apiRequest<PagedResult<Customer>>(`/api/customers?${params}`, { signal })
    },
    placeholderData: keepPreviousData,
  })
}

/** Quick lookup while typing (order creation): name or phone fragment, active customers only. */
export function useCustomerLookup(term: string) {
  const search = term.trim()
  return useQuery({
    queryKey: [...customersKey, 'lookup', search],
    queryFn: ({ signal }) =>
      apiRequest<PagedResult<Customer>>(`/api/customers?${new URLSearchParams({ search, pageSize: '8' })}`, { signal }),
    enabled: search.length >= 2,
    placeholderData: keepPreviousData,
  })
}

export function useCustomer(id: string | undefined) {
  return useQuery({
    queryKey: [...customersKey, 'detail', id],
    queryFn: ({ signal }) => apiRequest<CustomerDetails>(`/api/customers/${id}`, { signal }),
    enabled: Boolean(id),
  })
}

export function useCustomerDevices(id: string | undefined) {
  return useQuery({
    queryKey: [...customersKey, 'devices', id],
    queryFn: ({ signal }) => apiRequest<Device[]>(`/api/customers/${id}/devices`, { signal }),
    enabled: Boolean(id),
  })
}

export function useCustomerOrders(id: string | undefined, page: number) {
  return useQuery({
    queryKey: [...customersKey, 'orders', id, page],
    queryFn: ({ signal }) =>
      apiRequest<PagedResult<OrderListItem>>(`/api/customers/${id}/repair-orders?page=${page}&pageSize=${PAGE_SIZE}`, { signal }),
    enabled: Boolean(id),
    placeholderData: keepPreviousData,
  })
}

function useCustomerMutation<TVariables, TResult>(mutationFn: (variables: TVariables) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSettled: () => queryClient.invalidateQueries({ queryKey: customersKey }),
  })
}

export const useCreateCustomer = () =>
  useCustomerMutation((input: CustomerInput) => apiRequest<Customer>('/api/customers', { method: 'POST', body: input }))

export const useUpdateCustomer = () =>
  useCustomerMutation(({ id, ...input }: CustomerInput & { id: string }) =>
    apiRequest<Customer>(`/api/customers/${id}`, { method: 'PUT', body: input }),
  )

export const useSetCustomerArchived = () =>
  useCustomerMutation(({ id, archived }: { id: string; archived: boolean }) =>
    apiRequest<Customer>(`/api/customers/${id}/${archived ? 'archive' : 'restore'}`, { method: 'POST' }),
  )

/** The existing customers with the same phone, when the API refused a duplicate (409 customer_phone_exists). */
export function phoneMatches(error: unknown): CustomerMatch[] | null {
  if (error instanceof ApiError && error.code === 'customer_phone_exists' && Array.isArray(error.body.matches)) {
    return error.body.matches as CustomerMatch[]
  }
  return null
}

/** Customers (archived included) whose phone matches, for the duplicate hint while a new customer is typed. */
export function usePhoneMatches(phone: string, enabled: boolean) {
  const value = phone.trim()
  return useQuery({
    queryKey: [...customersKey, 'phone', value],
    queryFn: ({ signal }) =>
      apiRequest<PagedResult<Customer>>(
        `/api/customers?${new URLSearchParams({ phone: value, includeArchived: 'true', pageSize: '5' })}`,
        { signal },
      ),
    enabled: enabled && value.replace(/\D/g, '').length >= 7,
    staleTime: 30_000,
  })
}
