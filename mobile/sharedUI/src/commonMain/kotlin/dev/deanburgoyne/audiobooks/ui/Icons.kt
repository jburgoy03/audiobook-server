package dev.deanburgoyne.audiobooks.ui

import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.StartOffset
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Icon
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.PathBuilder
import androidx.compose.ui.graphics.vector.path
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * The web client's icon set (web/src/components/Icons.tsx), path for path: one
 * 24-unit grid, one stroke weight (1.6), round caps and joins. Filled shapes (play,
 * pause, the chapter-skip triangles) are also stroked, which rounds their corners
 * to match the strokes. Colours here are placeholders: Icon's tint replaces them.
 */
object PlayerIcons {
    val Play = icon("Play", filled = true) {
        moveTo(7.5f, 5.5f); lineTo(12.75f, 8.75f); lineTo(12.75f, 15.25f); lineTo(7.5f, 18.5f); close()
        moveTo(12.75f, 8.75f); lineTo(18f, 12f); lineTo(12.75f, 15.25f); close()
    }

    val Pause = icon("Pause", filled = true) {
        moveTo(7f, 5.5f); lineTo(10.25f, 5.5f); lineTo(10.25f, 18.5f); lineTo(7f, 18.5f); close()
        moveTo(13.75f, 5.5f); lineTo(17f, 5.5f); lineTo(17f, 18.5f); lineTo(13.75f, 18.5f); close()
    }

    val PreviousChapter = ImageVector.Builder("Previous chapter", 24.dp, 24.dp, 24f, 24f)
        .stroked { moveTo(6.5f, 6f); lineTo(6.5f, 18f) }
        .stroked(filled = true) { moveTo(17.5f, 6.5f); lineTo(9.75f, 12f); lineTo(17.5f, 17.5f); close() }
        .build()

    val NextChapter = ImageVector.Builder("Next chapter", 24.dp, 24.dp, 24f, 24f)
        .stroked { moveTo(17.5f, 6f); lineTo(17.5f, 18f) }
        .stroked(filled = true) { moveTo(6.5f, 6.5f); lineTo(14.25f, 12f); lineTo(6.5f, 17.5f); close() }
        .build()

    /** The back chevron: "‹ Library". */
    val Back = icon("Back") { moveTo(14.5f, 6f); lineTo(8.5f, 12f); lineTo(14.5f, 18f) }

    /** A downward chevron: close the player, which slid up over the library. */
    val Collapse = icon("Collapse") { moveTo(6f, 9.5f); lineTo(12f, 15.5f); lineTo(18f, 9.5f) }

    /** The open arc of the skip buttons (the "30" is set in type, see SkipIcon). */
    internal val SkipArc = ImageVector.Builder("Skip", 24.dp, 24.dp, 24f, 24f)
        .stroked {
            moveTo(5.2f, 8.2f)
            arcTo(8f, 8f, 0f, isMoreThanHalf = true, isPositiveArc = true, x1 = 4f, y1 = 12.5f)
        }
        .stroked { moveTo(4.6f, 4.4f); lineTo(5.2f, 8.2f); lineTo(9f, 7.6f) }
        .build()

    private fun icon(name: String, filled: Boolean = false, block: PathBuilder.() -> Unit) =
        ImageVector.Builder(name, 24.dp, 24.dp, 24f, 24f).stroked(filled, block).build()

    private fun ImageVector.Builder.stroked(filled: Boolean = false, block: PathBuilder.() -> Unit) = path(
        fill = if (filled) SolidColor(Color.Black) else null,
        stroke = SolidColor(Color.Black),
        strokeLineWidth = 1.6f,
        strokeLineCap = StrokeCap.Round,
        strokeLineJoin = StrokeJoin.Round,
        pathBuilder = block,
    )
}

/**
 * Skip back or forward 30 s: the open arc with its arrowhead, mirrored for forward,
 * and the amount set inside it in the book face so the numerals match the type.
 */
@Composable
fun SkipIcon(forward: Boolean, size: Dp = 30.dp, tint: Color = LocalContentColor.current) {
    Box(Modifier.size(size), contentAlignment = Alignment.Center) {
        Icon(
            PlayerIcons.SkipArc,
            contentDescription = if (forward) "Forward 30 seconds" else "Back 30 seconds",
            tint = tint,
            modifier = Modifier.size(size).then(if (forward) Modifier.mirrored() else Modifier),
        )
        Text(
            "30",
            style = MaterialTheme.typography.labelSmall.copy(fontWeight = FontWeight.SemiBold, fontSize = (size.value * 0.3f).sp),
            color = tint,
            modifier = Modifier.offset(x = (size * 0.017f), y = (size * 0.06f)),
        )
    }
}

/** Three small bars that rise and fall while audio plays, beside the current chapter. */
@Composable
fun LevelMeter(active: Boolean, color: Color = Palette.Ribbon) {
    val levels = if (active) animatedLevels() else listOf(8f / 12, 11f / 12, 6f / 12)
    Canvas(Modifier.size(12.dp)) {
        val bar = size.width / 6
        levels.forEachIndexed { i, level ->
            val h = size.height * level
            drawRoundRect(
                color = color,
                topLeft = Offset(bar * 0.5f + i * bar * 2, size.height - h),
                size = Size(bar, h),
                cornerRadius = CornerRadius(bar / 2),
            )
        }
    }
}

@Composable
private fun animatedLevels(): List<Float> {
    val transition = rememberInfiniteTransition()
    return listOf(900, 700, 1100).mapIndexed { i, ms ->
        val v by transition.animateFloat(
            initialValue = 0.35f,
            targetValue = 1f,
            animationSpec = infiniteRepeatable(
                tween(ms, easing = FastOutSlowInEasing),
                RepeatMode.Reverse,
                initialStartOffset = StartOffset(i * 300),
            ),
        )
        v * listOf(8f, 11f, 6f)[i] / 12f
    }
}

private fun Modifier.mirrored() = this.graphicsLayer(scaleX = -1f)
