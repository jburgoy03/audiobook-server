package dev.deanburgoyne.audiobooks.ui

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.path
import androidx.compose.ui.unit.dp

/**
 * The handful of player icons, drawn here as plain shapes on a 24-unit grid rather
 * than pulled in from Material's icon library, which Google no longer updates. The
 * fill colour is replaced by Icon's tint.
 */
object PlayerIcons {
    val Play = icon("Play") {
        moveTo(8f, 5f); lineTo(19f, 12f); lineTo(8f, 19f); close()
    }

    val Pause = icon("Pause") {
        moveTo(6f, 5f); lineTo(10f, 5f); lineTo(10f, 19f); lineTo(6f, 19f); close()
        moveTo(14f, 5f); lineTo(18f, 5f); lineTo(18f, 19f); lineTo(14f, 19f); close()
    }

    val PreviousChapter = icon("Previous chapter") {
        moveTo(6f, 6f); lineTo(8f, 6f); lineTo(8f, 18f); lineTo(6f, 18f); close()
        moveTo(18f, 6f); lineTo(9.5f, 12f); lineTo(18f, 18f); close()
    }

    val NextChapter = icon("Next chapter") {
        moveTo(6f, 6f); lineTo(14.5f, 12f); lineTo(6f, 18f); close()
        moveTo(16f, 6f); lineTo(18f, 6f); lineTo(18f, 18f); lineTo(16f, 18f); close()
    }

    /** A downward chevron: "close the player", as it slid up. */
    val Collapse = icon("Collapse") {
        moveTo(7.4f, 8.6f); lineTo(12f, 13.2f); lineTo(16.6f, 8.6f); lineTo(18f, 10f)
        lineTo(12f, 16f); lineTo(6f, 10f); close()
    }

    private fun icon(name: String, block: androidx.compose.ui.graphics.vector.PathBuilder.() -> Unit) =
        ImageVector.Builder(name, 24.dp, 24.dp, 24f, 24f)
            .path(fill = SolidColor(Color.Black), pathBuilder = block)
            .build()
}
