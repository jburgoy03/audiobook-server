package dev.deanburgoyne.audiobooks.playback

import dev.deanburgoyne.audiobooks.api.BookDetail
import kotlinx.coroutines.flow.StateFlow

/** What's playing, in book terms. Null in [BookPlayer.state] when nothing is loaded. */
data class NowPlaying(
    val bookId: String,
    val title: String,
    val author: String?,
    val durationSeconds: Double,
    val positionSeconds: Double,
    val isPlaying: Boolean,
    val isBuffering: Boolean,
    val chapterTitle: String?,
    val speed: Float,
    val error: String?,
)

/**
 * The player as the UI sees it: one book, one timeline, positions in book seconds.
 * Each platform implements it over its own media stack (Media3 on Android, AVPlayer
 * on iOS), mapping to "file N, T seconds in" with the Timeline functions.
 */
interface BookPlayer {
    val state: StateFlow<NowPlaying?>

    fun play(book: BookDetail, startAt: Double)
    fun togglePlayPause()
    fun seekTo(positionSeconds: Double)
    fun skipBy(seconds: Double)
    fun previousChapter()
    fun nextChapter()
    fun setSpeed(speed: Float)
    fun stop()
}
