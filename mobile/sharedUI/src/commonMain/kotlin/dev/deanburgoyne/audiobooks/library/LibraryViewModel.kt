package dev.deanburgoyne.audiobooks.library

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dev.deanburgoyne.audiobooks.api.ApiException
import dev.deanburgoyne.audiobooks.api.AudiobookApi
import dev.deanburgoyne.audiobooks.api.BookDetail
import dev.deanburgoyne.audiobooks.api.BookSummary
import dev.deanburgoyne.audiobooks.api.Progress
import dev.deanburgoyne.audiobooks.api.SessionExpiredException
import dev.deanburgoyne.audiobooks.downloads.DownloadState
import dev.deanburgoyne.audiobooks.downloads.DownloadedBook
import dev.deanburgoyne.audiobooks.downloads.Downloads
import dev.deanburgoyne.audiobooks.downloads.toSummary
import dev.deanburgoyne.audiobooks.progress.ProgressSync
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** A book the user has started and not finished, with where they are in it. */
data class InProgress(val book: BookSummary, val progress: Progress)

data class LibraryState(
    val loading: Boolean = true,
    val error: String? = null,
    val books: List<BookSummary> = emptyList(),
    /** Most recently listened first, as the server orders progress. */
    val continueListening: List<InProgress> = emptyList(),
    val progressByBook: Map<String, Progress> = emptyMap(),
    /** Downloads by book ID, whatever their state. */
    val downloads: Map<String, DownloadedBook> = emptyMap(),
    /** The server couldn't be reached: [books] are the downloaded ones. */
    val offline: Boolean = false,
)

/**
 * The library as of the last refresh. Online only for now: the offline cache (Room)
 * arrives with downloads, when there's something to read without a connection.
 */
class LibraryViewModel(
    private val api: AudiobookApi,
    private val progress: ProgressSync,
    private val downloads: Downloads,
) : ViewModel() {
    private val _state = MutableStateFlow(LibraryState())
    val state: StateFlow<LibraryState> = _state.asStateFlow()

    init {
        refresh()
        // Follow this device's listening as the server accepts it, without a refresh.
        viewModelScope.launch { progress.saved.collect { applySaved(it) } }
        viewModelScope.launch {
            downloads.books.collect { list -> _state.update { it.copy(downloads = list.associateBy { d -> d.bookId }) } }
        }
    }

    private fun applySaved(saved: Progress) = _state.update { state ->
        val book = state.books.firstOrNull { it.id == saved.bookId } ?: return@update state
        val others = state.continueListening.filter { it.book.id != saved.bookId }
        state.copy(
            progressByBook = state.progressByBook + (saved.bookId to saved),
            // Most recent first: the book just listened to moves to the front.
            continueListening = if (saved.isFinished) others else listOf(InProgress(book, saved)) + others,
        )
    }

    private suspend fun showDownloadedInstead(error: String) {
        val local = offlineBooks(downloads)
        // Where this device last knew each book to be: resuming works offline too.
        val known = withKnownPositions(emptyList()).associateBy { it.bookId }
        _state.update {
            if (local.isEmpty()) it.copy(loading = false, error = error)
            else it.copy(
                loading = false,
                offline = true,
                books = local,
                continueListening = local.mapNotNull { book ->
                    known[book.id]?.takeIf { p -> !p.isFinished }?.let { p -> InProgress(book, p) }
                },
                progressByBook = known,
                error = "Can't reach the server. Showing downloaded books.",
            )
        }
    }

    /**
     * The server's positions with this device's own where it knows better (a report
     * not yet sent), plus books only this device has positions for. The order stays the
     * server's, most recent first; this device's extra books go in front.
     */
    private fun withKnownPositions(server: List<Progress>): List<Progress> {
        val known = progress.knownPositions()
        val fromServer = server.map { p ->
            known[p.bookId]?.let { k -> p.copy(positionSeconds = k.positionSeconds, isFinished = k.isFinished) } ?: p
        }
        val onServer = server.map { it.bookId }.toSet()
        val onlyHere = known.filterKeys { it !in onServer }.map { (bookId, k) ->
            Progress(bookId, k.positionSeconds, reportedAt = "", updatedAt = "", isFinished = k.isFinished)
        }
        return onlyHere + fromServer
    }

    fun refresh() {
        _state.update { it.copy(loading = true, error = null) }
        viewModelScope.launch {
            try {
                // Both at once: they're independent, and the screen needs both. Inside
                // coroutineScope, so a failure comes out of this block as an exception
                // the catch below sees; a bare async in viewModelScope would also report
                // it to the parent job, which crashes the app however await is wrapped.
                val (books, positions) = coroutineScope {
                    val books = async { api.books() }
                    val progress = async { api.progress() }
                    books.await() to progress.await()
                }
                val byId = books.associateBy { it.id }

                downloads.noteServerVersions(books)
                progress.rememberServer(positions)
                val resumable = withKnownPositions(positions)
                _state.value = LibraryState(
                    downloads = _state.value.downloads,
                    loading = false,
                    books = books,
                    continueListening = resumable
                        .filter { !it.isFinished }
                        .mapNotNull { p -> byId[p.bookId]?.let { InProgress(it, p) } },
                    progressByBook = resumable.associateBy { it.bookId },
                )
            } catch (e: CancellationException) {
                throw e
            } catch (_: SessionExpiredException) {
                // The session view model has heard too (sessionEnded) and leaves this screen.
            } catch (e: Exception) {
                showDownloadedInstead(describe(e))
            }
        }
    }
}

/** Offline (or the server failing): the books on the device, if there are any. */
private suspend fun offlineBooks(downloads: Downloads) =
    downloads.dao.allBooks()
        .filter { it.state == DownloadState.Complete }
        .mapNotNull { downloads.detail(it.bookId)?.toSummary() }

sealed interface BookState {
    data object Loading : BookState
    /** [offline]: from the download, because the server couldn't be reached. */
    data class Loaded(val book: BookDetail, val offline: Boolean = false) : BookState
    data object Gone : BookState
    data class Failed(val message: String) : BookState
}

class BookViewModel(
    private val api: AudiobookApi,
    private val bookId: String,
    private val downloads: Downloads,
) : ViewModel() {
    private val _state = MutableStateFlow<BookState>(BookState.Loading)
    val state: StateFlow<BookState> = _state.asStateFlow()

    init { load() }

    fun load() {
        _state.value = BookState.Loading
        viewModelScope.launch {
            _state.value = try {
                api.book(bookId)?.let { BookState.Loaded(it) } ?: BookState.Gone
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                downloads.detail(bookId)?.let { BookState.Loaded(it, offline = true) } ?: BookState.Failed(describe(e))
            }
        }
    }
}

private fun describe(e: Exception) = when (e) {
    is ApiException -> e.message ?: "The server refused that (${e.status})."
    else -> "Couldn't reach the server. Pull down to try again."
}
