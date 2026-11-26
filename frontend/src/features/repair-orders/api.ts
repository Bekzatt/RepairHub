import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { customersKey } from '@/features/customers/api'
import type { CustomerInput, DeviceInput } from '@/features/customers/types'
import { devicesKey } from '@/features/devices/api'
import { apiRequest } from '@/lib/api-client'
import { PAGE_SIZE, type PagedResult } from '@/lib/paged'
import type {
  Assignee,
  DeviceType,
  OrderDetails,
  OrderEvent,
  OrderFilters,
  OrderListItem,
  OrderStatus,
  OrderSummary,
  RepairOutcome,
} from './types'

export const ordersKey = ['orders'] as const
const detailKey = (id: string) => [...ordersKey, 'detail', id] as const
const historyKey = (id: string) => [...ordersKey, 'history', id] as const

/** The API understands "me", "unassigned" or a user id; "all" means no filter. */
function technicianParam(technician: string): string | null {
  if (technician === 'all') return null
  return technician === 'mine' ? 'me' : technician
}

export function useOrders(filters: OrderFilters) {
  return useQuery({
    queryKey: [...ordersKey, 'list', filters],
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({ page: String(filters.page), pageSize: String(PAGE_SIZE) })
      if (filters.search.trim()) params.set('search', filters.search.trim())
      for (const status of filters.statuses) params.append('status', status)
      const technician = technicianParam(filters.technician)
      if (technician) params.set('technician', technician)
      if (filters.overdue) params.set('overdue', 'true')
      return apiRequest<PagedResult<OrderListItem>>(`/api/repair-orders?${params}`, { signal })
    },
    placeholderData: keepPreviousData,
  })
}

export function useOrderSummary(technician: string) {
  const param = technicianParam(technician)
  return useQuery({
    queryKey: [...ordersKey, 'summary', technician],
    queryFn: ({ signal }) =>
      apiRequest<OrderSummary>(`/api/repair-orders/summary${param ? `?technician=${param}` : ''}`, { signal }),
  })
}

export function useOrder(id: string | undefined) {
  return useQuery({
    queryKey: detailKey(id ?? ''),
    queryFn: ({ signal }) => apiRequest<OrderDetails>(`/api/repair-orders/${id}`, { signal }),
    enabled: Boolean(id),
  })
}

export function useOrderHistory(id: string) {
  return useQuery({
    queryKey: historyKey(id),
    queryFn: ({ signal }) => apiRequest<OrderEvent[]>(`/api/repair-orders/${id}/history`, { signal }),
  })
}

export function useAssignees() {
  return useQuery({
    queryKey: [...ordersKey, 'assignees'],
    queryFn: ({ signal }) => apiRequest<Assignee[]>('/api/repair-orders/assignees', { signal }),
    staleTime: 5 * 60_000,
  })
}

export interface CreateOrderInput {
  customerId: string | null
  newCustomer: CustomerInput | null
  deviceId: string | null
  newDevice: DeviceInput | null
  problem: string
  condition: string | null
  accessories: string | null
  assignedTechnicianId: string | null
  expectedCompletionDate: string | null
  estimatedPrice: number | null
}

export function useCreateOrder() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: CreateOrderInput) => apiRequest<OrderDetails>('/api/repair-orders', { method: 'POST', body: input }),
    onSuccess: async (order) => {
      queryClient.setQueryData(detailKey(order.id), order)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: [...ordersKey, 'list'] }),
        queryClient.invalidateQueries({ queryKey: [...ordersKey, 'summary'] }),
        queryClient.invalidateQueries({ queryKey: customersKey }),
        queryClient.invalidateQueries({ queryKey: devicesKey }),
      ])
    },
  })
}

/**
 * Every order change returns the fresh order (with a new version and recomputed allowed transitions):
 * put it in the cache and refresh lists and history. On 409 concurrency_conflict the caller reloads the order.
 */
function useOrderMutation<TVariables>(id: string, request: (variables: TVariables) => Promise<OrderDetails>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: request,
    onSuccess: (order) => queryClient.setQueryData(detailKey(id), order),
    onSettled: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: detailKey(id) }),
        queryClient.invalidateQueries({ queryKey: historyKey(id) }),
        queryClient.invalidateQueries({ queryKey: [...ordersKey, 'list'] }),
        queryClient.invalidateQueries({ queryKey: [...ordersKey, 'summary'] }),
      ])
    },
  })
}

const orderUrl = (id: string, action: string) => `/api/repair-orders/${id}/${action}`

export const useChangeStatus = (id: string) =>
  useOrderMutation(id, (body: { status: OrderStatus; outcome: RepairOutcome | null; comment: string | null; version: number }) =>
    apiRequest<OrderDetails>(orderUrl(id, 'status'), { method: 'POST', body }),
  )

export const useTakeOrder = (id: string) =>
  useOrderMutation(id, () => apiRequest<OrderDetails>(orderUrl(id, 'take'), { method: 'POST' }))

export const useAssignTechnician = (id: string) =>
  useOrderMutation(id, (body: { technicianId: string | null; version: number }) =>
    apiRequest<OrderDetails>(orderUrl(id, 'technician'), { method: 'PUT', body }),
  )

export const useUpdateDiagnostics = (id: string) =>
  useOrderMutation(id, (body: { diagnostics: string | null; version: number }) =>
    apiRequest<OrderDetails>(orderUrl(id, 'diagnostics'), { method: 'PUT', body }),
  )

export const useUpdateIntake = (id: string) =>
  useOrderMutation(id, (body: { problem: string; condition: string | null; accessories: string | null; version: number }) =>
    apiRequest<OrderDetails>(orderUrl(id, 'intake'), { method: 'PUT', body }),
  )

export const useUpdateTerms = (id: string) =>
  useOrderMutation(
    id,
    (body: { estimatedPrice: number | null; finalPrice: number | null; expectedCompletionDate: string | null; version: number }) =>
      apiRequest<OrderDetails>(orderUrl(id, 'terms'), { method: 'PUT', body }),
  )

export interface SnapshotInput {
  customerName: string
  customerPhone: string
  deviceType: DeviceType
  deviceBrand: string
  deviceModel: string
  deviceSerialNumber: string | null
  deviceImei: string | null
}

export const useCorrectSnapshot = (id: string) =>
  useOrderMutation(id, (body: SnapshotInput & { version: number }) =>
    apiRequest<OrderDetails>(orderUrl(id, 'snapshot'), { method: 'PUT', body }),
  )

export function useAddNote(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (text: string) => apiRequest<OrderEvent>(orderUrl(id, 'notes'), { method: 'POST', body: { text } }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: historyKey(id) }),
  })
}
