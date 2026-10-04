package dev.deanburgoyne.audiobooks.player

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import dev.deanburgoyne.audiobooks.progress.JumpOffer
import dev.deanburgoyne.audiobooks.ui.formatDuration

/**
 * Another device got further. Never acted on silently: jumping skips listening the
 * person may not have heard, staying overwrites the other device's position. Their
 * call, and either answer is remembered for that server state.
 */
@Composable
fun JumpOfferCard(offer: JumpOffer, currentPositionSeconds: Double, onJump: () -> Unit, onStay: () -> Unit) {
    Surface(color = MaterialTheme.colorScheme.secondaryContainer, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(
                "${offer.deviceName ?: "Another device"} is " +
                    "${formatDuration(offer.positionSeconds - currentPositionSeconds)} further along.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSecondaryContainer,
            )
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = onJump) { Text("Jump there") }
                TextButton(onClick = onStay) { Text("Stay here") }
            }
        }
    }
}
