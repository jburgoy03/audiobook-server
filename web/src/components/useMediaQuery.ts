import { useCallback, useSyncExternalStore } from 'react'

/**
 * Whether a media query matches, following changes (a window resized, a tablet
 * turned). For layout that CSS alone can't do: moving one element to a different
 * place in the tree, rather than rendering it twice and hiding one copy.
 */
export function useMediaQuery(query: string): boolean {
  const subscribe = useCallback(
    (onChange: () => void) => {
      const list = window.matchMedia(query)
      list.addEventListener('change', onChange)
      return () => list.removeEventListener('change', onChange)
    },
    [query],
  )
  return useSyncExternalStore(subscribe, () => window.matchMedia(query).matches)
}
