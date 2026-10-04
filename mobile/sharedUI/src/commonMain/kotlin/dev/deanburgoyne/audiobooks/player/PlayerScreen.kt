package dev.deanburgoyne.audiobooks.player

import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import dev.deanburgoyne.audiobooks.library.Cover
import dev.deanburgoyne.audiobooks.playback.BookPlayer
import dev.deanburgoyne.audiobooks.playback.NowPlaying
import dev.deanburgoyne.audiobooks.playback.SleepTimer
import dev.deanburgoyne.audiobooks.playback.formatClock
import dev.deanburgoyne.audiobooks.ui.ControlButton
import dev.deanburgoyne.audiobooks.ui.Palette
import dev.deanburgoyne.audiobooks.ui.PlayerIcons
import dev.deanburgoyne.audiobooks.ui.SkipIcon
import dev.deanburgoyne.audiobooks.ui.Timeline
import dev.deanburgoyne.audiobooks.ui.formatRemaining
import dev.deanburgoyne.audiobooks.ui.secondaryItalic

private val Speeds = listOf(0.8f, 1.0f, 1.1f, 1.25f, 1.5f, 1.75f, 2.0f)
private val SleepMinutes = listOf(15, 30, 45, 60)

/**
 * The full player, in the web player's arrangement: the chapter title large, its
 * place in the book in italic, the timeline, the transport centred under it, status
 * on the left edge and speed on the right.
 *
 * Two timelines, one look. The draggable one covers the current chapter: across a
 * 20-hour book a phone-width bar moves minutes per pixel, too coarse to find a
 * sentence. The thin one under it is the whole book in chapter segments, the web's
 * bar exactly, to show where this chapter sits.
 */
@Composable
fun PlayerScreen(now: NowPlaying, coverUrl: String?, player: BookPlayer, onClose: () -> Unit, offer: @Composable () -> Unit = {}) {
    Column(
        Modifier.fillMaxSize().safeDrawingPadding().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp),
    ) {
        Row(Modifier.fillMaxWidth().padding(top = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            ControlButton(onClick = onClose, size = 44.dp, modifier = Modifier.padding(start = 0.dp)) {
                Icon(PlayerIcons.Collapse, contentDescription = "Close player", tint = Palette.Muted, modifier = Modifier.size(24.dp))
            }
            Spacer(Modifier.weight(1f))
        }

        Box(Modifier.fillMaxWidth().padding(top = 8.dp, bottom = 28.dp), contentAlignment = Alignment.Center) {
            Cover(coverUrl, now.title, Modifier.widthIn(max = 340.dp).fillMaxWidth())
        }

        Text(now.chapterTitle ?: now.title, style = MaterialTheme.typography.headlineSmall)
        Row(Modifier.fillMaxWidth().padding(top = 4.dp, bottom = 14.dp), horizontalArrangement = Arrangement.SpaceBetween) {
            Text(now.title + (now.author?.let { " · $it" } ?: ""), style = secondaryItalic, maxLines = 1, modifier = Modifier.weight(1f, fill = false))
            now.chapterNumber?.let { Text("Chapter $it of ${now.chapterCount}", style = secondaryItalic, modifier = Modifier.padding(start = 12.dp)) }
        }

        offer()

        // The current chapter, draggable: a time label follows the finger.
        val start = now.chapterStartSeconds ?: 0.0
        val end = now.chapterEndSeconds ?: now.durationSeconds
        var dragging by remember { mutableStateOf<Double?>(null) }
        val shown = dragging ?: now.positionSeconds
        Timeline(
            position = now.positionSeconds, start = start, end = end, chapters = now.chapters,
            interactive = true, onSeek = player::seekTo, onDrag = { dragging = it },
        )
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            Text(formatClock(shown - start), style = MaterialTheme.typography.bodySmall, color = Palette.Muted)
            Text("−" + formatClock(end - shown), style = MaterialTheme.typography.bodySmall, color = Palette.Muted)
        }

        Row(
            Modifier.fillMaxWidth().padding(top = 18.dp),
            horizontalArrangement = Arrangement.Center,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            ControlButton(onClick = player::previousChapter, enabled = now.chapterCount > 1) {
                Icon(PlayerIcons.PreviousChapter, contentDescription = "Previous chapter", tint = Palette.Ink, modifier = Modifier.size(22.dp))
            }
            ControlButton(onClick = { player.skipBy(-30.0) }) { SkipIcon(forward = false, tint = Palette.Ink) }
            ControlButton(onClick = player::togglePlayPause, size = 72.dp, solid = true, modifier = Modifier.padding(horizontal = 8.dp)) {
                Icon(
                    if (now.isPlaying) PlayerIcons.Pause else PlayerIcons.Play,
                    contentDescription = if (now.isPlaying) "Pause" else "Play",
                    tint = Palette.Page,
                    modifier = Modifier.size(30.dp),
                )
            }
            ControlButton(onClick = { player.skipBy(30.0) }) { SkipIcon(forward = true, tint = Palette.Ink) }
            ControlButton(onClick = player::nextChapter, enabled = now.chapterCount > 1) {
                Icon(PlayerIcons.NextChapter, contentDescription = "Next chapter", tint = Palette.Ink, modifier = Modifier.size(22.dp))
            }
        }

        Row(
            Modifier.fillMaxWidth().padding(top = 20.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            SleepControl(now, player::setSleepTimer)
            SpeedControl(now.speed, player::setSpeed)
        }

        // The whole book, in the web's segmented bar.
        Column(Modifier.padding(top = 32.dp, bottom = 24.dp)) {
            Timeline(now.positionSeconds, 0.0, now.durationSeconds, now.chapters)
            Row(Modifier.fillMaxWidth().padding(top = 4.dp), horizontalArrangement = Arrangement.SpaceBetween) {
                Text("${((now.positionSeconds / now.durationSeconds.coerceAtLeast(1.0)) * 100).toInt()}% of the book", style = MaterialTheme.typography.bodySmall, color = Palette.Muted)
                Text(formatRemaining(now.durationSeconds, now.positionSeconds), style = MaterialTheme.typography.bodySmall, color = Palette.Muted)
            }
        }
    }
}

/** Speed: muted label and a small bordered value, as the web's select. */
@Composable
private fun SpeedControl(speed: Float, onSpeed: (Float) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("Speed", style = MaterialTheme.typography.bodyMedium, color = Palette.Muted)
        Box {
            Text(
                speedLabel(speed),
                style = MaterialTheme.typography.bodyMedium,
                modifier = Modifier
                    .border(1.dp, Palette.Rule, RoundedCornerShape(6.dp))
                    .clickable { open = true }
                    .padding(horizontal = 10.dp, vertical = 4.dp),
            )
            DropdownMenu(expanded = open, onDismissRequest = { open = false }, containerColor = Palette.Cloth) {
                Speeds.forEach { s ->
                    DropdownMenuItem(
                        text = { Text(speedLabel(s), fontWeight = if (s == speed) FontWeight.SemiBold else FontWeight.Normal) },
                        trailingIcon = { if (s == speed) Text("✓") },
                        onClick = { onSpeed(s); open = false },
                    )
                }
            }
        }
    }
}

/** The sleep timer, as the status on the left edge: muted italic, tappable. */
@Composable
private fun SleepControl(now: NowPlaying, onTimer: (SleepTimer?) -> Unit) {
    var open by remember { mutableStateOf(false) }
    val label = when {
        now.sleepAtChapterEnd -> "Sleeping at the chapter's end"
        now.sleepRemainingSeconds != null -> "Sleeping in ${formatClock(now.sleepRemainingSeconds!!)}"
        now.isBuffering && now.isPlaying -> "Loading…"
        else -> "Sleep timer"
    }
    Box {
        Text(label, style = secondaryItalic, modifier = Modifier.clickable { open = true }.padding(vertical = 6.dp))
        DropdownMenu(expanded = open, onDismissRequest = { open = false }, containerColor = Palette.Cloth) {
            SleepMinutes.forEach { m ->
                DropdownMenuItem(text = { Text("In $m minutes") }, onClick = { onTimer(SleepTimer.After(m)); open = false })
            }
            DropdownMenuItem(text = { Text("At the end of this chapter") }, onClick = { onTimer(SleepTimer.EndOfChapter); open = false })
            if (now.sleepAtChapterEnd || now.sleepRemainingSeconds != null) {
                DropdownMenuItem(text = { Text("Off") }, onClick = { onTimer(null); open = false })
            }
        }
    }
}

private fun speedLabel(speed: Float) = speed.toString().trimEnd('0').trimEnd('.') + "×"
