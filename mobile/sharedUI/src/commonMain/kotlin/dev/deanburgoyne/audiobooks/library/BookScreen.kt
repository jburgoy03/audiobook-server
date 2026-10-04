package dev.deanburgoyne.audiobooks.library

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
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
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.CompositingStrategy
import androidx.compose.ui.graphics.BlendMode
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import dev.deanburgoyne.audiobooks.api.BookDetail
import dev.deanburgoyne.audiobooks.api.Progress
import dev.deanburgoyne.audiobooks.downloads.DownloadedBook
import dev.deanburgoyne.audiobooks.playback.NowPlaying
import dev.deanburgoyne.audiobooks.playback.indexAt
import dev.deanburgoyne.audiobooks.ui.LevelMeter
import dev.deanburgoyne.audiobooks.ui.Palette
import dev.deanburgoyne.audiobooks.ui.PillButton
import dev.deanburgoyne.audiobooks.ui.PlayerIcons
import dev.deanburgoyne.audiobooks.ui.RuledRow
import dev.deanburgoyne.audiobooks.ui.SectionHead
import dev.deanburgoyne.audiobooks.ui.Timeline
import dev.deanburgoyne.audiobooks.ui.emphasised
import dev.deanburgoyne.audiobooks.ui.formatDuration
import dev.deanburgoyne.audiobooks.ui.formatRemaining
import dev.deanburgoyne.audiobooks.ui.secondaryItalic
import kotlin.math.roundToInt

private val Gutter = 20.dp

@Composable
fun BookScreen(
    state: BookState,
    progress: Progress?,
    nowPlaying: NowPlaying?,
    coverUrl: (BookDetail) -> String?,
    onBack: () -> Unit,
    onRetry: () -> Unit,
    onPlay: (BookDetail, startAt: Double) -> Unit,
    onToggle: () -> Unit,
    onSeek: (Double) -> Unit,
    download: DownloadedBook?,
    wifiOnly: Boolean,
    onDownload: (BookDetail) -> Unit,
    onRetryDownload: () -> Unit,
    onRemoveDownload: () -> Unit,
) {
    Column(Modifier.fillMaxSize().statusBarsPadding()) {
        BackLink(onBack)
        Box(Modifier.fillMaxSize()) {
            when (state) {
                BookState.Loading -> CircularProgressIndicator(Modifier.align(Alignment.Center), color = Palette.Muted, strokeWidth = 2.dp)
                BookState.Gone -> Notice("This book is no longer in the library.")
                is BookState.Failed -> Column(Modifier.padding(Gutter), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    Text(state.message, style = MaterialTheme.typography.bodyLarge.copy(fontStyle = FontStyle.Italic))
                    PillButton("Try again", onRetry)
                }
                is BookState.Loaded -> {
                    val book = state.book
                    val playing = nowPlaying?.takeIf { it.bookId == book.id }
                    BookDetails(
                        book = book,
                        progress = progress,
                        playing = playing,
                        coverUrl = coverUrl(book),
                        onPlay = onPlay,
                        onToggle = onToggle,
                        onChapter = { start -> if (playing != null) onSeek(start) else onPlay(book, start) },
                    ) {
                        DownloadSection(
                            book = book,
                            download = download,
                            wifiOnly = wifiOnly,
                            offline = state.offline,
                            onDownload = { onDownload(book) },
                            onRetry = onRetryDownload,
                            onRemove = onRemoveDownload,
                        )
                    }
                }
            }
        }
    }
}

/** "‹ Library", muted italic, its chevron's stroke (not its box) on the left edge. */
@Composable
private fun BackLink(onBack: () -> Unit) {
    Row(
        Modifier.clickable(onClick = onBack).padding(start = Gutter - 6.dp, end = 16.dp, top = 14.dp, bottom = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        Icon(PlayerIcons.Back, contentDescription = null, tint = Palette.Muted, modifier = Modifier.size(18.dp))
        Text("Library", style = secondaryItalic)
    }
}

@Composable
private fun BookDetails(
    book: BookDetail,
    progress: Progress?,
    playing: NowPlaying?,
    coverUrl: String?,
    onPlay: (BookDetail, Double) -> Unit,
    onToggle: () -> Unit,
    onChapter: (Double) -> Unit,
    downloadSection: @Composable () -> Unit,
) {
    // The player's own position wins over the server's while this book is loaded:
    // it's newer. Otherwise the server's (or this device's cached) position.
    val position = playing?.positionSeconds ?: progress?.takeIf { !it.isFinished }?.positionSeconds
    val started = position != null && position > 0
    val currentChapter = if (position != null) book.chapters.indexAt(position) else -1

    LazyColumn(
        contentPadding = PaddingValues(start = Gutter, end = Gutter, top = 8.dp, bottom = 40.dp),
        modifier = Modifier.fillMaxSize(),
    ) {
        // Cover on the first of two tracks, heading on the second: the cover is
        // exactly one library tile wide.
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(16.dp), modifier = Modifier.fillMaxWidth()) {
                Box(Modifier.weight(1f)) {
                    Cover(coverUrl, book.title, progress = position?.let { (it / book.durationSeconds).toFloat() })
                }
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(book.title, style = MaterialTheme.typography.headlineLarge)
                    book.subtitle?.let { Text(it, style = MaterialTheme.typography.titleMedium.copy(fontStyle = FontStyle.Italic, fontWeight = FontWeight.Normal)) }
                    Text(
                        "by ${book.author ?: "Unknown author"}",
                        style = secondaryItalic.copy(fontSize = MaterialTheme.typography.titleMedium.fontSize),
                        modifier = Modifier.padding(top = 4.dp),
                    )
                    book.narrator?.let { Text("Read by $it", style = MaterialTheme.typography.bodyMedium, color = Palette.Muted) }
                    book.credit?.let { Text(it, style = MaterialTheme.typography.bodyMedium, color = Palette.Muted) }
                }
            }
        }

        // The player block: where you are, then Resume.
        item {
            Column(Modifier.padding(top = 32.dp)) {
                val chapter = book.chapters.getOrNull(currentChapter)
                if (started && chapter != null) {
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.Bottom) {
                        Text(chapter.title, style = MaterialTheme.typography.bodyLarge, maxLines = 1, modifier = Modifier.weight(1f, fill = false))
                        Text("Chapter ${currentChapter + 1} of ${book.chapters.size}", style = secondaryItalic, modifier = Modifier.padding(start = 12.dp))
                    }
                }
                Spacer(Modifier.height(10.dp))
                Timeline(position ?: 0.0, 0.0, book.durationSeconds, book.chapters, interactive = playing != null, onSeek = onChapter)
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    Text(
                        if (started) "${((position!! / book.durationSeconds) * 100).roundToInt()}% listened" else formatDuration(book.durationSeconds),
                        style = MaterialTheme.typography.bodySmall, color = Palette.Muted,
                    )
                    Text(
                        when {
                            progress?.isFinished == true && playing == null -> "Finished"
                            started -> formatRemaining(book.durationSeconds, position!!)
                            else -> book.publishedYear?.toString().orEmpty()
                        },
                        style = MaterialTheme.typography.bodySmall, color = Palette.Muted,
                    )
                }
                Spacer(Modifier.height(22.dp))
                PillButton(
                    text = when {
                        playing?.isPlaying == true -> "Pause"
                        started -> "Resume"
                        else -> "Play"
                    },
                    onClick = { if (playing != null) onToggle() else onPlay(book, position ?: 0.0) },
                    leading = {
                        Icon(
                            if (playing?.isPlaying == true) PlayerIcons.Pause else PlayerIcons.Play,
                            contentDescription = null, tint = Palette.Page, modifier = Modifier.size(18.dp),
                        )
                    },
                )
                Spacer(Modifier.height(20.dp))
                downloadSection()
            }
        }

        book.description?.let { description ->
            item { Blurb(description, book.descriptionSource, Modifier.padding(top = 28.dp)) }
        }

        if (book.chapters.size > 1) {
            item { SectionHead("Chapters", Modifier.padding(top = 44.dp), count = book.chapters.size.toString()) }
            itemsIndexed(book.chapters, key = { _, c -> c.sequence }) { i, chapter ->
                val state = when {
                    i == currentChapter && started -> ChapterState.Current
                    i < currentChapter -> ChapterState.Played
                    else -> ChapterState.Ahead
                }
                ChapterRow(chapter.title, chapter.endOffsetSeconds - chapter.startOffsetSeconds, state, playing?.isPlaying == true) {
                    onChapter(chapter.startOffsetSeconds)
                }
            }
        }
    }
}

private enum class ChapterState { Played, Current, Ahead }

/**
 * A chapter row: rules edge to edge. The current chapter is bold with the red bar in
 * its left inset and, while audio plays, the level meter beside its length; played
 * chapters are muted. Tapping one plays from its start.
 */
@Composable
private fun ChapterRow(title: String, seconds: Double, state: ChapterState, playing: Boolean, onClick: () -> Unit) {
    Box {
        RuledRow(onClick = onClick) {
            Text(
                title,
                style = MaterialTheme.typography.bodyLarge.copy(fontWeight = if (state == ChapterState.Current) FontWeight.SemiBold else FontWeight.Normal),
                color = if (state == ChapterState.Played) Palette.Muted else Palette.Ink,
                modifier = Modifier.weight(1f),
            )
            if (state == ChapterState.Current) LevelMeter(active = playing)
            Text(
                formatDuration(seconds),
                style = MaterialTheme.typography.bodyMedium,
                color = if (state == ChapterState.Current) Palette.Muted else Palette.Faint,
            )
        }
        if (state == ChapterState.Current) {
            Box(Modifier.align(Alignment.CenterStart).padding(vertical = 10.dp).width(3.dp).height(24.dp).background(Palette.Ribbon))
        }
    }
}

/** Five lines with a fade into the page, then "More"; the credit line underneath. */
@Composable
private fun Blurb(text: String, source: String?, modifier: Modifier = Modifier) {
    var open by rememberSaveable { mutableStateOf(false) }
    var clipped by remember { mutableStateOf(false) }
    Column(modifier) {
        Text(
            // Paragraphs as on the web (split on blank lines), emphasis as italic.
            emphasised(text.split(Regex("\n{2,}")).joinToString("\n\n") { it.trim() }),
            style = MaterialTheme.typography.bodyLarge,
            maxLines = if (open) Int.MAX_VALUE else 5,
            onTextLayout = { if (!open) clipped = it.hasVisualOverflow },
            modifier = if (!open && clipped) Modifier.fadeOut() else Modifier,
        )
        Row(Modifier.padding(top = 6.dp), horizontalArrangement = Arrangement.spacedBy(16.dp), verticalAlignment = Alignment.CenterVertically) {
            if (clipped || open) {
                Text(
                    if (open) "Less" else "More",
                    style = MaterialTheme.typography.bodySmall.copy(textDecoration = TextDecoration.Underline),
                    modifier = Modifier.clickable { open = !open }.padding(vertical = 4.dp),
                )
            }
            // The text is the catalogue's, not ours: name it.
            source?.let { Text("From $it", style = secondaryItalic.copy(fontSize = MaterialTheme.typography.bodySmall.fontSize)) }
        }
    }
}

/** The web's mask: solid to 55% of the height, then fading to nothing. */
private fun Modifier.fadeOut() = this
    .graphicsLayer { compositingStrategy = CompositingStrategy.Offscreen }
    .drawWithContent {
        drawContent()
        drawRect(
            Brush.verticalGradient(0.55f to Color.Black, 1f to Color.Transparent),
            blendMode = BlendMode.DstIn,
        )
    }

@Composable
private fun Notice(text: String) {
    Box(Modifier.fillMaxSize().padding(Gutter)) {
        Text(text, style = MaterialTheme.typography.bodyLarge.copy(fontStyle = FontStyle.Italic))
    }
}
