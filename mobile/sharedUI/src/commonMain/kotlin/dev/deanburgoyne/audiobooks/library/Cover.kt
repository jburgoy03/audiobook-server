package dev.deanburgoyne.audiobooks.library

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.BlurredEdgeTreatment
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.scale
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ColorFilter
import androidx.compose.ui.graphics.ColorMatrix
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.min
import coil3.compose.AsyncImage
import dev.deanburgoyne.audiobooks.ui.Palette
import dev.deanburgoyne.audiobooks.ui.ribbonShape

private val Darken = ColorFilter.colorMatrix(ColorMatrix().apply { setToScale(0.55f, 0.55f, 0.55f, 1f) })

/**
 * Covers arrive in any shape (a 175 px square, a photo of a hardback), so every one
 * sits in a square frame: the image fits inside, uncropped, and a blurred, darkened
 * copy of itself fills the rest. A book with no art gets a cloth board with its title.
 * On black a cover needs an edge more than a shadow: a hairline keyline.
 *
 * [progress] (0–1) marks a started book: the red ribbon hanging from the top, slightly
 * in from the right, and a red line along the bottom. Red only ever means "where you
 * are in a book".
 */
@Composable
fun Cover(url: String?, title: String, modifier: Modifier = Modifier, progress: Float? = null) {
    val shape = RoundedCornerShape(2.dp)
    BoxWithConstraints(
        modifier
            .aspectRatio(1f)
            .clip(shape)
            .background(Palette.Cloth),
    ) {
        Text(
            title,
            style = MaterialTheme.typography.bodyLarge.copy(fontStyle = FontStyle.Italic, lineHeight = MaterialTheme.typography.bodyLarge.fontSize * 1.2f),
            textAlign = TextAlign.Center,
            maxLines = 5,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.align(Alignment.Center).padding(maxWidth * 0.14f),
        )
        if (url != null) {
            AsyncImage(
                model = url,
                contentDescription = null,
                contentScale = ContentScale.Crop,
                colorFilter = Darken,
                // Oversized like the web's inset: -12%, so the blur's soft edges fall
                // outside the frame instead of fading in from its sides.
                modifier = Modifier.fillMaxSize().scale(1.24f).blur(22.dp, BlurredEdgeTreatment.Unbounded),
            )
            AsyncImage(
                model = url,
                contentDescription = null, // the title is always set beside or under a cover
                contentScale = ContentScale.Fit,
                modifier = Modifier.fillMaxSize(),
            )
        }

        if (progress != null && progress > 0f) {
            val side = min(maxWidth, maxHeight)
            Box(
                Modifier
                    .align(Alignment.TopEnd)
                    .padding(end = side * 0.12f)
                    .width(min(side * 0.10f, 16.dp))
                    .height(side * 0.28f)
                    .background(Palette.Ribbon, ribbonShape()),
            )
            Box(Modifier.align(Alignment.BottomStart).fillMaxWidth().height(3.dp).background(Color.Black.copy(alpha = 0.55f))) {
                Box(Modifier.fillMaxWidth(progress.coerceIn(0f, 1f)).fillMaxHeight().background(Palette.Ribbon))
            }
        }

        Box(Modifier.fillMaxSize().border(1.dp, Color.White.copy(alpha = 0.09f), shape))
    }
}
