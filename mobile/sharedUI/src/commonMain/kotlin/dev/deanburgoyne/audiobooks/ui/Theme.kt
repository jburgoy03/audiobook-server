package dev.deanburgoyne.audiobooks.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.em
import androidx.compose.ui.unit.sp
import dev.deanburgoyne.audiobooks.resources.Res
import dev.deanburgoyne.audiobooks.resources.eb_garamond_400
import dev.deanburgoyne.audiobooks.resources.eb_garamond_400_italic
import dev.deanburgoyne.audiobooks.resources.eb_garamond_500
import dev.deanburgoyne.audiobooks.resources.eb_garamond_600
import org.jetbrains.compose.resources.Font

/*
  Black, bone and blood: the web client's palette (web/src/index.css), token for
  token. A true black page, bone-white type in EB Garamond, the covers as the only
  colour. One accent, blood red, keeps a single meaning, where you are in a book:
  the ribbon on started books, the current chapter, the playhead. Nothing else is
  red; errors are bone, in italic, as on the web. Dark only, by design.
*/
object Palette {
    val Page = Color(0xFF000000)
    val Ink = Color(0xFFEBE6DC)
    val Muted = Color(0xFF8F897E)
    val Faint = Color(0xFF5B5750)
    val Rule = Ink.copy(alpha = 0.12f)
    val Raised = Ink.copy(alpha = 0.06f)
    val Ribbon = Color(0xFFC8323E)
    val Played = Ink.copy(alpha = 0.82f)
    val Unplayed = Ink.copy(alpha = 0.14f)
    val PressTint = Ink.copy(alpha = 0.13f)
    /** A book with no art: its title on a near-black board. */
    val Cloth = Color(0xFF121110)
    /** The now-playing bar: smoked glass. */
    val Glass = Color(0xF20A0A09)
}

@Composable
fun garamond() = FontFamily(
    Font(Res.font.eb_garamond_400, FontWeight.Normal),
    Font(Res.font.eb_garamond_500, FontWeight.Medium),
    Font(Res.font.eb_garamond_600, FontWeight.SemiBold),
    Font(Res.font.eb_garamond_400_italic, FontWeight.Normal, FontStyle.Italic),
)

/**
 * The web's type scale in sp. Its base is 17px (Garamond runs small), so 1rem = 17sp:
 * book titles 1.1875rem, times 0.9375rem, section heads 1.5rem italic, and so on.
 */
@Composable
fun AudiobooksTheme(content: @Composable () -> Unit) {
    val g = garamond()
    fun style(size: Float, weight: FontWeight = FontWeight.Normal, italic: Boolean = false, line: Float = 1.5f, tracking: Float = 0f) =
        TextStyle(
            fontFamily = g,
            fontSize = size.sp,
            fontWeight = weight,
            fontStyle = if (italic) FontStyle.Italic else FontStyle.Normal,
            lineHeight = (size * line).sp,
            letterSpacing = tracking.em,
            // No colour here: it comes from where the text sits (LocalContentColor),
            // so a bone button's label can be black.
        )

    val typography = Typography(
        displaySmall = style(36f, FontWeight.Medium, line = 1.02f, tracking = -0.015f),   // featured title
        headlineLarge = style(32f, FontWeight.Medium, line = 1.05f, tracking = -0.015f),  // book page title
        headlineMedium = style(38f, italic = true, line = 1.1f),                          // sign-in title
        headlineSmall = style(30f, FontWeight.Medium, line = 1.15f, tracking = -0.01f),  // player chapter
        titleLarge = style(25.5f, italic = true, line = 1.3f),                            // section heads
        titleMedium = style(20f, FontWeight.Medium, line = 1.2f),                          // book titles
        titleSmall = style(23.5f, FontWeight.Medium, line = 1f, tracking = 0.01f),        // wordmark
        bodyLarge = style(18f),                                                            // blurb, chapters
        bodyMedium = style(17f),
        bodySmall = style(16f),                                                            // times, small print
        labelLarge = style(18f, FontWeight.SemiBold),                                      // pill buttons
        labelMedium = style(16f),
        labelSmall = style(14f),
    )

    MaterialTheme(
        colorScheme = darkColorScheme(
            background = Palette.Page,
            onBackground = Palette.Ink,
            surface = Palette.Page,
            onSurface = Palette.Ink,
            surfaceVariant = Palette.Cloth,
            onSurfaceVariant = Palette.Muted,
            surfaceContainer = Color(0xFF0D0D0C),
            surfaceContainerHigh = Color(0xFF0D0D0C),
            primary = Palette.Ink,
            onPrimary = Palette.Page,
            secondary = Palette.Muted,
            secondaryContainer = Palette.Raised,
            onSecondaryContainer = Palette.Ink,
            outline = Palette.Rule,
            outlineVariant = Palette.Rule,
            error = Palette.Ink,
        ),
        typography = typography,
        content = content,
    )
}

/** Muted italic: authors, "Back", status lines; the web's .muted with font-style italic. */
val secondaryItalic: TextStyle
    @Composable get() = MaterialTheme.typography.bodyMedium.copy(color = Palette.Muted, fontStyle = FontStyle.Italic)
