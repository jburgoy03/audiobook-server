package dev.deanburgoyne.audiobooks.session

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import dev.deanburgoyne.audiobooks.ui.Masthead
import dev.deanburgoyne.audiobooks.ui.Palette
import dev.deanburgoyne.audiobooks.ui.PillButton
import dev.deanburgoyne.audiobooks.ui.UnderlineField
import dev.deanburgoyne.audiobooks.ui.secondaryItalic

/**
 * The web's sign-in page: one narrow column on the left edge, like a title page. An
 * italic title, a line of explanation, fields that are only a rule, a bone pill.
 */
@Composable
private fun TitlePage(title: String, lead: String? = null, content: @Composable ColumnScope.() -> Unit) {
    // The masthead outside the scrolling part: scroll containers clip, and the ribbon
    // hangs up past the top of the content.
    Column(Modifier.fillMaxSize().safeDrawingPadding()) {
        Masthead()
        Column(
            Modifier.verticalScroll(rememberScrollState()).padding(horizontal = 20.dp).padding(top = 48.dp, bottom = 32.dp).widthIn(max = 420.dp),
            verticalArrangement = Arrangement.spacedBy(0.dp),
        ) {
            Text(title, style = MaterialTheme.typography.headlineMedium)
            Spacer(Modifier.height(12.dp))
            if (lead != null) Text(lead, style = MaterialTheme.typography.bodyMedium, color = Palette.Muted)
            Spacer(Modifier.height(32.dp))
            Column(verticalArrangement = Arrangement.spacedBy(22.dp), content = content)
        }
    }
}

/** Errors are bone, in italic, as on the web: red only ever means a position in a book. */
@Composable
private fun ErrorText(error: String?) {
    if (error != null) Text(error, style = MaterialTheme.typography.bodyMedium.copy(fontStyle = FontStyle.Italic), color = Palette.Ink)
}

@Composable
private fun TextLink(text: String, onClick: () -> Unit) {
    Text(
        text,
        style = secondaryItalic.copy(textDecoration = TextDecoration.Underline),
        modifier = Modifier.clickable(onClick = onClick).padding(vertical = 6.dp),
    )
}

@Composable
private fun Submit(label: String, busy: Boolean, enabled: Boolean, onClick: () -> Unit) {
    PillButton(if (busy) "One moment…" else label, onClick, enabled = enabled && !busy)
}

@Composable
fun StartingScreen() {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        CircularProgressIndicator(color = Palette.Muted, strokeWidth = 2.dp)
    }
}

@Composable
fun UnreachableScreen(screen: SessionScreen.Unreachable, onRetry: () -> Unit, onChangeServer: () -> Unit) =
    TitlePage("Can't reach your server", screen.serverUrl) {
        ErrorText(screen.message)
        PillButton("Try again", onRetry)
        TextLink("Use a different server", onChangeServer)
    }

@Composable
fun ChooseServerScreen(screen: SessionScreen.ChooseServer, onSubmit: (String) -> Unit) {
    var address by rememberSaveable { mutableStateOf(screen.initial) }
    val submit = { onSubmit(address) }

    TitlePage("Your library", "The address you use for AudiobookServer in a browser.") {
        UnderlineField(
            value = address,
            onValueChange = { address = it },
            label = "Server address",
            placeholder = "audiobooks.example.com",
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Go),
            keyboardActions = KeyboardActions(onGo = { submit() }),
        )
        ErrorText(screen.error)
        Submit("Continue", screen.busy, enabled = address.isNotBlank(), onClick = submit)
    }
}

@Composable
fun SignInScreen(screen: SessionScreen.SignIn, onSubmit: (String, String) -> Unit, onChangeServer: () -> Unit) {
    var username by rememberSaveable { mutableStateOf(screen.username) }
    // Deliberately not rememberSaveable: a passphrase shouldn't be written into saved state.
    var password by remember { mutableStateOf("") }
    val submit = { onSubmit(username, password) }

    TitlePage("Sign in", screen.serverUrl) {
        UnderlineField(
            value = username,
            onValueChange = { username = it },
            label = "Username",
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
        )
        UnderlineField(
            value = password,
            onValueChange = { password = it },
            label = "Passphrase",
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
            keyboardActions = KeyboardActions(onDone = { submit() }),
        )
        ErrorText(screen.error)
        Submit("Sign in", screen.busy, enabled = username.isNotBlank() && password.isNotEmpty(), onClick = submit)
        TextLink("Use a different server", onChangeServer)
    }
}

@Composable
fun ChangePasswordScreen(
    screen: SessionScreen.ChangePassword,
    onSubmit: (current: String, new: String, confirm: String) -> Unit,
    onSignOut: () -> Unit,
) {
    var current by remember { mutableStateOf("") }
    var new by remember { mutableStateOf("") }
    var confirm by remember { mutableStateOf("") }
    val submit = { onSubmit(current, new, confirm) }
    val password = PasswordVisualTransformation()
    val next = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Next)

    TitlePage(
        "A passphrase of your own",
        "${screen.username}, an admin set your passphrase. Choose your own to continue: at least 12 characters.",
    ) {
        UnderlineField(current, { current = it }, "Current passphrase", visualTransformation = password, keyboardOptions = next)
        UnderlineField(new, { new = it }, "New passphrase", visualTransformation = password, keyboardOptions = next)
        UnderlineField(
            confirm, { confirm = it }, "New passphrase again",
            visualTransformation = password,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
            keyboardActions = KeyboardActions(onDone = { submit() }),
        )
        ErrorText(screen.error)
        Submit("Change passphrase", screen.busy, enabled = current.isNotEmpty() && new.isNotEmpty(), onClick = submit)
        TextLink("Sign out", onSignOut)
    }
}
