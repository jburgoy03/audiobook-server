package dev.deanburgoyne.audiobooks.ui

import kotlin.math.roundToLong

/** "12 h 5 m", "48 m", "under a minute". Book lengths, not timestamps. */
fun formatDuration(seconds: Double): String {
    val minutes = (seconds / 60).roundToLong()
    val h = minutes / 60
    val m = minutes % 60
    return when {
        minutes < 1 -> "under a minute"
        h == 0L -> "$m m"
        m == 0L -> "$h h"
        else -> "$h h $m m"
    }
}

/** "3 h 20 m left", from a position on the book's timeline. */
fun formatRemaining(durationSeconds: Double, positionSeconds: Double): String =
    formatDuration((durationSeconds - positionSeconds).coerceAtLeast(0.0)) + " left"
