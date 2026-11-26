import { USER_ROLES, type UserRole } from '@/features/auth/types'

/**
 * Client-side mirror of the API's UserPermissions, used only to hide actions that would be refused.
 * The API is the authority and enforces these rules regardless of the UI.
 */
export function canManageRole(actor: UserRole, role: UserRole): boolean {
  if (actor === 'Owner') return true
  if (actor === 'Admin') return role === 'Technician' || role === 'Receptionist'
  return false
}

export function assignableRoles(actor: UserRole): UserRole[] {
  return USER_ROLES.filter((role) => canManageRole(actor, role))
}
