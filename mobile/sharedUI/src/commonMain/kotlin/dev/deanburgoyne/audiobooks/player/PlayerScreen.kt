package dev.deanburgoyne.audiobooks.player

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import dev.deanburgoyne.audiobooks.library.Cover
import dev.deanburgoyne.audiobooks.playback.BookPlayer
import dev.deanburgoyne.audiobooks.playback.NowPlaying
import dev.deanburgoyne.audiobooks.playback.SleepTimer
import dev.deanburgoyne.audiobooks.playback.formatClock
import dev.deanburgoyne.audiobooks.ui.PlayerIcons
import dev.deanburgoyne.audiobooks.ui.formatRemaining
import kotlin.math.roundToInt

private val Speeds = listOf(0.8f, 1.0f, 1.1f, 1.25f, 1.5f, 1.75f, 2.0f)
private val SleepMinutes = listOf(15, 30, 45, 60)

/**
 * The full player. The slider covers the current chapter rather than the whole book:
 * across a 20-hour book a phone-width slider moves minutes per pixel, too coarse to
 * find a sentence. The whole book is the line of text under it.
 */
@Composable
fun PlayerScreen(now: NowPlaying, coverUrl: String?, player: BookPlayer, onClose: () -> Unit) {
    Column(
        Modifier.fillMaxSize().safeDrawingPadding().verticalScroll(rememberScrollState()).padding(horizontal = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Row(Modifier.fillMaxWidth()) {
            IconButton(onClick = onClose) { Icon(PlayerIcons.Collapse, contentDescription = "Close player") }
        }

        Cover(coverUrl, now.title, Modifier.widthIn(max = 320.dp).fillMaxWidth())

        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text(now.title, style = MaterialTheme.typography.titleLarge, textAlign = TextAlign.Center)
            now.author?.let { Text(it, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant) }
        }

        Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
            now.chapterTitle?.let { Text(it, style = MaterialTheme.typography.titleSmall, textAlign = TextAlign.Center) }
            now.chapterNumber?.let {
                Text("Chapter $it of ${now.chapterCount}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            ChapterSlider(now, onSeek = player::seekTo)
            Text(
                "${((now.positionSeconds / now.durationSeconds.coerceAtLeast(1.0)) * 100).roundToInt()}% · " +
                    formatRemaining(now.durationSeconds, now.positionSeconds) +
                    if (now.speed != 1f) " at ${speedLabel(now.speed)}" else "",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            now.error?.let { Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall) }
        }

        Row(
            Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceEvenly,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = player::previousChapter, enabled = now.chapterCount > 1) {
                Icon(PlayerIcons.PreviousChapter, contentDescription = "Previous chapter")
            }
            TextButton(onClick = { player.skipBy(-30.0) }) { Text("−30") }
            FilledIconButton(onClick = player::togglePlayPause, modifier = Modifier.size(72.dp)) {
                Icon(
                    if (now.isPlaying) PlayerIcons.Pause else PlayerIcons.Play,
                    contentDescription = if (now.isPlaying) "Pause" else "Play",
                    modifier = Modifier.size(36.dp),
                )
            }
            TextButton(onClick = { player.skipBy(30.0) }) { Text("+30") }
            IconButton(onClick = player::nextChapter, enabled = now.chapterCount > 1) {
                Icon(PlayerIcons.NextChapter, contentDescription = "Next chapter")
            }
        }

        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp, Alignment.CenterHorizontally)) {
            SpeedButton(now.speed, player::setSpeed)
            SleepButton(now, player::setSleepTimer)
        }
    }
}

@Composable
private fun ChapterSlider(now: NowPlaying, onSeek: (Double) -> Unit) {
    val start = now.chapterStartSeconds ?: 0.0
    val end = (now.chapterEndSeconds ?: now.durationSeconds).coerceAtLeast(start + 1)
    // While dragging, the thumb follows the finger; the seek happens on release, so a
    // drag across the chapter is one seek, not dozens.
    var dragging by remember { mutableStateOf<Float?>(null) }
    val shown = dragging ?: (now.positionSeconds - start).toFloat()

    Slider(
        value = shown.coerceIn(0f, (end - start).toFloat()),
        onValueChange = { dragging = it },
        onValueChangeFinished = {
            dragging?.let { onSeek(start + it) }
            dragging = null
        },
        valueRange = 0f..(end - start).toFloat(),
        modifier = Modifier.fillMaxWidth(),
    )
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        Text(formatClock(shown.toDouble()), style = MaterialTheme.typography.bodySmall)
        Text("−" + formatClock(end - start - shown), style = MaterialTheme.typography.bodySmall)
    }
}

@Composable
private fun SpeedButton(speed: Float, onSpeed: (Float) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Box {
        OutlinedButton(onClick = { open = true }) { Text(speedLabel(speed)) }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            Speeds.forEach { s ->
                DropdownMenuItem(text = { Text(speedLabel(s)) }, onClick = { onSpeed(s); open = false })
            }
        }
    }
}

@Composable
private fun SleepButton(now: NowPlaying, onTimer: (SleepTimer?) -> Unit) {
    var open by remember { mutableStateOf(false) }
    val label = when {
        now.sleepAtChapterEnd -> "Sleep: end of chapter"
        now.sleepRemainingSeconds != null -> "Sleep: ${formatClock(now.sleepRemainingSeconds!!)}"
        else -> "Sleep timer"
    }
    Box {
        OutlinedButton(onClick = { open = true }) { Text(label) }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            SleepMinutes.forEach { m ->
                DropdownMenuItem(text = { Text("$m minutes") }, onClick = { onTimer(SleepTimer.After(m)); open = false })
            }
            DropdownMenuItem(text = { Text("End of chapter") }, onClick = { onTimer(SleepTimer.EndOfChapter); open = false })
            if (now.sleepAtChapterEnd || now.sleepRemainingSeconds != null) {
                DropdownMenuItem(text = { Text("Off") }, onClick = { onTimer(null); open = false })
            }
        }
    }
}

private fun speedLabel(speed: Float): String {
    val text = speed.toString().trimEnd('0').trimEnd('.')
    return "${text}×"
}
