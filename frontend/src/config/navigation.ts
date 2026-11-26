import {
  BarChart3,
  Bell,
  ClipboardList,
  LayoutGrid,
  Laptop,
  Package,
  Settings,
  ShieldCheck,
  UserCog,
  Users,
  Wallet,
  Receipt,
  type LucideIcon,
} from 'lucide-react'
import type { UserRole } from '@/features/auth/types'
import { ru } from '@/locales/ru'

export type ModuleKey = keyof typeof ru.modules

export interface NavItem {
  key: ModuleKey
  path: string
  icon: LucideIcon
  /** false = placeholder page until the module is implemented. */
  available: boolean
  /** Visible only to these roles (the API enforces the same rule). Omitted = everyone. */
  roles?: readonly UserRole[]
}

export interface NavGroup {
  label: string
  items: NavItem[]
}

export const STAFF_MANAGER_ROLES = ['Owner', 'Admin'] as const satisfies readonly UserRole[]

export const navGroups: NavGroup[] = [
  {
    label: ru.nav.groups.work,
    items: [
      { key: 'dashboard', path: '/', icon: LayoutGrid, available: true },
      { key: 'orders', path: '/orders', icon: ClipboardList, available: false },
      { key: 'customers', path: '/customers', icon: Users, available: false },
      { key: 'devices', path: '/devices', icon: Laptop, available: false },
    ],
  },
  {
    label: ru.nav.groups.stock,
    items: [
      { key: 'inventory', path: '/inventory', icon: Package, available: false },
      { key: 'payments', path: '/payments', icon: Wallet, available: false },
      { key: 'expenses', path: '/expenses', icon: Receipt, available: false },
    ],
  },
  {
    label: ru.nav.groups.service,
    items: [
      { key: 'warranty', path: '/warranty', icon: ShieldCheck, available: false },
      { key: 'notifications', path: '/notifications', icon: Bell, available: false },
    ],
  },
  {
    label: ru.nav.groups.analytics,
    items: [{ key: 'reports', path: '/reports', icon: BarChart3, available: false }],
  },
  {
    label: ru.nav.groups.management,
    items: [{ key: 'staff', path: '/users', icon: UserCog, available: true, roles: STAFF_MANAGER_ROLES }],
  },
]

export const settingsItem: NavItem = {
  key: 'settings',
  path: '/settings',
  icon: Settings,
  available: false,
}

export function isVisibleTo(item: NavItem, role: UserRole): boolean {
  return !item.roles || item.roles.includes(role)
}

/** Every module that currently renders a placeholder page. */
export const placeholderModules: NavItem[] = [...navGroups.flatMap((g) => g.items), settingsItem].filter(
  (item) => !item.available,
)
