package dev.deanburgoyne.audiobooks.playback

import dev.deanburgoyne.audiobooks.api.Chapter
import kotlin.math.abs

/** What the player should do after a [SleepClock.tick]. */
sealed interface SleepAction {
    data object None : SleepAction
    /** A minutes timer is running out: set the output volume to this (1 → 0). */
    data class Fade(val volume: Float) : SleepAction
    /** Pause, put the volume back to full, and park at [positionSeconds]. */
    data class Sleep(val positionSeconds: Double) : SleepAction
}

/**
 * The sleep timer's rules, apart from any media stack, so they're tested here and
 * the iPhone app gets them for free. The same rules as the web client's
 * player/sleepTimer.ts:
 *
 * - Minutes count down **only while audio plays** (the platform calls [tick] while
 *   playing and [interrupt] otherwise). Pausing keeps the time left; with a wall-clock
 *   deadline, resuming after a long pause would sleep again at once.
 * - The last [FadeSeconds] fade the volume out; then it pauses and steps back over
 *   the faded stretch, so resuming replays what was half heard.
 * - End of chapter means whichever chapter playback runs out of: skipping ahead moves
 *   the target with you. It parks exactly on the boundary, so Play starts the next
 *   chapter from its first word. No fade: the chapter's own pause is the fade.
 */
class SleepClock {
    var timer: SleepTimer? = null
        private set

    /** Playback seconds left on a minutes timer; null otherwise. */
    var remainingSeconds: Double? = null
        private set

    private class Sample(val wall: Double, val position: Double)
    private var last: Sample? = null

    /** Null turns it off. */
    fun set(timer: SleepTimer?) {
        this.timer = timer
        remainingSeconds = (timer as? SleepTimer.After)?.let { it.minutes * 60.0 }
        last = null
    }

    /** Playback paused, stalled or jumped: the next tick starts measuring afresh. */
    fun interrupt() {
        last = null
    }

    /**
     * Called regularly while playing. [wallSeconds] is a monotonic clock (immune to the
     * wall clock being changed), [positionSeconds] the book position, [speed] the
     * playback speed.
     */
    fun tick(wallSeconds: Double, positionSeconds: Double, speed: Float, chapters: List<Chapter>): SleepAction {
        val previous = last
        last = Sample(wallSeconds, positionSeconds)
        val timer = timer ?: return SleepAction.None
        val elapsed = if (previous == null) 0.0 else (wallSeconds - previous.wall).coerceAtLeast(0.0)

        return when (timer) {
            is SleepTimer.After -> {
                val remaining = (remainingSeconds ?: 0.0) - elapsed
                if (remaining <= 0.0) {
                    set(null)
                    SleepAction.Sleep((positionSeconds - FadeSeconds * speed).coerceAtLeast(0.0))
                } else {
                    remainingSeconds = remaining
                    if (remaining < FadeSeconds) SleepAction.Fade(fadeLevel(remaining)) else SleepAction.None
                }
            }
            SleepTimer.EndOfChapter -> {
                previous ?: return SleepAction.None
                val boundary = chapters.crossedBoundary(previous.position, positionSeconds, elapsed * speed)
                    ?: return SleepAction.None
                set(null)
                SleepAction.Sleep(boundary)
            }
        }
    }

    companion object {
        const val FadeSeconds = 10.0

        /** Squared, like the web's volume slider, so the fade sounds even. */
        fun fadeLevel(remainingSeconds: Double): Float {
            val x = (remainingSeconds / FadeSeconds).coerceIn(0.0, 1.0)
            return (x * x).toFloat()
        }
    }
}

// How far the position may stray from what the elapsed time predicts and still count
// as playback rather than a seek.
private const val NaturalSlackSeconds = 2.0

/**
 * The chapter start that playback ran across going from [from] to [to], or null: no
 * boundary crossed, or the move was a seek (a skip, a scrub, Next chapter). [expected]
 * is how far playback should have moved: elapsed time × speed.
 */
fun List<Chapter>.crossedBoundary(from: Double, to: Double, expected: Double): Double? {
    if (to <= from || abs(to - from - expected) > NaturalSlackSeconds) return null
    val i = indexAt(from)
    if (i < 0 || indexAt(to) <= i) return null
    return getOrNull(i + 1)?.startOffsetSeconds
}
