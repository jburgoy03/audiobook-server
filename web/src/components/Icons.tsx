// Small inline icons, so controls don't depend on how a font draws ▶ or ⏭.
import type { ReactNode } from 'react'


type IconProps = { size?: number }

const svg = (size: number, children: ReactNode) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" focusable="false">
    {children}
  </svg>
)

export const PlayIcon = ({ size = 28 }: IconProps) => svg(size, <path d="M8 5.5v13l10.5-6.5z" />)

export const PauseIcon = ({ size = 28 }: IconProps) =>
  svg(size, <path d="M7 5h3.5v14H7zM13.5 5H17v14h-3.5z" />)

export const PreviousIcon = ({ size = 22 }: IconProps) =>
  svg(size, <path d="M6 5h2.2v14H6zM19 5.5v13L9.5 12z" />)

export const NextIcon = ({ size = 22 }: IconProps) =>
  svg(size, <path d="M15.8 5H18v14h-2.2zM5 5.5v13l9.5-6.5z" />)

/** A circular arrow with the skip amount inside; mirrored for forward. */
export const SkipIcon = ({ size = 30, forward = false }: IconProps & { forward?: boolean }) => (
  <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true" focusable="false">
    <g transform={forward ? 'translate(32 0) scale(-1 1)' : undefined}>
      <path
        d="M16 6a10 10 0 1 1-10 10"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
      />
      <path d="M16 2.5v7l-5-3.5z" fill="currentColor" />
    </g>
    <text x="16" y="19.5" textAnchor="middle" fontSize="9" fontWeight="600" fill="currentColor">
      30
    </text>
  </svg>
)

export const BackIcon = ({ size = 18 }: IconProps) =>
  svg(size, <path d="M14.5 5.5 8 12l6.5 6.5-1.4 1.4L5.2 12l7.9-7.9z" />)
