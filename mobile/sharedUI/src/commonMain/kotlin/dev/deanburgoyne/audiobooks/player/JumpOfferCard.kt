package dev.deanburgoyne.audiobooks.player

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import dev.deanburgoyne.audiobooks.playback.formatClock
import dev.deanburgoyne.audiobooks.progress.JumpOffer
import dev.deanburgoyne.audiobooks.ui.Palette
import dev.deanburgoyne.audiobooks.ui.PillButton
import dev.deanburgoyne.audiobooks.ui.QuietButton

/**
 * Another device got further: a quiet line, not a dialog, and no red (the offer is
 * somewhere else's position, not this book's playhead). The book keeps playing where
 * it is until the listener picks. The web's wording: "Further along on <device>, at <time>".
 */
@Composable
fun JumpOfferCard(offer: JumpOffer, onJump: () -> Unit, onStay: () -> Unit, modifier: Modifier = Modifier) {
    val shape = RoundedCornerShape(10.dp)
    Column(
        modifier
            .fillMaxWidth()
            .background(Palette.Raised, shape)
            .border(1.dp, Palette.Rule, shape)
            .padding(start = 16.dp, end = 10.dp, top = 10.dp, bottom = 10.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Text(
            "Further along on ${offer.deviceName ?: "another device"}, at ${formatClock(offer.positionSeconds)}",
            style = MaterialTheme.typography.bodyMedium,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            PillButton("Jump there", onJump)
            QuietButton("Stay here", onStay)
        }
    }
}
