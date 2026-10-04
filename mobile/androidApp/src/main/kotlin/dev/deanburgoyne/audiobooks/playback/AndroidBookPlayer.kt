package dev.deanburgoyne.audiobooks.playback

import android.content.ComponentName
import android.content.Context
import android.net.Uri
import android.os.Bundle
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.Player
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import dev.deanburgoyne.audiobooks.api.AudiobookApi
import dev.deanburgoyne.audiobooks.api.BookDetail
import dev.deanburgoyne.audiobooks.api.Chapter
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlin.math.roundToLong

/**
 * [BookPlayer] over a MediaController connected to [PlaybackService].
 *
 * A book is a playlist with one item per file. Each item carries the book ID, its
 * file's start on the book timeline and the book's length in its metadata extras, so
 * book positions come from the session itself: if the app's process is killed while
 * the service plays on, a new process reconnects and reads where it is without
 * having the book in memory. Chapters aren't in the items (a long book has hundreds);
 * they're fetched by book ID when needed.
 *
 * All calls on the main thread, as MediaController requires.
 */
class AndroidBookPlayer(context: Context, private val api: AudiobookApi) : BookPlayer {
    private val scope = MainScope()
    private val _state = MutableStateFlow<NowPlaying?>(null)
    override val state: StateFlow<NowPlaying?> = _state.asStateFlow()

    private var controller: MediaController? = null
    private val waiting = mutableListOf<(MediaController) -> Unit>()

    private var chapters: List<Chapter> = emptyList()
    private var chaptersFor: String? = null

    private val listener = object : Player.Listener {
        override fun onEvents(player: Player, events: Player.Events) = publish()
    }

    init {
        val token = SessionToken(context, ComponentName(context, PlaybackService::class.java))
        val future = MediaController.Builder(context, token).buildAsync()
        future.addListener({
            val connected = future.get()
            controller = connected
            connected.addListener(listener)
            waiting.forEach { it(connected) }
            waiting.clear()
            publish()
        }, context.mainExecutor)

        // Media3 reports state changes, but not the position ticking along.
        scope.launch {
            while (true) {
                if (controller?.isPlaying == true) publish()
                delay(500)
            }
        }
    }

    override fun play(book: BookDetail, startAt: Double) = withController { c ->
        if (book.id != chaptersFor) {
            chapters = book.chapters
            chaptersFor = book.id
        }
        val cover = if (book.hasCover) api.coverUrl(book.id, size = 1080) else null
        val items = book.files.map { file ->
            MediaItem.Builder()
                .setMediaId("${book.id}/${file.sequence}")
                .setUri(api.streamUrl(book.id, file.sequence))
                .setMediaMetadata(
                    MediaMetadata.Builder()
                        .setTitle(book.title)
                        .setArtist(book.author)
                        .setAlbumTitle(book.title)
                        .setArtworkUri(cover?.let(Uri::parse))
                        .setExtras(Bundle().apply {
                            putString(BookItems.BOOK_ID, book.id)
                            putDouble(BookItems.FILE_START, file.startOffsetSeconds)
                            putDouble(BookItems.BOOK_DURATION, book.durationSeconds)
                        })
                        .build(),
                )
                .build()
        }
        val start = book.files.locate(startAt)
        c.setMediaItems(items, start.index, (start.offsetSeconds * 1000).roundToLong())
        c.prepare()
        c.play()
    }

    override fun togglePlayPause() = withController { c ->
        // After an error the player is idle: prepare again rather than "play" nothing.
        if (c.playbackState == Player.STATE_IDLE) c.prepare()
        // At the end of the book, play means start over (as on the web).
        if (c.playbackState == Player.STATE_ENDED) c.seekTo(0, 0)
        if (c.isPlaying) c.pause() else c.play()
    }

    override fun seekTo(positionSeconds: Double) = withController { c ->
        val starts = (0 until c.mediaItemCount).map { fileStart(c.getMediaItemAt(it)) }
        if (starts.isEmpty()) return@withController
        val duration = bookDuration(c.getMediaItemAt(0))
        val t = positionSeconds.coerceIn(0.0, duration)
        // The last file that starts at or before t (boundaries belong to the next file).
        val index = starts.indexOfLast { it <= t }.coerceAtLeast(0)
        c.seekTo(index, ((t - starts[index]) * 1000).roundToLong())
        publish()
    }

    override fun skipBy(seconds: Double) {
        val now = _state.value ?: return
        seekTo(now.positionSeconds + seconds)
    }

    override fun previousChapter() {
        val now = _state.value ?: return
        seekTo(chapters.previousTarget(now.positionSeconds))
    }

    override fun nextChapter() {
        val now = _state.value ?: return
        chapters.nextTarget(now.positionSeconds)?.let(::seekTo)
    }

    override fun setSpeed(speed: Float) = withController { it.setPlaybackSpeed(speed) }

    override fun stop() = withController { c ->
        c.stop()
        c.clearMediaItems()
    }

    private fun publish() {
        val c = controller ?: return
        val item = c.currentMediaItem
        val bookId = BookItems.bookId(item)
        if (item == null || bookId == null) {
            _state.value = null
            return
        }
        ensureChapters(bookId)

        val position = fileStart(item) + c.currentPosition / 1000.0
        _state.value = NowPlaying(
            bookId = bookId,
            title = item.mediaMetadata.albumTitle?.toString() ?: item.mediaMetadata.title?.toString().orEmpty(),
            author = item.mediaMetadata.artist?.toString(),
            durationSeconds = bookDuration(item),
            positionSeconds = position,
            // "Playing" as the user means it: asked to play, even while it buffers.
            isPlaying = c.playWhenReady && c.playbackState != Player.STATE_ENDED && c.playerError == null,
            isBuffering = c.playbackState == Player.STATE_BUFFERING,
            chapterTitle = chapters.getOrNull(chapters.indexAt(position))?.title,
            speed = c.playbackParameters.speed,
            error = c.playerError?.let { "Playback stopped: ${it.errorCodeName}" },
        )
    }

    /** Reconnected to a book this process didn't start: fetch its chapters once. */
    private fun ensureChapters(bookId: String) {
        if (bookId == chaptersFor) return
        chaptersFor = bookId
        chapters = emptyList()
        scope.launch {
            try {
                api.book(bookId)?.let { if (chaptersFor == bookId) chapters = it.chapters }
                publish()
            } catch (e: CancellationException) {
                throw e
            } catch (_: Exception) {
                chaptersFor = null // try again on the next publish
            }
        }
    }

    private fun withController(action: (MediaController) -> Unit) {
        controller?.let(action) ?: waiting.add(action)
    }

    private fun fileStart(item: MediaItem) = BookItems.fileStart(item)
    private fun bookDuration(item: MediaItem) = BookItems.bookDuration(item)
}
