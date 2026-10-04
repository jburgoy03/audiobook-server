package dev.deanburgoyne.audiobooks.library

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import dev.deanburgoyne.audiobooks.api.BookDetail
import dev.deanburgoyne.audiobooks.downloads.DownloadState
import dev.deanburgoyne.audiobooks.downloads.DownloadedBook
import dev.deanburgoyne.audiobooks.ui.formatBytes

/** Download, its progress, and what can be done with it, on the book page. */
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
    val muted = MaterialTheme.colorScheme.onSurfaceVariant

    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        when {
            download == null -> OutlinedButton(onClick = onDownload, enabled = !offline, modifier = Modifier.fillMaxWidth()) {
                Text("Download ($size)")
            }

            download.state == DownloadState.Complete && download.isStale -> {
                Text("The server's copy of this book has changed since it was downloaded.", style = MaterialTheme.typography.bodySmall)
                Row(verticalAlignment = Alignment.CenterVertically) {
                    TextButton(onClick = onDownload, enabled = !offline) { Text("Download again") }
                    TextButton(onClick = onRemove) { Text("Remove") }
                }
            }

            download.state == DownloadState.Complete -> Row(
                Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text("Downloaded · $size", style = MaterialTheme.typography.bodyMedium, color = muted)
                TextButton(onClick = onRemove) { Text("Remove") }
            }

            download.state == DownloadState.Failed -> {
                Text(
                    "Download failed" + (download.error?.let { ": $it" } ?: "."),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.error,
                )
                Row {
                    TextButton(onClick = onRetry) { Text("Try again") }
                    TextButton(onClick = onRemove) { Text("Remove") }
                }
            }

            else -> {
                val fraction = if (download.totalBytes > 0) download.downloadedBytes.toFloat() / download.totalBytes else 0f
                LinearProgressIndicator(progress = { fraction }, modifier = Modifier.fillMaxWidth())
                Row(
                    Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        when {
                            download.state == DownloadState.Downloading ->
                                "${(fraction * 100).toInt()}% of $size"
                            download.error != null -> "Will retry: ${download.error}"
                            wifiOnly -> "Waiting for Wi-Fi"
                            else -> "Waiting to start"
                        },
                        style = MaterialTheme.typography.bodySmall,
                        color = muted,
                        modifier = Modifier.weight(1f),
                    )
                    TextButton(onClick = onRemove) { Text("Cancel") }
                }
            }
        }
    }
}
