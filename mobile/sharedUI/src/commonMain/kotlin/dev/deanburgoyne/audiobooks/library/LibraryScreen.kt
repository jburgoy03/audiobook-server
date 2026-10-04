package dev.deanburgoyne.audiobooks.library

import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CheckboxDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.scale
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.animation.core.animateFloatAsState
import dev.deanburgoyne.audiobooks.api.BookSummary
import dev.deanburgoyne.audiobooks.downloads.DownloadState
import dev.deanburgoyne.audiobooks.ui.Masthead
import dev.deanburgoyne.audiobooks.ui.Palette
import dev.deanburgoyne.audiobooks.ui.PillButton
import dev.deanburgoyne.audiobooks.ui.PlayerIcons
import dev.deanburgoyne.audiobooks.ui.SectionHead
import dev.deanburgoyne.audiobooks.ui.Timeline
import dev.deanburgoyne.audiobooks.ui.formatDuration
import dev.deanburgoyne.audiobooks.ui.formatRemaining
import dev.deanburgoyne.audiobooks.ui.secondaryItalic
import kotlin.math.roundToInt

private val Gutter = 20.dp
private val ColGap = 16.dp
private val RowGap = 32.dp
private val SectionGap = 56.dp

/**
 * The web's library at phone width: two equal tracks. "Continue listening" leads
 * with the most recent book featured (cover and title side by side, its timeline
 * across the full width, then Resume), the other started books beside their covers;
 * "All books" is the grid of covers below.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LibraryScreen(
    state: LibraryState,
    coverUrl: (BookSummary) -> String?,
    onRefresh: () -> Unit,
    onOpen: (BookSummary) -> Unit,
    onResume: (BookSummary) -> Unit,
    onSignOut: () -> Unit,
    wifiOnly: Boolean,
    onWifiOnlyChange: (Boolean) -> Unit,
) {
    Column(Modifier.fillMaxSize()) {
    // Pinned: the wordmark and its ribbon stay put while the shelves scroll.
    Masthead(Modifier.statusBarsPadding()) { SettingsMenu(wifiOnly, onWifiOnlyChange, onSignOut) }
    PullToRefreshBox(isRefreshing = state.loading, onRefresh = onRefresh, modifier = Modifier.fillMaxSize()) {
        LazyVerticalGrid(
            columns = GridCells.Fixed(2),
            contentPadding = PaddingValues(start = Gutter, end = Gutter, top = 28.dp, bottom = 40.dp),
            horizontalArrangement = Arrangement.spacedBy(ColGap),
            verticalArrangement = Arrangement.spacedBy(RowGap),
            modifier = Modifier.fillMaxSize(),
        ) {
            if (state.error != null) {
                item(span = { GridItemSpan(maxLineSpan) }) {
                    Text(state.error, style = MaterialTheme.typography.bodyLarge.copy(fontStyle = FontStyle.Italic))
                }
            }

            val continuing = state.continueListening
            if (continuing.isNotEmpty()) {
                item(span = { GridItemSpan(maxLineSpan) }) { SectionHead("Continue listening") }
                item(span = { GridItemSpan(maxLineSpan) }) {
                    Featured(continuing.first(), coverUrl, onOpen, onResume)
                }
                items(continuing.drop(1), key = { "continue-${it.book.id}" }, span = { GridItemSpan(maxLineSpan) }) { item ->
                    ContinueRow(item, coverUrl(item.book), onOpen = { onOpen(item.book) })
                }
                item(span = { GridItemSpan(maxLineSpan) }) { Spacer(Modifier.height(SectionGap - RowGap)) }
            }

            if (state.books.isNotEmpty()) {
                item(span = { GridItemSpan(maxLineSpan) }) {
                    SectionHead("All books", count = state.books.size.toString())
                }
                items(state.books, key = { it.id }) { book ->
                    val progress = state.progressByBook[book.id]
                    BookTile(
                        book = book,
                        coverUrl = coverUrl(book),
                        progress = progress?.takeIf { !it.isFinished }?.let { (it.positionSeconds / book.durationSeconds).toFloat() },
                        downloaded = state.downloads[book.id]?.state == DownloadState.Complete,
                        onClick = { onOpen(book) },
                    )
                }
            }

            if (!state.loading && state.error == null && state.books.isEmpty()) {
                item(span = { GridItemSpan(maxLineSpan) }) {
                    Text(
                        "No books yet. Add a library from the web's admin page and scan it.",
                        style = MaterialTheme.typography.bodyLarge,
                        modifier = Modifier.padding(top = 32.dp),
                    )
                }
            }
        }
    }
    }
}

@Composable
private fun SettingsMenu(wifiOnly: Boolean, onWifiOnlyChange: (Boolean) -> Unit, onSignOut: () -> Unit) {
    var open by remember { mutableStateOf(false) }
    Box {
        Text("Settings", style = secondaryItalic, modifier = Modifier.clickable { open = true }.padding(vertical = 4.dp))
        DropdownMenu(expanded = open, onDismissRequest = { open = false }, containerColor = Palette.Cloth) {
            DropdownMenuItem(
                text = { Text("Download on Wi-Fi only") },
                trailingIcon = {
                    Checkbox(
                        checked = wifiOnly,
                        onCheckedChange = null,
                        colors = CheckboxDefaults.colors(checkedColor = Palette.Ink, checkmarkColor = Palette.Page, uncheckedColor = Palette.Muted),
                    )
                },
                onClick = { onWifiOnlyChange(!wifiOnly) },
            )
            DropdownMenuItem(text = { Text("Sign out") }, onClick = { open = false; onSignOut() })
        }
    }
}

/** Covers settle a little under the finger, as on the web. */
@Composable
private fun Modifier.pressable(onClick: () -> Unit): Modifier {
    val source = remember { MutableInteractionSource() }
    val pressed by source.collectIsPressedAsState()
    val scale by animateFloatAsState(if (pressed) 0.97f else 1f, label = "cover")
    return this.scale(scale).clickable(source, indication = null, onClick = onClick)
}

@Composable
private fun Featured(item: InProgress, coverUrl: (BookSummary) -> String?, onOpen: (BookSummary) -> Unit, onResume: (BookSummary) -> Unit) {
    val (book, progress) = item
    Column(verticalArrangement = Arrangement.spacedBy(20.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(ColGap)) {
            Box(Modifier.weight(1f).pressable { onOpen(book) }) {
                Cover(coverUrl(book), book.title, progress = (progress.positionSeconds / book.durationSeconds).toFloat())
            }
            Column(Modifier.weight(1f).clickable { onOpen(book) }) {
                Text(book.title, style = MaterialTheme.typography.displaySmall)
                Spacer(Modifier.height(6.dp))
                book.author?.let { Text(it, style = secondaryItalic.copy(fontSize = MaterialTheme.typography.titleMedium.fontSize)) }
            }
        }
        Column {
            Timeline(progress.positionSeconds, 0.0, book.durationSeconds, chapters = emptyList())
            Row(Modifier.fillMaxWidth().padding(top = 6.dp), horizontalArrangement = Arrangement.SpaceBetween) {
                Text("${((progress.positionSeconds / book.durationSeconds) * 100).roundToInt()}% listened", style = MaterialTheme.typography.bodySmall, color = Palette.Muted)
                Text(formatRemaining(book.durationSeconds, progress.positionSeconds), style = MaterialTheme.typography.bodySmall, color = Palette.Muted)
            }
            Spacer(Modifier.height(22.dp))
            PillButton("Resume", onClick = { onResume(book) }, leading = {
                Icon(PlayerIcons.Play, contentDescription = null, tint = Palette.Page, modifier = Modifier.width(18.dp).height(18.dp))
            })
        }
    }
}

@Composable
private fun ContinueRow(item: InProgress, coverUrl: String?, onOpen: () -> Unit) {
    val (book, progress) = item
    Row(horizontalArrangement = Arrangement.spacedBy(ColGap), modifier = Modifier.fillMaxWidth()) {
        Box(Modifier.weight(1f).pressable(onOpen)) {
            Cover(coverUrl, book.title, progress = (progress.positionSeconds / book.durationSeconds).toFloat())
        }
        Column(Modifier.weight(1f).clickable(onClick = onOpen), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(book.title, style = MaterialTheme.typography.titleMedium)
            book.author?.let { Text(it, style = secondaryItalic) }
            Text(
                formatRemaining(book.durationSeconds, progress.positionSeconds),
                style = MaterialTheme.typography.bodySmall,
                color = Palette.Muted,
                modifier = Modifier.padding(top = 6.dp),
            )
        }
    }
}

@Composable
private fun BookTile(book: BookSummary, coverUrl: String?, progress: Float?, downloaded: Boolean, onClick: () -> Unit) {
    Column(Modifier.clickable(onClick = onClick), verticalArrangement = Arrangement.spacedBy(2.dp)) {
        Box(Modifier.pressable(onClick)) { Cover(coverUrl, book.title, progress = progress) }
        Spacer(Modifier.height(10.dp))
        Text(book.title, style = MaterialTheme.typography.titleMedium, maxLines = 3, overflow = TextOverflow.Ellipsis)
        book.author?.let { Text(it, style = secondaryItalic, maxLines = 1, overflow = TextOverflow.Ellipsis) }
        Text(
            formatDuration(book.durationSeconds) + if (downloaded) " · on this phone" else "",
            style = MaterialTheme.typography.bodySmall,
            color = Palette.Faint,
        )
    }
}
