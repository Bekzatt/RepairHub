import type { DeviceType } from '@/features/repair-orders/types'

export interface Customer {
  id: string
  fullName: string
  phone: string
  additionalPhone: string | null
  email: string | null
  notes: string | null
  isArchived: boolean
  createdAt: string
  updatedAt: string
}

export interface CustomerDetails {
  customer: Customer
  deviceCount: number
  orderCount: number
  openOrderCount: number
}

/** Returned in "matches" of a 409 customer_phone_exists response. */
export interface CustomerMatch {
  id: string
  fullName: string
  phone: string
  additionalPhone: string | null
  isArchived: boolean
}

export interface CustomerInput {
  fullName: string
  phone: string
  additionalPhone: string | null
  email: string | null
  notes: string | null
  allowDuplicatePhone?: boolean
}

export interface Device {
  id: string
  customerId: string
  customerName: string
  type: DeviceType
  brand: string
  model: string
  serialNumber: string | null
  imei: string | null
  description: string | null
  isArchived: boolean
  createdAt: string
  updatedAt: string
  lastReceivedAt: string | null
}

export interface DeviceDetails {
  device: Device
  orderCount: number
  openOrderCount: number
}

export interface DeviceInput {
  type: DeviceType
  brand: string
  model: string
  serialNumber: string | null
  imei: string | null
  description: string | null
}
