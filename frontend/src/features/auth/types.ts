export const USER_ROLES = ['Owner', 'Admin', 'Technician', 'Receptionist'] as const
export type UserRole = (typeof USER_ROLES)[number]

export interface WorkshopSummary {
  id: string
  name: string
  currency: string
  timeZone: string
}

export interface CurrentUser {
  id: string
  fullName: string
  email: string
  phone: string | null
  role: UserRole
  workshop: WorkshopSummary
}

export interface AuthResponse {
  accessToken: string
  accessTokenExpiresAt: string
  user: CurrentUser
}
