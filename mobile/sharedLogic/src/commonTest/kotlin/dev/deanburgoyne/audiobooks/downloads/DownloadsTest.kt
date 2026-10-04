package dev.deanburgoyne.audiobooks.downloads

import dev.deanburgoyne.audiobooks.api.BookDetail
import dev.deanburgoyne.audiobooks.api.BookFile
import dev.deanburgoyne.audiobooks.api.BookSummary
import dev.deanburgoyne.audiobooks.api.Chapter
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** The DAO's behaviour, in memory: what these tests need of Room, not Room itself. */
private class FakeDao : DownloadDao {
    val books = MutableStateFlow<Map<String, DownloadedBook>>(emptyMap())
    val files = mutableMapOf<Pair<String, Int>, DownloadedFile>()

    override fun observeBooks(): Flow<List<DownloadedBook>> = books.map { it.values.toList() }
    override suspend fun allBooks() = books.value.values.toList()
    override suspend fun book(bookId: String) = books.value[bookId]
    override suspend fun files(bookId: String) = files.values.filter { it.bookId == bookId }.sortedBy { it.sequence }
    override suspend fun upsertBook(book: DownloadedBook) { books.value += book.bookId to book }
    override suspend fun upsertFiles(files: List<DownloadedFile>) { files.forEach { this.files[it.bookId to it.sequence] = it } }
    override suspend fun delete(bookId: String) {
        books.value -= bookId
        files.keys.removeAll { it.first == bookId }
    }
    override suspend fun replace(book: DownloadedBook, files: List<DownloadedFile>) {
        delete(book.bookId); upsertBook(book); upsertFiles(files)
    }
    override suspend fun setState(bookId: String, state: DownloadState, error: String?) = update(bookId) { it.copy(state = state, error = error) }
    override suspend fun setDownloadedBytes(bookId: String, bytes: Long) = update(bookId) { it.copy(downloadedBytes = bytes) }
    override suspend fun markFileComplete(bookId: String, sequence: Int) {
        files[bookId to sequence]?.let { files[bookId to sequence] = it.copy(complete = true) }
    }
    override suspend fun setServerContentVersion(bookId: String, version: String?) = update(bookId) { it.copy(serverContentVersion = version) }

    private fun update(bookId: String, change: (DownloadedBook) -> DownloadedBook) {
        books.value[bookId]?.let { books.value += bookId to change(it) }
    }
}

private class FakeFiles : DownloadFiles {
    val present = mutableSetOf<Pair<String, Int>>()
    override fun path(bookId: String, sequence: Int) = "/books/$bookId/$sequence"
    override fun exists(bookId: String, sequence: Int) = (bookId to sequence) in present
    override fun deleteBook(bookId: String) { present.removeAll { it.first == bookId } }
}

private class FakeScheduler : DownloadScheduler {
    val scheduled = mutableListOf<String>()
    val cancelled = mutableListOf<String>()
    override fun schedule(bookId: String) { scheduled += bookId }
    override fun cancel(bookId: String) { cancelled += bookId }
}

private val book = BookDetail(
    id = "b1", title = "Dune", author = "Frank Herbert", durationSeconds = 200.0, hasCover = true,
    files = listOf(BookFile(0, 0.0, 100.0, "audio/mpeg", 1000), BookFile(1, 100.0, 100.0, "audio/mpeg", 2000)),
    chapters = listOf(Chapter(0, "One", 0.0, 200.0)),
    contentVersion = "v1",
)

class DownloadsTest {
    private val dao = FakeDao()
    private val files = FakeFiles()
    private val scheduler = FakeScheduler()
    private val downloads = Downloads(dao, files, scheduler, nowMillis = { 42 })

    private suspend fun completeDownload() {
        downloads.start(book)
        for (seq in 0..1) { dao.markFileComplete("b1", seq); files.present += "b1" to seq }
        dao.setState("b1", DownloadState.Complete)
    }

    @Test
    fun starting_records_the_book_and_schedules_the_transfer() = runTest {
        downloads.start(book)

        val stored = dao.book("b1")!!
        assertEquals(DownloadState.Queued, stored.state)
        assertEquals(3000, stored.totalBytes)
        assertEquals(listOf("b1"), scheduler.scheduled)
        assertEquals(book, downloads.detail("b1"))
    }

    @Test
    fun only_a_complete_download_plays_from_the_device() = runTest {
        downloads.start(book)
        assertNull(downloads.playableFiles("b1"))

        completeDownload()
        assertEquals(mapOf(0 to "/books/b1/0", 1 to "/books/b1/1"), downloads.playableFiles("b1"))
    }

    @Test
    fun a_stale_download_streams_online_but_still_plays_offline() = runTest {
        completeDownload()
        downloads.noteServerVersions(listOf(BookSummary("b1", "Dune", null, 200.0, true, 2, 1, "x", contentVersion = "v2")))

        assertTrue(dao.book("b1")!!.isStale)
        assertNull(downloads.playableFiles("b1"))
        assertEquals(2, downloads.playableFiles("b1", offline = true)?.size)
    }

    @Test
    fun a_file_missing_from_storage_means_not_playable() = runTest {
        completeDownload()
        files.present -= "b1" to 1
        assertNull(downloads.playableFiles("b1"))
    }

    @Test
    fun removing_cancels_and_deletes_everything() = runTest {
        completeDownload()
        downloads.remove("b1")

        assertNull(dao.book("b1"))
        assertTrue(dao.files("b1").isEmpty())
        assertTrue(files.present.isEmpty())
        assertTrue("b1" in scheduler.cancelled)
    }
}
