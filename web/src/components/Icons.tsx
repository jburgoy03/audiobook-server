// The app's icon set. Drawn on one 24-unit grid with one stroke weight (1.6)
// and round joins, so every control reads as part of the same family. Filled
// shapes (play, pause, chapter skips) carry the same rounded corners as the
// strokes, by drawing them with a matching stroke.
//
// Two icons move, and only in answer to the listener:
// - PlayPauseIcon morphs between its two states (the paths share point counts,
//   so the browser can interpolate `d`; where it can't, it simply swaps).
// - SkipIcon's arc turns a little in its own direction while pressed.
import type { ReactNode } from 'react'

type IconProps = { size?: number; className?: string }

const icon = (size: number, children: ReactNode, className?: string) => (
  <svg
    className={['icon', className].filter(Boolean).join(' ')}
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth={1.6}
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
    focusable="false"
  >
    {children}
  </svg>
)

// Play is a triangle split down the middle; pause is two bars. Each half keeps
// four points in both states, which is what lets one morph into the other.
const PLAY_LEFT = 'M7.5 5.5 L12.75 8.75 L12.75 15.25 L7.5 18.5 Z'
const PLAY_RIGHT = 'M12.75 8.75 L18 12 L18 12 L12.75 15.25 Z'
const PAUSE_LEFT = 'M7 5.5 L10.25 5.5 L10.25 18.5 L7 18.5 Z'
const PAUSE_RIGHT = 'M13.75 5.5 L17 5.5 L17 18.5 L13.75 18.5 Z'

export const PlayPauseIcon = ({ playing, size = 28 }: IconProps & { playing: boolean }) =>
  icon(
    size,
    <g fill="currentColor">
      <path className="pp-half" d={playing ? PAUSE_LEFT : PLAY_LEFT} />
      <path className="pp-half" d={playing ? PAUSE_RIGHT : PLAY_RIGHT} />
    </g>,
    'icon-playpause',
  )

export const PlayIcon = ({ size = 20, className }: IconProps) =>
  icon(size, <path d={`${PLAY_LEFT} ${PLAY_RIGHT}`} fill="currentColor" />, className)

export const PauseIcon = ({ size = 20, className }: IconProps) =>
  icon(size, <path d={`${PAUSE_LEFT} ${PAUSE_RIGHT}`} fill="currentColor" />, className)

export const PreviousIcon = ({ size = 22 }: IconProps) =>
  icon(
    size,
    <>
      <path d="M6.5 6v12" />
      <path d="M17.5 6.5 L9.75 12 L17.5 17.5 Z" fill="currentColor" />
    </>,
  )

export const NextIcon = ({ size = 22 }: IconProps) =>
  icon(
    size,
    <>
      <path d="M17.5 6v12" />
      <path d="M6.5 6.5 L14.25 12 L6.5 17.5 Z" fill="currentColor" />
    </>,
  )

/**
 * An open arc with an arrowhead, and the skip amount set inside it in the
 * book face, so the numerals match the rest of the type. Mirrored for forward.
 */
export const SkipIcon = ({ size = 30, forward = false }: IconProps & { forward?: boolean }) =>
  icon(
    size,
    <>
      <g className={forward ? 'skip-arc skip-arc-forward' : 'skip-arc'}>
        <g transform={forward ? 'translate(24 0) scale(-1 1)' : undefined}>
          <path d="M5.2 8.2 A8 8 0 1 1 4 12.5" />
          <path d="M4.6 4.4 L5.2 8.2 L9 7.6" />
        </g>
      </g>
      <text
        x="12.4"
        y="15.4"
        textAnchor="middle"
        fontSize="8"
        fontWeight="600"
        fill="currentColor"
        stroke="none"
        className="skip-label"
      >
        30
      </text>
    </>,
  )

// A speaker with up to two waves for the level, or a cross when silent. Three
// states, not four: a third wave doesn't fit the 24-unit grid at this stroke.
const SPEAKER = 'M3.5 9.5 H7 L11.5 5.5 V18.5 L7 14.5 H3.5 Z'
export const VolumeIcon = ({ level, muted, size = 22 }: IconProps & { level: number; muted: boolean }) =>
  icon(
    size,
    <>
      <path d={SPEAKER} />
      {muted ? (
        <path d="M16.25 9.75 L20.75 14.25 M20.75 9.75 L16.25 14.25" />
      ) : (
        <>
          {level > 0 && <path d="M14.75 9.5 A3.6 3.6 0 0 1 14.75 14.5" />}
          {level >= 0.5 && <path d="M17.25 7 A7.2 7.2 0 0 1 17.25 17" />}
        </>
      )}
    </>,
  )

export const BackIcon = ({ size = 18 }: IconProps) => icon(size, <path d="M14.5 6 L8.5 12 L14.5 18" />)

export const SearchIcon = ({ size = 20 }: IconProps) =>
  icon(
    size,
    <>
      <circle cx="10.5" cy="10.5" r="5.5" />
      <path d="M14.5 14.5 L19 19" />
    </>,
  )

export const CloseIcon = ({ size = 20 }: IconProps) => icon(size, <path d="M6.5 6.5 L17.5 17.5 M17.5 6.5 L6.5 17.5" />)

/** The library as covers: four rounded squares. */
export const GridIcon = ({ size = 20 }: IconProps) =>
  icon(
    size,
    <>
      <rect x="5" y="5" width="5.5" height="5.5" rx="1" />
      <rect x="13.5" y="5" width="5.5" height="5.5" rx="1" />
      <rect x="5" y="13.5" width="5.5" height="5.5" rx="1" />
      <rect x="13.5" y="13.5" width="5.5" height="5.5" rx="1" />
    </>,
  )

/** The library as rows: a small square and a line, three times. */
export const ListIcon = ({ size = 20 }: IconProps) =>
  icon(
    size,
    <>
      <rect x="4.5" y="5" width="3.5" height="3.5" rx="0.75" />
      <rect x="4.5" y="10.25" width="3.5" height="3.5" rx="0.75" />
      <rect x="4.5" y="15.5" width="3.5" height="3.5" rx="0.75" />
      <path d="M11 6.75 H19.5 M11 12 H19.5 M11 17.25 H19.5" />
    </>,
  )

/**
 * Three small bars that rise and fall while audio plays: shown on the current
 * chapter, so the list says "this is playing", not just "you are here". Still
 * when paused, and for anyone who prefers reduced motion.
 */
export const LevelMeter = ({ active }: { active: boolean }) => (
  <svg
    className={active ? 'level-meter active' : 'level-meter'}
    width="12"
    height="12"
    viewBox="0 0 12 12"
    aria-hidden="true"
    focusable="false"
  >
    <rect x="1" y="4" width="2" height="8" rx="1" />
    <rect x="5" y="1" width="2" height="11" rx="1" />
    <rect x="9" y="6" width="2" height="6" rx="1" />
  </svg>
)
