package dev.deanburgoyne.audiobooks.library

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Checkbox
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.Scaffold
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import dev.deanburgoyne.audiobooks.api.BookSummary
import dev.deanburgoyne.audiobooks.downloads.DownloadState
import dev.deanburgoyne.audiobooks.ui.formatDuration
import dev.deanburgoyne.audiobooks.ui.formatRemaining

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LibraryScreen(
    state: LibraryState,
    coverUrl: (BookSummary) -> String?,
    onRefresh: () -> Unit,
    onOpen: (BookSummary) -> Unit,
    onSignOut: () -> Unit,
    wifiOnly: Boolean,
    onWifiOnlyChange: (Boolean) -> Unit,
) {
    var menu by remember { mutableStateOf(false) }
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Library") },
                actions = {
                    TextButton(onClick = { menu = true }) { Text("Settings") }
                    DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                        DropdownMenuItem(
                            text = { Text("Download on Wi-Fi only") },
                            trailingIcon = { Checkbox(checked = wifiOnly, onCheckedChange = null) },
                            onClick = { onWifiOnlyChange(!wifiOnly) },
                        )
                        DropdownMenuItem(text = { Text("Sign out") }, onClick = { menu = false; onSignOut() })
                    }
                },
            )
        },
    ) { padding ->
        PullToRefreshBox(
            isRefreshing = state.loading,
            onRefresh = onRefresh,
            modifier = Modifier.fillMaxSize().padding(padding),
        ) {
            LazyVerticalGrid(
                // As many ~140dp columns as fit: 2 on a phone, more on a tablet.
                columns = GridCells.Adaptive(minSize = 140.dp),
                contentPadding = PaddingValues(16.dp),
                horizontalArrangement = Arrangement.spacedBy(16.dp),
                verticalArrangement = Arrangement.spacedBy(20.dp),
                modifier = Modifier.fillMaxSize(),
            ) {
                if (state.error != null) {
                    item(span = { GridItemSpan(maxLineSpan) }) {
                        Text(state.error, color = MaterialTheme.colorScheme.error)
                    }
                }

                if (state.continueListening.isNotEmpty()) {
                    item(span = { GridItemSpan(maxLineSpan) }) {
                        ContinueListening(state.continueListening, coverUrl, onOpen)
                    }
                }

                if (state.books.isNotEmpty()) {
                    item(span = { GridItemSpan(maxLineSpan) }) {
                        Text("All books", style = MaterialTheme.typography.titleMedium)
                    }
                }

                items(state.books, key = { it.id }) { book ->
                    BookTile(book, coverUrl(book), downloaded = state.downloads[book.id]?.state == DownloadState.Complete, onClick = { onOpen(book) })
                }

                if (!state.loading && state.error == null && state.books.isEmpty()) {
                    item(span = { GridItemSpan(maxLineSpan) }) {
                        Text(
                            "No books yet. Add a library from the web admin page and scan it.",
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun ContinueListening(
    items: List<InProgress>,
    coverUrl: (BookSummary) -> String?,
    onOpen: (BookSummary) -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Continue listening", style = MaterialTheme.typography.titleMedium)
        // Edge to edge isn't possible inside the grid's padding; this row scrolls within it.
        LazyRow(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            items(items, key = { it.book.id }) { (book, progress) ->
                Column(Modifier.width(150.dp).clickable { onOpen(book) }) {
                    Cover(coverUrl(book), book.title)
                    Spacer(Modifier.height(6.dp))
                    LinearProgressIndicator(
                        progress = { (progress.positionSeconds / book.durationSeconds).toFloat().coerceIn(0f, 1f) },
                        modifier = Modifier.fillMaxWidth(),
                    )
                    Spacer(Modifier.height(4.dp))
                    Text(book.title, style = MaterialTheme.typography.bodyMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(
                        formatRemaining(book.durationSeconds, progress.positionSeconds),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
        }
    }
}

@Composable
private fun BookTile(book: BookSummary, coverUrl: String?, downloaded: Boolean, onClick: () -> Unit) {
    Column(Modifier.clickable(onClick = onClick)) {
        Cover(coverUrl, book.title)
        Spacer(Modifier.height(6.dp))
        Text(book.title, style = MaterialTheme.typography.bodyMedium, maxLines = 2, overflow = TextOverflow.Ellipsis)
        Text(
            listOfNotNull(if (downloaded) "Downloaded" else null, book.author, formatDuration(book.durationSeconds)).joinToString(" · "),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}
