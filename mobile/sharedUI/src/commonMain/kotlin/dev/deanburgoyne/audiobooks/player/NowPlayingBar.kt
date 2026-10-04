package dev.deanburgoyne.audiobooks.player

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import dev.deanburgoyne.audiobooks.library.Cover
import dev.deanburgoyne.audiobooks.playback.NowPlaying
import dev.deanburgoyne.audiobooks.ui.ControlButton
import dev.deanburgoyne.audiobooks.ui.Palette
import dev.deanburgoyne.audiobooks.ui.PlayerIcons
import dev.deanburgoyne.audiobooks.ui.SkipIcon
import dev.deanburgoyne.audiobooks.ui.secondaryItalic

/**
 * A pane of smoked glass along the bottom, with the whole book's progress as a red
 * line along its top edge: the web's now-playing bar at phone width (book, then the
 * controls; volume and time remaining drop out, as they do there).
 */
@Composable
fun NowPlayingBar(
    now: NowPlaying,
    coverUrl: String?,
    onToggle: () -> Unit,
    onSkip: (Double) -> Unit,
    onOpen: () -> Unit,
    above: @Composable () -> Unit = {},
) {
    Column(Modifier.fillMaxWidth().background(Palette.Glass).navigationBarsPadding()) {
        val fraction = if (now.durationSeconds > 0) (now.positionSeconds / now.durationSeconds).toFloat() else 0f
        Box(
            Modifier.fillMaxWidth().height(2.dp).drawBehind {
                val w = size.width * fraction.coerceIn(0f, 1f)
                drawRect(Palette.Ribbon.copy(alpha = 0.3f), Offset(0f, -2f), Size(w, size.height + 4f))
                drawRect(Palette.Ribbon, Offset.Zero, Size(w, size.height))
            },
        )
        HorizontalDivider(color = Palette.Rule, thickness = 1.dp)
        Box(Modifier.padding(horizontal = 12.dp)) { above() }
        Row(
            Modifier.fillMaxWidth().height(80.dp).padding(horizontal = 20.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Row(
                Modifier.weight(1f).fillMaxHeight().clickable(onClick = onOpen),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(14.dp),
            ) {
                Cover(coverUrl, now.title, Modifier.size(52.dp))
                Column(Modifier.weight(1f)) {
                    Text(
                        now.title,
                        style = MaterialTheme.typography.bodyLarge.copy(fontWeight = FontWeight.Medium),
                        maxLines = 1, overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        now.error ?: now.chapterTitle ?: now.author.orEmpty(),
                        style = secondaryItalic.copy(fontSize = MaterialTheme.typography.bodySmall.fontSize),
                        maxLines = 1, overflow = TextOverflow.Ellipsis,
                    )
                }
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                ControlButton(onClick = { onSkip(-30.0) }, size = 40.dp) { SkipIcon(forward = false, size = 26.dp, tint = Palette.Ink) }
                ControlButton(onClick = onToggle, size = 52.dp, solid = true, modifier = Modifier.padding(horizontal = 6.dp)) {
                    Icon(if (now.isPlaying) PlayerIcons.Pause else PlayerIcons.Play, contentDescription = if (now.isPlaying) "Pause" else "Play", tint = Palette.Page, modifier = Modifier.size(24.dp))
                }
                ControlButton(onClick = { onSkip(30.0) }, size = 40.dp) { SkipIcon(forward = true, size = 26.dp, tint = Palette.Ink) }
            }
        }
    }
}
