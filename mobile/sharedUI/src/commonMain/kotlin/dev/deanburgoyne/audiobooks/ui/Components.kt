package dev.deanburgoyne.audiobooks.ui

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.GenericShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.scale
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import dev.deanburgoyne.audiobooks.api.Chapter

/** The ribbon: a strip hanging from a top edge, with a notched tail (the app's mark). */
fun ribbonShape(notch: Float = 0.2f) = GenericShape { size, _ ->
    moveTo(0f, 0f)
    lineTo(size.width, 0f)
    lineTo(size.width, size.height)
    lineTo(size.width / 2, size.height * (1 - notch))
    lineTo(0f, size.height)
    close()
}

/** Every press answers at once: down fast, back slower (the web's --press / --release). */
@Composable
private fun pressScale(source: MutableInteractionSource, pressed: Float = 0.95f): Float {
    val isPressed by source.collectIsPressedAsState()
    return animateFloatAsState(if (isPressed) pressed else 1f, label = "press").value
}

/** The bone pill: "Resume", "Sign in", "Jump there". The one solid button style. */
@Composable
fun PillButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    leading: (@Composable () -> Unit)? = null,
) {
    val source = remember { MutableInteractionSource() }
    Row(
        modifier
            .scale(pressScale(source))
            .alpha(if (enabled) 1f else 0.6f)
            .clip(CircleShape)
            .background(Palette.Ink)
            .clickable(source, indication = null, enabled = enabled, onClick = onClick)
            .padding(PaddingValues(start = if (leading != null) 18.dp else 23.dp, end = 23.dp, top = 10.dp, bottom = 10.dp)),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(9.dp),
    ) {
        leading?.invoke()
        Text(text, style = MaterialTheme.typography.labelLarge, color = Palette.Page)
    }
}

/** The quiet pill: secondary actions ("Stay here", "Remove"). Outlined, or bare. */
@Composable
fun QuietButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, outlined: Boolean = false, enabled: Boolean = true) {
    val source = remember { MutableInteractionSource() }
    Box(
        modifier
            .scale(pressScale(source))
            .alpha(if (enabled) 1f else 0.5f)
            .clip(CircleShape)
            .then(if (outlined) Modifier.border(1.dp, Palette.Rule, CircleShape) else Modifier)
            .clickable(source, indication = null, enabled = enabled, onClick = onClick)
            .padding(horizontal = 15.dp, vertical = 6.dp),
    ) {
        Text(text, style = MaterialTheme.typography.bodySmall, color = if (outlined) Palette.Ink else Palette.Muted)
    }
}

/** A round transport control; [solid] is the bone play button. */
@Composable
fun ControlButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    size: Dp = 48.dp,
    solid: Boolean = false,
    enabled: Boolean = true,
    content: @Composable () -> Unit,
) {
    val source = remember { MutableInteractionSource() }
    val pressed by source.collectIsPressedAsState()
    Box(
        modifier
            .scale(pressScale(source, if (solid) 0.95f else 0.9f))
            .alpha(if (enabled) 1f else 0.35f)
            .clip(CircleShape)
            .background(if (solid) Palette.Ink else if (pressed) Palette.PressTint else Color.Transparent)
            .clickable(source, indication = null, enabled = enabled, onClick = onClick)
            .size(size),
        contentAlignment = Alignment.Center,
    ) { content() }
}

/**
 * A section heading: italic title on the left edge, an optional count on the right,
 * and a rule the full width, so a sparse section still shows where the frame ends.
 */
@Composable
fun SectionHead(title: String, modifier: Modifier = Modifier, count: String? = null) {
    Column(modifier.fillMaxWidth()) {
        Row(
            Modifier.fillMaxWidth().padding(bottom = 10.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.Bottom,
        ) {
            Text(title, style = MaterialTheme.typography.titleLarge)
            if (count != null) Text(count, style = MaterialTheme.typography.bodyMedium, color = Palette.Muted)
        }
        HorizontalDivider(color = Palette.Rule, thickness = 1.dp)
    }
}

/** The sign-in fields: no box, a rule underneath that turns bone while focused. */
@Composable
fun UnderlineField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    modifier: Modifier = Modifier,
    placeholder: String? = null,
    keyboardOptions: KeyboardOptions = KeyboardOptions.Default,
    keyboardActions: KeyboardActions = KeyboardActions.Default,
    visualTransformation: VisualTransformation = VisualTransformation.None,
) {
    var focused by remember { mutableStateOf(false) }
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(label, style = MaterialTheme.typography.bodySmall, color = Palette.Muted)
        BasicTextField(
            value = value,
            onValueChange = onValueChange,
            singleLine = true,
            textStyle = MaterialTheme.typography.bodyLarge.copy(fontSize = MaterialTheme.typography.titleLarge.fontSize, color = Palette.Ink),
            cursorBrush = SolidColor(Palette.Ink),
            keyboardOptions = keyboardOptions,
            keyboardActions = keyboardActions,
            visualTransformation = visualTransformation,
            modifier = Modifier
                .fillMaxWidth()
                .onFocusChanged { focused = it.isFocused }
                .drawBehind {
                    drawLine(
                        if (focused) Palette.Ink else Palette.Rule,
                        Offset(0f, size.height + 6.dp.toPx()),
                        Offset(size.width, size.height + 6.dp.toPx()),
                        strokeWidth = 1.dp.toPx(),
                    )
                }
                .padding(top = 4.dp),
            decorationBox = { inner ->
                Box {
                    if (value.isEmpty() && placeholder != null)
                        Text(placeholder, style = MaterialTheme.typography.titleLarge.copy(fontStyle = androidx.compose.ui.text.font.FontStyle.Normal), color = Palette.Faint)
                    inner()
                }
            },
        )
        Box(Modifier.height(6.dp))
    }
}

/**
 * The timeline: a thin bar of chapter segments laid end to end, read like a ruler,
 * with the current chapter's played part in red and a glowing red playhead. The
 * web's design; [interactive] makes it draggable (seek on release).
 *
 * [start]..[end] is the stretch shown: the whole book, or one chapter for a fine
 * seek. Chapters outside it are clipped away.
 */
@Composable
fun Timeline(
    position: Double,
    start: Double,
    end: Double,
    chapters: List<Chapter>,
    modifier: Modifier = Modifier,
    interactive: Boolean = false,
    onSeek: (Double) -> Unit = {},
    onDrag: (Double?) -> Unit = {},
) {
    val span = (end - start).coerceAtLeast(1.0)
    var drag by remember { mutableStateOf<Float?>(null) }
    val shown = drag?.let { start + it * span } ?: position
    val height = if (interactive) 40.dp else 14.dp
    val barInset = if (interactive) (if (drag != null) 14.dp else 17.dp) else 5.dp

    Box(
        modifier
            .fillMaxWidth()
            .height(height)
            .then(
                if (!interactive) Modifier
                else Modifier
                    .pointerInput(start, end) {
                        detectTapGestures { offset -> onSeek(start + (offset.x / size.width).coerceIn(0f, 1f) * span) }
                    }
                    .pointerInput(start, end) {
                        detectHorizontalDragGestures(
                            onDragStart = { o -> drag = (o.x / size.width).coerceIn(0f, 1f); onDrag(start + drag!! * span) },
                            onHorizontalDrag = { change, _ ->
                                drag = (change.position.x / size.width).coerceIn(0f, 1f)
                                onDrag(start + drag!! * span)
                            },
                            onDragEnd = { drag?.let { onSeek(start + it * span) }; drag = null; onDrag(null) },
                            onDragCancel = { drag = null; onDrag(null) },
                        )
                    }
            )
            .drawBehind {
                val top = barInset.toPx()
                val barHeight = size.height - 2 * top
                val radius = CornerRadius(3.dp.toPx())
                fun x(t: Double) = (((t - start) / span).coerceIn(0.0, 1.0) * size.width).toFloat()

                // The whole stretch unplayed, then each chapter's played part.
                drawRoundRect(Palette.Unplayed, Offset(0f, top), Size(size.width, barHeight), radius)
                val segments = chapters.filter { it.endOffsetSeconds > start && it.startOffsetSeconds < end }
                    .ifEmpty { listOf(Chapter(0, "", start, end)) }
                for (c in segments) {
                    val left = x(c.startOffsetSeconds)
                    val right = x(c.endOffsetSeconds)
                    val played = x(minOf(shown, c.endOffsetSeconds))
                    if (played > left) {
                        val current = shown >= c.startOffsetSeconds && shown < c.endOffsetSeconds
                        drawRect(if (current) Palette.Ribbon else Palette.Played, Offset(left, top), Size(played - left, barHeight))
                    }
                    // The divider between chapters, over the fill, so structure stays visible.
                    if (right < size.width - 0.5f) drawRect(Palette.Page, Offset(right - 1f, top), Size(1.dp.toPx(), barHeight))
                }

                // The playhead: a short red upright with a soft glow.
                val head = x(shown)
                val inset = if (interactive) 9.dp.toPx() else 1.dp.toPx()
                drawRoundRect(
                    Palette.Ribbon.copy(alpha = 0.25f),
                    Offset(head - 4.dp.toPx(), inset - 2.dp.toPx()),
                    Size(8.dp.toPx(), size.height - 2 * inset + 4.dp.toPx()),
                    CornerRadius(4.dp.toPx()),
                )
                drawRoundRect(
                    Palette.Ribbon,
                    Offset(head - 1.5.dp.toPx(), inset),
                    Size(3.dp.toPx(), size.height - 2 * inset),
                    CornerRadius(2.dp.toPx()),
                )
            },
    )
}

/** A plain progress line (downloads): bone, not red, since red means a position in a book. */
@Composable
fun ProgressLine(fraction: Float, modifier: Modifier = Modifier) {
    Box(
        modifier.fillMaxWidth().height(3.dp).clip(RoundedCornerShape(2.dp)).background(Palette.Unplayed),
    ) {
        Box(Modifier.fillMaxWidth(fraction.coerceIn(0f, 1f)).height(3.dp).background(Palette.Played))
    }
}

/** Rows like the web's chapter list: rules edge to edge, the whole row pressable. */
@Composable
fun RuledRow(onClick: (() -> Unit)?, modifier: Modifier = Modifier, content: @Composable RowScope.() -> Unit) {
    val source = remember { MutableInteractionSource() }
    val pressed by source.collectIsPressedAsState()
    Column(modifier.fillMaxWidth()) {
        Row(
            Modifier
                .fillMaxWidth()
                .background(if (pressed) Palette.PressTint else Color.Transparent)
                .then(if (onClick != null) Modifier.clickable(source, indication = null, onClick = onClick) else Modifier)
                .padding(horizontal = 12.dp, vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
            content = content,
        )
        HorizontalDivider(color = Palette.Rule, thickness = 1.dp)
    }
}

/**
 * The masthead: the wordmark on the left with the app's mark, a red ribbon hanging
 * from the top edge of the screen like a bookmark out of a book, and [end] on the
 * right. A rule underneath, as on every web page.
 */
@Composable
fun Masthead(modifier: Modifier = Modifier, end: @Composable () -> Unit = {}) {
    Column(modifier.fillMaxWidth()) {
        Row(
            Modifier.fillMaxWidth().padding(start = 20.dp, end = 20.dp, top = 18.dp, bottom = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            // The ribbon is drawn, not laid out: it hangs up through the status bar to
            // the top of the screen without making the masthead any taller.
            Text(
                "Audiobooks",
                style = MaterialTheme.typography.titleSmall,
                modifier = Modifier
                    .drawBehind {
                        val width = 14.dp.toPx()
                        val bottom = size.height + 5.dp.toPx() // just below the baseline
                        val top = -400.dp.toPx() // well past the top of any screen
                        val notch = 7.dp.toPx()
                        val path = androidx.compose.ui.graphics.Path().apply {
                            moveTo(0f, top)
                            lineTo(width, top)
                            lineTo(width, bottom)
                            lineTo(width / 2, bottom - notch)
                            lineTo(0f, bottom)
                            close()
                        }
                        drawPath(path, Palette.Ribbon)
                    }
                    .padding(start = 26.dp),
            )
            end()
        }
        HorizontalDivider(color = Palette.Rule, thickness = 1.dp)
    }
}
