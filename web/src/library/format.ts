/** "4h 12m", "38m". Rounded, because this is a glance, not a clock. */
export function formatLength(seconds: number) {
  const minutes = Math.max(1, Math.round(seconds / 60))
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return h > 0 ? (m > 0 ? `${h}h ${m}m` : `${h}h`) : `${m}m`
}
