package dev.deanburgoyne.audiobooks.library

import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.lifecycle.viewmodel.compose.viewModel
import dev.deanburgoyne.audiobooks.api.AudiobookApi
import dev.deanburgoyne.audiobooks.ui.SystemBackHandler

/**
 * Library → book, and back. One level deep doesn't need a navigation library yet; the
 * open book's ID is saved state, so rotation and process death return to it.
 */
@Composable
fun LibraryHost(api: AudiobookApi, onSignOut: () -> Unit) {
    val library = viewModel { LibraryViewModel(api) }
    val state by library.state.collectAsState()
    var openBookId by rememberSaveable { mutableStateOf<String?>(null) }

    // Thumbnails: 640 for the grid (two or three columns at phone density). The server
    // ignores ?size= until B3 ships, and sends the original.
    val gridCover = { hasCover: Boolean, id: String -> if (hasCover) api.coverUrl(id, size = 640) else null }

    val bookId = openBookId
    if (bookId == null) {
        LibraryScreen(
            state = state,
            coverUrl = { gridCover(it.hasCover, it.id) },
            onRefresh = library::refresh,
            onOpen = { openBookId = it.id },
            onSignOut = onSignOut,
        )
    } else {
        SystemBackHandler { openBookId = null }
        // Keyed by book, so opening another book gets a fresh view model.
        val book = viewModel(key = "book-$bookId") { BookViewModel(api, bookId) }
        val bookState by book.state.collectAsState()
        BookScreen(
            state = bookState,
            progress = state.progressByBook[bookId],
            coverUrl = { if (it.hasCover) api.coverUrl(it.id, size = 1080) else null },
            onBack = { openBookId = null },
            onRetry = book::load,
        )
    }
}
