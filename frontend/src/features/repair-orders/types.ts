import type { UserRole } from '@/features/auth/types'

export const DEVICE_TYPES = ['Laptop', 'Desktop', 'AllInOne', 'Printer', 'Smartphone', 'Tablet', 'Monitor', 'Other'] as const
export type DeviceType = (typeof DEVICE_TYPES)[number]

export const ORDER_STATUSES = [
  'Received',
  'Diagnostics',
  'AwaitingApproval',
  'WaitingForParts',
  'InRepair',
  'ReadyForPickup',
  'Issued',
  'Cancelled',
] as const
export type OrderStatus = (typeof ORDER_STATUSES)[number]

/** Display grouping only (filter chips); the workflow rules live on the server. */
export const OPEN_STATUSES: readonly OrderStatus[] = ORDER_STATUSES.filter((s) => s !== 'Issued' && s !== 'Cancelled')

export const OUTCOMES = ['Repaired', 'NotRepaired', 'Declined'] as const
export type RepairOutcome = (typeof OUTCOMES)[number]

export type OrderEventType =
  | 'Created'
  | 'StatusChanged'
  | 'OutcomeRecorded'
  | 'TechnicianAssigned'
  | 'DiagnosticsUpdated'
  | 'ProblemUpdated'
  | 'ConditionUpdated'
  | 'AccessoriesUpdated'
  | 'EstimateChanged'
  | 'FinalPriceChanged'
  | 'ExpectedDateChanged'
  | 'CustomerInfoCorrected'
  | 'DeviceInfoCorrected'
  | 'NoteAdded'

export interface UserRef {
  id: string
  fullName: string
}

export interface Assignee extends UserRef {
  role: UserRole
}

export interface OrderListItem {
  id: string
  number: string
  status: OrderStatus
  outcome: RepairOutcome | null
  customerId: string
  customerName: string
  customerPhone: string
  deviceId: string
  deviceType: DeviceType
  deviceBrand: string
  deviceModel: string
  problem: string
  assignedTechnician: UserRef | null
  receivedAt: string
  /** Calendar date "YYYY-MM-DD" in the workshop time zone. */
  expectedCompletionDate: string | null
  isOverdue: boolean
  estimatedPrice: number | null
  finalPrice: number | null
  currency: string
  updatedAt: string
}

export interface AvailableTransition {
  to: OrderStatus
  requiresOutcome: boolean
  outcomes: RepairOutcome[]
  requiresReason: boolean
}

export interface OrderPermissions {
  editIntake: boolean
  editDiagnostics: boolean
  editTerms: boolean
  correctSnapshot: boolean
  assign: boolean
  take: boolean
  addNote: boolean
}

export interface OrderDetails {
  id: string
  number: string
  status: OrderStatus
  outcome: RepairOutcome | null
  customerId: string
  customerName: string
  customerPhone: string
  deviceId: string
  deviceType: DeviceType
  deviceBrand: string
  deviceModel: string
  deviceSerialNumber: string | null
  deviceImei: string | null
  problem: string
  condition: string | null
  accessories: string | null
  diagnostics: string | null
  estimatedPrice: number | null
  finalPrice: number | null
  currency: string
  assignedTechnician: UserRef | null
  createdBy: UserRef
  updatedBy: UserRef
  receivedAt: string
  expectedCompletionDate: string | null
  isOverdue: boolean
  startedAt: string | null
  completedAt: string | null
  issuedAt: string | null
  cancelledAt: string | null
  cancellationReason: string | null
  createdAt: string
  updatedAt: string
  version: number
  /** Computed by the server for the current user. */
  allowedTransitions: AvailableTransition[]
  can: OrderPermissions
}

export interface OrderEvent {
  id: string
  type: OrderEventType
  oldValue: string | null
  newValue: string | null
  comment: string | null
  actor: UserRef
  occurredAt: string
}

export interface OrderSummary {
  byStatus: { status: OrderStatus; count: number }[]
  overdue: number
  unassigned: number
}

/** "mine" | "unassigned" | "all" | a user id */
export type TechnicianFilter = string

export interface OrderFilters {
  search: string
  statuses: OrderStatus[]
  technician: TechnicianFilter
  overdue: boolean
  page: number
}
