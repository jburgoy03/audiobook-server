package dev.deanburgoyne.audiobooks.library

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import dev.deanburgoyne.audiobooks.api.AudiobookApi
import dev.deanburgoyne.audiobooks.api.BookSummary
import dev.deanburgoyne.audiobooks.downloads.DownloadSettings
import dev.deanburgoyne.audiobooks.downloads.Downloads
import dev.deanburgoyne.audiobooks.playback.BookPlayer
import dev.deanburgoyne.audiobooks.player.JumpOfferCard
import dev.deanburgoyne.audiobooks.player.NowPlayingBar
import dev.deanburgoyne.audiobooks.player.PlayerScreen
import dev.deanburgoyne.audiobooks.progress.ProgressSync
import dev.deanburgoyne.audiobooks.ui.SystemBackHandler
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch

/**
 * Library → book → player, and back. Shallow enough not to need a navigation
 * library; the open book and the player's state are saved, so rotation and process
 * death return to them.
 */
@Composable
fun LibraryHost(
    api: AudiobookApi,
    player: BookPlayer,
    progress: ProgressSync,
    downloads: Downloads,
    downloadSettings: DownloadSettings,
    onSignOut: () -> Unit,
) {
    val scope = rememberCoroutineScope()
    var wifiOnly by remember { mutableStateOf(downloadSettings.wifiOnly) }
    val library = viewModel { LibraryViewModel(api, progress, downloads) }
    val state by library.state.collectAsState()
    var openBookId by rememberSaveable { mutableStateOf<String?>(null) }
    var playerOpen by rememberSaveable { mutableStateOf(false) }
    val nowPlaying by player.state.collectAsState()
    val offer by progress.offer.collectAsState()

    // 640 for the grid and the featured cover, 1080 for the book page and player,
    // 320 for the bar: what the server's thumbnails are for.
    val cover = { hasCover: Boolean, id: String, size: Int -> if (hasCover) api.coverUrl(id, size) else null }

    // Resume from the library: the book's details (from the server, or the download
    // offline), then play from where this device knows the listener to be.
    val resume = { book: BookSummary ->
        scope.launch {
            if (nowPlaying?.bookId == book.id) {
                if (nowPlaying?.isPlaying != true) player.togglePlayPause()
            } else {
                val detail = try { api.book(book.id) } catch (e: CancellationException) { throw e } catch (_: Exception) { null }
                    ?: downloads.detail(book.id)
                val at = state.progressByBook[book.id]?.takeIf { !it.isFinished }?.positionSeconds ?: 0.0
                detail?.let { player.play(it, at) }
            }
        }
        Unit
    }

    val jump = @Composable { bookId: String, positionSeconds: Double ->
        offer?.takeIf { it.bookId == bookId }?.let { o ->
            JumpOfferCard(
                offer = o,
                onJump = { progress.acceptOffer()?.let { player.seekTo(it.positionSeconds) } },
                onStay = { progress.dismissOffer(positionSeconds) },
                modifier = Modifier.padding(bottom = 16.dp),
            )
        }
    }

    val playingNow = nowPlaying
    if (playerOpen && playingNow != null) {
        SystemBackHandler { playerOpen = false }
        PlayerScreen(
            now = playingNow,
            coverUrl = cover(true, playingNow.bookId, 1080),
            player = player,
            onClose = { playerOpen = false },
            offer = { jump(playingNow.bookId, playingNow.positionSeconds) },
        )
        return
    }

    Column(Modifier.fillMaxSize()) {
        // While the bar shows, it owns the navigation-bar inset; the screens above
        // mustn't pad for it a second time.
        val screens = if (playingNow != null) Modifier.consumeWindowInsets(WindowInsets.navigationBars) else Modifier
        Box(Modifier.weight(1f).then(screens)) {
            val bookId = openBookId
            if (bookId == null) {
                LibraryScreen(
                    state = state,
                    coverUrl = { cover(it.hasCover, it.id, 640) },
                    onRefresh = library::refresh,
                    onOpen = { openBookId = it.id },
                    onResume = resume,
                    onSignOut = onSignOut,
                    wifiOnly = wifiOnly,
                    onWifiOnlyChange = {
                        wifiOnly = it
                        downloadSettings.wifiOnly = it
                    },
                )
            } else {
                SystemBackHandler { openBookId = null }
                // Keyed by book, so opening another book gets a fresh view model.
                val book = viewModel(key = "book-$bookId") { BookViewModel(api, bookId, downloads) }
                val bookState by book.state.collectAsState()
                BookScreen(
                    state = bookState,
                    progress = state.progressByBook[bookId],
                    nowPlaying = playingNow,
                    coverUrl = { cover(it.hasCover, it.id, 1080) },
                    onBack = { openBookId = null },
                    onRetry = book::load,
                    onPlay = player::play,
                    onToggle = player::togglePlayPause,
                    onSeek = player::seekTo,
                    download = state.downloads[bookId],
                    wifiOnly = wifiOnly,
                    onDownload = { scope.launch { downloads.start(it) } },
                    onRetryDownload = { scope.launch { downloads.retry(bookId) } },
                    onRemoveDownload = { scope.launch { downloads.remove(bookId) } },
                )
            }
        }
        playingNow?.let { now ->
            NowPlayingBar(
                now = now,
                coverUrl = api.coverUrl(now.bookId, size = 320),
                onToggle = player::togglePlayPause,
                onSkip = player::skipBy,
                onOpen = { playerOpen = true },
                above = {
                    if (offer?.bookId == now.bookId) Box(Modifier.padding(top = 12.dp)) { jump(now.bookId, now.positionSeconds) }
                },
            )
        }
    }
}
