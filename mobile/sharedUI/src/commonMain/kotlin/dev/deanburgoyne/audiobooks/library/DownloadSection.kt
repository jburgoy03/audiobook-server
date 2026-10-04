package dev.deanburgoyne.audiobooks.library

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.unit.dp
import dev.deanburgoyne.audiobooks.api.BookDetail
import dev.deanburgoyne.audiobooks.downloads.DownloadState
import dev.deanburgoyne.audiobooks.downloads.DownloadedBook
import dev.deanburgoyne.audiobooks.ui.formatBytes
import dev.deanburgoyne.audiobooks.ui.ProgressLine
import dev.deanburgoyne.audiobooks.ui.QuietButton
import dev.deanburgoyne.audiobooks.ui.secondaryItalic

/**
 * Keeping the book on the phone, in the quiet register: outlined and bare pills, a
 * bone progress line (not red: a download isn't a position in the book), and the
 * state in muted italic.
 */
@Composable
fun DownloadSection(
    book: BookDetail,
    download: DownloadedBook?,
    wifiOnly: Boolean,
    offline: Boolean,
    onDownload: () -> Unit,
    onRetry: () -> Unit,
    onRemove: () -> Unit,
) {
    val size = formatBytes(book.files.sumOf { it.sizeBytes })

    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        when {
            download == null ->
                QuietButton("Download to this phone · $size", onDownload, outlined = true, enabled = !offline)

            download.state == DownloadState.Complete && download.isStale -> {
                Text("The server's copy has changed since this was downloaded.", style = secondaryItalic)
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    QuietButton("Download again", onDownload, outlined = true, enabled = !offline)
                    QuietButton("Remove", onRemove)
                }
            }

            download.state == DownloadState.Complete -> Row(
                Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text("On this phone · $size", style = secondaryItalic)
                QuietButton("Remove", onRemove)
            }

            download.state == DownloadState.Failed -> {
                Text(
                    "The download stopped" + (download.error?.let { ": $it" } ?: "."),
                    style = MaterialTheme.typography.bodyMedium.copy(fontStyle = FontStyle.Italic),
                )
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    QuietButton("Try again", onRetry, outlined = true)
                    QuietButton("Remove", onRemove)
                }
            }

            else -> {
                val fraction = if (download.totalBytes > 0) download.downloadedBytes.toFloat() / download.totalBytes else 0f
                ProgressLine(fraction)
                Row(
                    Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        when {
                            download.state == DownloadState.Downloading -> "Downloading · ${(fraction * 100).toInt()}% of $size"
                            download.error != null -> "Will try again: ${download.error}"
                            wifiOnly -> "Waiting for Wi-Fi"
                            else -> "Waiting to start"
                        },
                        style = secondaryItalic,
                        modifier = Modifier.weight(1f),
                    )
                    QuietButton("Cancel", onRemove)
                }
            }
        }
    }
}
