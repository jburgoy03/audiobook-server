package dev.deanburgoyne.audiobooks.library

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import dev.deanburgoyne.audiobooks.api.BookDetail
import dev.deanburgoyne.audiobooks.api.Progress
import dev.deanburgoyne.audiobooks.ui.formatDuration
import dev.deanburgoyne.audiobooks.ui.formatRemaining

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BookScreen(
    state: BookState,
    progress: Progress?,
    coverUrl: (BookDetail) -> String?,
    onBack: () -> Unit,
    onRetry: () -> Unit,
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = {},
                navigationIcon = { TextButton(onClick = onBack) { Text("Back") } },
            )
        },
    ) { padding ->
        Box(Modifier.fillMaxSize().padding(padding)) {
            when (state) {
                BookState.Loading -> CircularProgressIndicator(Modifier.align(Alignment.Center))
                BookState.Gone -> Message("This book is no longer in the library.")
                is BookState.Failed -> Column(Modifier.align(Alignment.Center).padding(24.dp)) {
                    Text(state.message, color = MaterialTheme.colorScheme.error)
                    TextButton(onClick = onRetry) { Text("Try again") }
                }
                is BookState.Loaded -> BookDetails(state.book, progress, coverUrl(state.book))
            }
        }
    }
}

@Composable
private fun BookDetails(book: BookDetail, progress: Progress?, coverUrl: String?) {
    LazyColumn(
        contentPadding = PaddingValues(horizontal = 24.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        modifier = Modifier.fillMaxSize(),
    ) {
        item { Cover(coverUrl, book.title, Modifier.widthIn(max = 280.dp).fillMaxWidth()) }

        item {
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(book.title, style = MaterialTheme.typography.headlineSmall)
                book.subtitle?.let { Text(it, style = MaterialTheme.typography.titleMedium) }
                book.author?.let { Text(it, style = MaterialTheme.typography.bodyLarge) }
                book.narrator?.let {
                    Text("Read by $it", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Text(
                    listOfNotNull(
                        formatDuration(book.durationSeconds),
                        progress?.let {
                            if (it.isFinished) "finished" else formatRemaining(book.durationSeconds, it.positionSeconds)
                        },
                        book.publishedYear?.toString(),
                    ).joinToString(" · "),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }

        item {
            // Playback is A3; the button is here so the layout is settled.
            Button(onClick = {}, enabled = false, modifier = Modifier.fillMaxWidth()) {
                Text(if (progress != null && !progress.isFinished) "Resume" else "Play")
            }
        }

        book.description?.let { description ->
            item {
                Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    // Six lines, then "More": the chapters are what's usually wanted, and
                    // a full blurb can push them a screen away.
                    var expanded by rememberSaveable { mutableStateOf(false) }
                    var overflows by remember { mutableStateOf(false) }
                    Text(
                        description,
                        style = MaterialTheme.typography.bodyMedium,
                        maxLines = if (expanded) Int.MAX_VALUE else 6,
                        overflow = TextOverflow.Ellipsis,
                        onTextLayout = { if (!expanded) overflows = it.hasVisualOverflow },
                    )
                    if (overflows || expanded) {
                        TextButton(onClick = { expanded = !expanded }) { Text(if (expanded) "Less" else "More") }
                    }
                    book.credit?.let {
                        Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
        }

        if (book.chapters.size > 1) {
            item { Text("Chapters", style = MaterialTheme.typography.titleMedium, modifier = Modifier.fillMaxWidth()) }
            items(book.chapters, key = { it.sequence }) { chapter ->
                Column(Modifier.fillMaxWidth()) {
                    Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), horizontalArrangement = Arrangement.SpaceBetween) {
                        Text(chapter.title, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
                        Text(
                            formatDuration(chapter.endOffsetSeconds - chapter.startOffsetSeconds),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    HorizontalDivider()
                }
            }
        }
    }
}

@Composable
private fun Message(text: String) {
    Box(Modifier.fillMaxSize().padding(24.dp), contentAlignment = Alignment.Center) {
        Text(text, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}
