const dateFmt = new Intl.DateTimeFormat(undefined, { year: 'numeric', month: 'short', day: 'numeric' })

export const formatDate = (iso: string) => dateFmt.format(new Date(iso))

export function formatRelativeDays(iso: string): string {
  const days = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000)
  if (days <= 0) return 'today'
  if (days === 1) return 'yesterday'
  return `${days} days ago`
}

export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / (1024 * 1024)).toFixed(1)} MB`
}

export const formatDateTime = (iso: string) =>
  new Intl.DateTimeFormat(undefined, { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }).format(
    new Date(iso),
  )

/** Seconds -> "1h 5m 30s". Zero units are dropped ("2h", "45s"). */
export function formatDuration(totalSeconds: number): string {
  const sign = totalSeconds < 0 ? '-' : ''
  let s = Math.round(Math.abs(totalSeconds))
  if (s === 0) return '0s'
  const h = Math.floor(s / 3600)
  s -= h * 3600
  const m = Math.floor(s / 60)
  s -= m * 60
  const parts = [h && `${h}h`, m && `${m}m`, s && `${s}s`].filter(Boolean)
  return sign + parts.join(' ')
}

/** Short form for chart axes: "2.5h", "45m", "30s". */
export function formatDurationCompact(totalSeconds: number): string {
  const a = Math.abs(totalSeconds)
  if (a >= 3600) return `${+(totalSeconds / 3600).toFixed(1)}h`
  if (a >= 60) return `${+(totalSeconds / 60).toFixed(1)}m`
  return `${+totalSeconds.toFixed(0)}s`
}

const numberFmt = new Intl.NumberFormat(undefined, { maximumFractionDigits: 4 })

/** Display formatting driven by the metric's type. null means "undefined for this data". */
export function formatMetricValue(
  type: 'Number' | 'Percentage' | 'Currency' | 'Duration',
  value: number | null,
  currencyCode: string | null,
): string {
  if (value === null) return 'n/a'
  switch (type) {
    case 'Percentage':
      return `${new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(value)}%`
    case 'Currency':
      try {
        return new Intl.NumberFormat(undefined, { style: 'currency', currency: currencyCode ?? 'USD' }).format(value)
      } catch {
        return `${currencyCode ?? ''} ${numberFmt.format(value)}`.trim()
      }
    case 'Duration':
      return formatDuration(value)
    default:
      return numberFmt.format(value)
  }
}

export function formatAxisValue(
  type: 'Number' | 'Percentage' | 'Currency' | 'Duration',
  value: number,
  currencyCode: string | null,
): string {
  if (type === 'Duration') return formatDurationCompact(value)
  if (type === 'Currency')
    try {
      return new Intl.NumberFormat(undefined, { style: 'currency', currency: currencyCode ?? 'USD', notation: 'compact' }).format(value)
    } catch {
      return numberFmt.format(value)
    }
  if (type === 'Percentage') return `${+value.toFixed(1)}%`
  return new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(value)
}
