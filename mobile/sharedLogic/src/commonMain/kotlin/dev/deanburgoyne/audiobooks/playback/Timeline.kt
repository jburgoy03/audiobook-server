package dev.deanburgoyne.audiobooks.playback

import dev.deanburgoyne.audiobooks.api.BookFile
import dev.deanburgoyne.audiobooks.api.Chapter
import kotlin.math.floor

// The client-side mirror of the server's BookTimeline, and of the web client's
// player/timeline.ts, rule for rule. A book is one continuous timeline; each file owns
// the half-open interval [start, start + duration), so a position exactly on a
// boundary belongs to the file that starts there. Unlike the server, which answers
// null out of range, a player clamps: a seek past the end lands at the end.
//
// Players work in "file N, T seconds in"; everything else (progress, chapters, the
// slider) works in book seconds. These functions are the only crossing point.

/** A position resolved to a file: [index] into the book's file list, [offsetSeconds] into that file. */
data class FileLocation(val index: Int, val offsetSeconds: Double)

fun List<BookFile>.totalDuration(): Double =
    lastOrNull()?.let { it.startOffsetSeconds + it.durationSeconds } ?: 0.0

fun List<BookFile>.clamp(position: Double): Double =
    if (position.isNaN()) 0.0 else position.coerceIn(0.0, totalDuration())

/** Book seconds → file and offset. */
fun List<BookFile>.locate(position: Double): FileLocation {
    val t = clamp(position)
    for (i in indices.reversed()) {
        val file = this[i]
        if (t >= file.startOffsetSeconds)
            return FileLocation(i, minOf(t - file.startOffsetSeconds, file.durationSeconds))
    }
    return FileLocation(0, 0.0)
}

/** File and offset → book seconds. */
fun List<BookFile>.toBookPosition(index: Int, offsetSeconds: Double): Double {
    val file = getOrNull(index) ?: return 0.0
    return clamp(file.startOffsetSeconds + offsetSeconds.coerceIn(0.0, file.durationSeconds))
}

/** Index of the chapter containing [position], or -1 if there are no chapters. */
fun List<Chapter>.indexAt(position: Double): Int {
    for (i in indices.reversed()) {
        if (position >= this[i].startOffsetSeconds) return i
    }
    return if (isEmpty()) -1 else 0
}

/**
 * Where "previous chapter" goes: the start of the current chapter, unless playback is
 * within the first few seconds of it, in which case the chapter before. The same rule
 * as a CD player's back button, so a double tap goes back two.
 */
fun List<Chapter>.previousTarget(position: Double, graceSeconds: Double = 3.0): Double {
    val i = indexAt(position)
    if (i < 0) return 0.0
    val start = this[i].startOffsetSeconds
    return if (position - start > graceSeconds || i == 0) start else this[i - 1].startOffsetSeconds
}

/** Where "next chapter" goes, or null in the last chapter. */
fun List<Chapter>.nextTarget(position: Double): Double? =
    getOrNull(indexAt(position) + 1)?.startOffsetSeconds

/** "1:02:05" or "2:05": a position or length on a player. */
fun formatClock(seconds: Double): String {
    val s = floor(seconds.coerceAtLeast(0.0)).toLong()
    val h = s / 3600
    val m = (s % 3600) / 60
    val sec = (s % 60).toString().padStart(2, '0')
    return if (h > 0) "$h:${m.toString().padStart(2, '0')}:$sec" else "$m:$sec"
}
