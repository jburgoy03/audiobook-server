package dev.deanburgoyne.audiobooks.downloads

import dev.deanburgoyne.audiobooks.api.ApiJson
import dev.deanburgoyne.audiobooks.api.BookDetail
import dev.deanburgoyne.audiobooks.api.BookSummary
import kotlinx.coroutines.flow.Flow

/** Where downloaded files live on this platform. */
interface DownloadFiles {
    /** Absolute path of one file of a book. */
    fun path(bookId: String, sequence: Int): String
    fun exists(bookId: String, sequence: Int): Boolean
    fun deleteBook(bookId: String)
}

/** Runs and cancels the background transfer (WorkManager on Android). */
interface DownloadScheduler {
    fun schedule(bookId: String)
    fun cancel(bookId: String)
}

/**
 * Books kept on the device: starting and removing them, and what playback and the
 * offline screens read back. The transfer itself is the platform's (see
 * DownloadWorker on Android); it records progress through [dao].
 */
class Downloads(
    val dao: DownloadDao,
    private val files: DownloadFiles,
    private val scheduler: DownloadScheduler,
    private val nowMillis: () -> Long,
) {
    val books: Flow<List<DownloadedBook>> = dao.observeBooks()

    /** Downloads [book] from scratch, replacing any earlier copy. */
    suspend fun start(book: BookDetail) {
        scheduler.cancel(book.id)
        files.deleteBook(book.id)
        dao.replace(
            DownloadedBook(
                bookId = book.id,
                title = book.title,
                author = book.author,
                detailJson = ApiJson.encodeToString(BookDetail.serializer(), book),
                contentVersion = book.contentVersion,
                serverContentVersion = book.contentVersion,
                totalBytes = book.files.sumOf { it.sizeBytes },
                downloadedBytes = 0,
                state = DownloadState.Queued,
                error = null,
                addedAtMillis = nowMillis(),
            ),
            book.files.map { DownloadedFile(book.id, it.sequence, it.sizeBytes, complete = false) },
        )
        scheduler.schedule(book.id)
    }

    /** A failed download picks up where it stopped (files already complete are kept). */
    suspend fun retry(bookId: String) {
        dao.setState(bookId, DownloadState.Queued)
        scheduler.schedule(bookId)
    }

    suspend fun remove(bookId: String) {
        scheduler.cancel(bookId)
        dao.delete(bookId)
        files.deleteBook(bookId)
    }

    /** The book's details as downloaded, for the book page without a connection. */
    suspend fun detail(bookId: String): BookDetail? =
        dao.book(bookId)?.let { ApiJson.decodeFromString(BookDetail.serializer(), it.detailJson) }

    /**
     * Local paths by file sequence, when the whole book is here and still matches
     * the server. Otherwise null, and the player streams: a stale copy could put
     * positions on a different timeline from every other device's.
     *
     * [offline]: no server to stream from, so a stale copy is better than nothing.
     */
    suspend fun playableFiles(bookId: String, offline: Boolean = false): Map<Int, String>? {
        val book = dao.book(bookId) ?: return null
        if (book.state != DownloadState.Complete) return null
        if (book.isStale && !offline) return null

        val rows = dao.files(bookId)
        // Belt and braces: a file deleted behind the app's back (storage cleared)
        // means the download isn't really complete.
        if (rows.any { !it.complete || !files.exists(bookId, it.sequence) }) return null
        return rows.associate { it.sequence to files.path(bookId, it.sequence) }
    }

    /** The library loaded: note each downloaded book's current server version. */
    suspend fun noteServerVersions(library: List<BookSummary>) {
        val byId = library.associateBy { it.id }
        for (book in dao.allBooks()) {
            val server = byId[book.bookId] ?: continue
            if (server.contentVersion != book.serverContentVersion)
                dao.setServerContentVersion(book.bookId, server.contentVersion)
        }
    }
}

/** The one download setting, kept by the platform. */
interface DownloadSettings {
    /** Wait for an unmetered network (Wi-Fi) before downloading. Applies to downloads started after a change. */
    var wifiOnly: Boolean
}

/** A downloaded book as a library row, for the offline library. */
fun BookDetail.toSummary(): BookSummary = BookSummary(
    id = id, title = title, author = author, durationSeconds = durationSeconds, hasCover = hasCover,
    files = files.size, chapters = chapters.size, addedAt = "", contentVersion = contentVersion,
)
