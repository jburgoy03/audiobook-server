import type { CSSProperties } from 'react'
import { canSetVolume, useVolume, volumeStore } from '../player/volume'
import { VolumeIcon } from './Icons'

/**
 * Mute and a level slider, for the app-wide volume (player/volume.ts). Lives in
 * the now-playing bar, which shows on every page while a book is active. The
 * store is app-wide, so another placement would just be another instance.
 *
 * The slider is a native range input, so arrow keys and screen readers work as
 * usual. It's hidden where it can't do anything (iPhone Safari, which ignores
 * audio.volume) and on touch screens, where the hardware buttons set the level.
 * Bone, not red: red means position.
 */
export function VolumeControl({ className }: { className?: string }) {
  const { level, muted } = useVolume()
  const silent = muted || level === 0
  const shown = muted ? 0 : level

  return (
    <div className={['volume', className].filter(Boolean).join(' ')}>
      <button
        type="button"
        className="volume-button"
        onClick={volumeStore.toggleMute}
        aria-label="Mute"
        aria-pressed={silent}
      >
        <VolumeIcon level={level} muted={silent} />
      </button>
      {canSetVolume && (
        <input
          className="volume-slider"
          type="range"
          min={0}
          max={1}
          step={0.01}
          value={shown}
          aria-label="Volume"
          aria-valuetext={`${Math.round(shown * 100)}%`}
          style={{ '--fill': `${shown * 100}%` } as CSSProperties}
          onChange={(e) => volumeStore.setLevel(Number(e.target.value))}
        />
      )}
    </div>
  )
}
