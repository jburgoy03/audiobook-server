package dev.deanburgoyne.audiobooks.playback

import dev.deanburgoyne.audiobooks.api.BookDetail
import dev.deanburgoyne.audiobooks.api.Chapter
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
    /** The current chapter's span; null when the book has no chapters. */
    val chapterStartSeconds: Double? = null,
    val chapterEndSeconds: Double? = null,
    /** 1-based, for "Chapter 9 of 21"; null without chapters. */
    val chapterNumber: Int? = null,
    val chapterCount: Int = 0,
    /** Sleep timer: seconds until it pauses, or [sleepAtChapterEnd]; both empty when off. */
    val sleepRemainingSeconds: Double? = null,
    val sleepAtChapterEnd: Boolean = false,
    /** The book's chapters, for the segmented timeline. */
    val chapters: List<Chapter> = emptyList(),
)

sealed interface SleepTimer {
    data class After(val minutes: Int) : SleepTimer
    /** Pause where the current chapter ends, so the next one starts cleanly on resume. */
    data object EndOfChapter : SleepTimer
}

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
    /** Null turns it off. */
    fun setSleepTimer(timer: SleepTimer?)
    fun stop()
}
