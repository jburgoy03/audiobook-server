package dev.deanburgoyne.audiobooks.library

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dev.deanburgoyne.audiobooks.api.ApiException
import dev.deanburgoyne.audiobooks.api.AudiobookApi
import dev.deanburgoyne.audiobooks.api.BookDetail
import dev.deanburgoyne.audiobooks.api.BookSummary
import dev.deanburgoyne.audiobooks.api.Progress
import dev.deanburgoyne.audiobooks.api.SessionExpiredException
import dev.deanburgoyne.audiobooks.progress.ProgressSync
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.async
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
)

/**
 * The library as of the last refresh. Online only for now: the offline cache (Room)
 * arrives with downloads, when there's something to read without a connection.
 */
class LibraryViewModel(private val api: AudiobookApi, progress: ProgressSync) : ViewModel() {
    private val _state = MutableStateFlow(LibraryState())
    val state: StateFlow<LibraryState> = _state.asStateFlow()

    init {
        refresh()
        // Follow this device's listening as the server accepts it, without a refresh.
        viewModelScope.launch { progress.saved.collect { applySaved(it) } }
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

    fun refresh() {
        _state.update { it.copy(loading = true, error = null) }
        viewModelScope.launch {
            try {
                // Both at once: they're independent, and the screen needs both.
                val books = async { api.books() }
                val progress = async { api.progress() }
                val byId = books.await().associateBy { it.id }
                val positions = progress.await()

                _state.value = LibraryState(
                    loading = false,
                    books = books.await(),
                    continueListening = positions
                        .filter { !it.isFinished }
                        .mapNotNull { p -> byId[p.bookId]?.let { InProgress(it, p) } },
                    progressByBook = positions.associateBy { it.bookId },
                )
            } catch (e: CancellationException) {
                throw e
            } catch (_: SessionExpiredException) {
                // The session view model has heard too (sessionEnded) and leaves this screen.
            } catch (e: Exception) {
                _state.update { it.copy(loading = false, error = describe(e)) }
            }
        }
    }
}

sealed interface BookState {
    data object Loading : BookState
    data class Loaded(val book: BookDetail) : BookState
    data object Gone : BookState
    data class Failed(val message: String) : BookState
}

class BookViewModel(private val api: AudiobookApi, private val bookId: String) : ViewModel() {
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
                BookState.Failed(describe(e))
            }
        }
    }
}

private fun describe(e: Exception) = when (e) {
    is ApiException -> e.message ?: "The server refused that (${e.status})."
    else -> "Couldn't reach the server. Pull down to try again."
}
