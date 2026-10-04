package dev.deanburgoyne.audiobooks.ui

import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.withStyle

/**
 * Blurbs from Open Library are Markdown written by volunteers, and the server's
 * cleaning keeps their emphasis: "*The Name of the Wind*". Shown as italic, which is
 * what it means (and right for book titles); the same rule as the web's Blurb.
 *
 * Only single *…* or _…_ hugging their text, so "5 * 3" or snake_case stay as they are.
 */
private val Emphasis = Regex("""(?<![\w*_])([*_])(?=\S)(.+?)(?<=\S)\1(?![\w*_])""")

fun emphasised(text: String): AnnotatedString = buildAnnotatedString {
    var at = 0
    for (match in Emphasis.findAll(text)) {
        append(text, at, match.range.first)
        withStyle(SpanStyle(fontStyle = FontStyle.Italic)) { append(match.groupValues[2]) }
        at = match.range.last + 1
    }
    append(text, at, text.length)
}
