import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { customersKey } from '@/features/customers/api'
import type { Device, DeviceDetails, DeviceInput } from '@/features/customers/types'
import type { DeviceType, OrderListItem } from '@/features/repair-orders/types'
import { apiRequest } from '@/lib/api-client'
import { PAGE_SIZE, type PagedResult } from '@/lib/paged'

export const devicesKey = ['devices'] as const

export interface DeviceFilters {
  search: string
  type: DeviceType | 'all'
  includeArchived: boolean
  page: number
}

export function useDevices(filters: DeviceFilters) {
  return useQuery({
    queryKey: [...devicesKey, 'list', filters],
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({ page: String(filters.page), pageSize: String(PAGE_SIZE) })
      if (filters.search.trim()) params.set('search', filters.search.trim())
      if (filters.type !== 'all') params.set('type', filters.type)
      if (filters.includeArchived) params.set('includeArchived', 'true')
      return apiRequest<PagedResult<Device>>(`/api/devices?${params}`, { signal })
    },
    placeholderData: keepPreviousData,
  })
}

export function useDevice(id: string | undefined) {
  return useQuery({
    queryKey: [...devicesKey, 'detail', id],
    queryFn: ({ signal }) => apiRequest<DeviceDetails>(`/api/devices/${id}`, { signal }),
    enabled: Boolean(id),
  })
}

export function useDeviceOrders(id: string | undefined, page: number) {
  return useQuery({
    queryKey: [...devicesKey, 'orders', id, page],
    queryFn: ({ signal }) =>
      apiRequest<PagedResult<OrderListItem>>(`/api/devices/${id}/repair-orders?page=${page}&pageSize=${PAGE_SIZE}`, { signal }),
    enabled: Boolean(id),
    placeholderData: keepPreviousData,
  })
}

function useDeviceMutation<TVariables, TResult>(mutationFn: (variables: TVariables) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSettled: async () => {
      await queryClient.invalidateQueries({ queryKey: devicesKey })
      await queryClient.invalidateQueries({ queryKey: customersKey }) // device counts and lists on customer pages
    },
  })
}

export const useCreateDevice = () =>
  useDeviceMutation((input: DeviceInput & { customerId: string }) =>
    apiRequest<Device>('/api/devices', { method: 'POST', body: input }),
  )

export const useUpdateDevice = () =>
  useDeviceMutation(({ id, ...input }: DeviceInput & { id: string }) =>
    apiRequest<Device>(`/api/devices/${id}`, { method: 'PUT', body: input }),
  )

export const useSetDeviceArchived = () =>
  useDeviceMutation(({ id, archived }: { id: string; archived: boolean }) =>
    apiRequest<Device>(`/api/devices/${id}/${archived ? 'archive' : 'restore'}`, { method: 'POST' }),
  )
