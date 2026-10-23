package dev.deanburgoyne.audiobooks.player

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import dev.deanburgoyne.audiobooks.playback.NowPlaying
import dev.deanburgoyne.audiobooks.playback.SleepTimer
import dev.deanburgoyne.audiobooks.playback.formatClock
import dev.deanburgoyne.audiobooks.ui.Palette
import dev.deanburgoyne.audiobooks.ui.secondaryItalic
import kotlin.math.ceil

/** The web's choices (player/sleepTimer.ts SLEEP_MINUTES). */
private val SleepMinutes = listOf(5, 15, 30, 45, 60)

/**
 * The sleep timer as muted italic text that opens a menu: on the book screen beside
 * Play, and on the full player in the status spot. [idleLabel] shows while no timer
 * runs (the full player uses it for "Loading…").
 */
@Composable
fun SleepControl(
    now: NowPlaying,
    onTimer: (SleepTimer?) -> Unit,
    modifier: Modifier = Modifier,
    idleLabel: String = "Sleep timer",
) {
    var open by remember { mutableStateOf(false) }
    val remaining = now.sleepRemainingSeconds
    val label = when {
        now.sleepAtChapterEnd -> "Sleeping at chapter end"
        // Rounded up, so it reads 0:01 until the moment it stops, not 0:00.
        remaining != null -> "Sleeping in ${formatClock(ceil(remaining))}"
        else -> idleLabel
    }
    Box(modifier) {
        Text(label, style = secondaryItalic, maxLines = 1, modifier = Modifier.clickable { open = true }.padding(vertical = 6.dp))
        DropdownMenu(expanded = open, onDismissRequest = { open = false }, containerColor = Palette.Cloth) {
            SleepMinutes.forEach { m ->
                DropdownMenuItem(text = { Text("In $m minutes") }, onClick = { onTimer(SleepTimer.After(m)); open = false })
            }
            DropdownMenuItem(text = { Text("At the end of this chapter") }, onClick = { onTimer(SleepTimer.EndOfChapter); open = false })
            if (now.sleepAtChapterEnd || remaining != null) {
                DropdownMenuItem(text = { Text("Off") }, onClick = { onTimer(null); open = false })
            }
        }
    }
}
