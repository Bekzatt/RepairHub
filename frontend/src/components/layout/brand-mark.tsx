/** RepairHub mark: a cross-slot screw head — the most common thing on a repair bench. */
export function BrandMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" aria-hidden="true" className={className}>
      <rect width="32" height="32" rx="8" fill="currentColor" />
      <circle cx="16" cy="16" r="9.5" fill="none" stroke="var(--sidebar)" strokeWidth="2.5" />
      <path d="M16 10.5v11M10.5 16h11" stroke="var(--sidebar)" strokeWidth="2.5" strokeLinecap="round" />
    </svg>
  )
}
