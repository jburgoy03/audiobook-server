package dev.deanburgoyne.audiobooks.ui

import androidx.compose.runtime.Composable

/** The system back gesture/button. Android's comes from androidx.activity. */
@Composable
expect fun SystemBackHandler(enabled: Boolean = true, onBack: () -> Unit)
