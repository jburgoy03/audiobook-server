package dev.deanburgoyne.audiobooks.playback

import androidx.media3.common.MediaItem
import androidx.media3.common.Player

/**
 * What each playlist item carries in its metadata extras, so the service and the UI
 * can both turn "file N, T seconds in" into a book position without the book itself.
 */
internal object BookItems {
    const val BOOK_ID = "audiobooks.bookId"
    const val FILE_START = "audiobooks.fileStart"
    const val BOOK_DURATION = "audiobooks.bookDuration"

    fun bookId(item: MediaItem?): String? = item?.mediaMetadata?.extras?.getString(BOOK_ID)
    fun fileStart(item: MediaItem): Double = item.mediaMetadata.extras?.getDouble(FILE_START) ?: 0.0
    fun bookDuration(item: MediaItem): Double = item.mediaMetadata.extras?.getDouble(BOOK_DURATION) ?: 0.0
}

/** Where [player] is, in book terms; null when it holds no book. */
internal data class BookPosition(val bookId: String, val positionSeconds: Double, val durationSeconds: Double)

internal fun Player.bookPosition(): BookPosition? {
    val item = currentMediaItem ?: return null
    val bookId = BookItems.bookId(item) ?: return null
    return BookPosition(bookId, BookItems.fileStart(item) + currentPosition / 1000.0, BookItems.bookDuration(item))
}
