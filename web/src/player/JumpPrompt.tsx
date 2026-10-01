import { formatTime } from './timeline'
import type { BookPlayer } from './useBookPlayer'

/**
 * "Further along on Pixel 8, at 14:32:05": shown while the active book is
 * behind where another device left it. Playback never jumps by itself; the
 * listener picks. Either answer makes this device's position the one the server
 * keeps from then on.
 */
export function JumpPrompt({ player }: { player: BookPlayer }) {
  const offer = player.offer
  if (!offer) return null

  return (
    <div className="jump" role="status">
      <p className="jump-text">
        Further along {offer.deviceName ? <>on <em>{offer.deviceName}</em></> : 'on another device'}, at{' '}
        <span className="jump-time">{formatTime(offer.position)}</span>
      </p>
      <div className="jump-actions">
        <button type="button" className="jump-go" onClick={player.acceptOffer}>
          Jump there
        </button>
        <button type="button" className="jump-stay" onClick={player.dismissOffer}>
          Stay here
        </button>
      </div>
    </div>
  )
}
