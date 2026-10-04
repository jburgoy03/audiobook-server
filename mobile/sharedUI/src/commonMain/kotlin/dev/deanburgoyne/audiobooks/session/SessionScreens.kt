package dev.deanburgoyne.audiobooks.session

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp

/** A centred, scrollable column of fixed reading width: every screen here is a short form. */
@Composable
private fun Form(title: String, subtitle: String? = null, content: @Composable ColumnScope.() -> Unit) {
    Box(Modifier.fillMaxSize().safeDrawingPadding(), contentAlignment = Alignment.Center) {
        Column(
            Modifier.widthIn(max = 420.dp).fillMaxWidth().verticalScroll(rememberScrollState()).padding(24.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text(title, style = MaterialTheme.typography.headlineMedium)
            if (subtitle != null)
                Text(subtitle, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            content()
        }
    }
}

@Composable
private fun ErrorText(error: String?) {
    if (error != null) Text(error, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium)
}

@Composable
private fun SubmitButton(label: String, busy: Boolean, enabled: Boolean = true, onClick: () -> Unit) {
    Button(onClick = onClick, enabled = enabled && !busy, modifier = Modifier.fillMaxWidth()) {
        if (busy) CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp) else Text(label)
    }
}

@Composable
private fun PassphraseField(value: String, onChange: (String) -> Unit, label: String, last: Boolean, onDone: () -> Unit = {}) {
    OutlinedTextField(
        value = value,
        onValueChange = onChange,
        label = { Text(label) },
        singleLine = true,
        visualTransformation = PasswordVisualTransformation(),
        keyboardOptions = KeyboardOptions(
            keyboardType = KeyboardType.Password,
            imeAction = if (last) ImeAction.Done else ImeAction.Next,
        ),
        keyboardActions = KeyboardActions(onDone = { onDone() }),
        modifier = Modifier.fillMaxWidth(),
    )
}

@Composable
fun StartingScreen() {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
}

@Composable
fun UnreachableScreen(screen: SessionScreen.Unreachable, onRetry: () -> Unit, onChangeServer: () -> Unit) =
    Form("Can't reach your server", screen.serverUrl) {
        ErrorText(screen.message)
        SubmitButton("Try again", busy = false, onClick = onRetry)
        TextButton(onClick = onChangeServer) { Text("Use a different server") }
    }

@Composable
fun ChooseServerScreen(screen: SessionScreen.ChooseServer, onSubmit: (String) -> Unit) {
    var address by rememberSaveable { mutableStateOf(screen.initial) }
    val submit = { onSubmit(address) }

    Form("Connect to your server", "The address you use for AudiobookServer in a browser.") {
        OutlinedTextField(
            value = address,
            onValueChange = { address = it },
            label = { Text("Server address") },
            placeholder = { Text("audiobooks.example.com") },
            singleLine = true,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Go),
            keyboardActions = KeyboardActions(onGo = { submit() }),
            modifier = Modifier.fillMaxWidth(),
        )
        ErrorText(screen.error)
        SubmitButton("Continue", screen.busy, enabled = address.isNotBlank(), onClick = submit)
    }
}

@Composable
fun SignInScreen(screen: SessionScreen.SignIn, onSubmit: (String, String) -> Unit, onChangeServer: () -> Unit) {
    var username by rememberSaveable { mutableStateOf(screen.username) }
    // Deliberately not rememberSaveable: a passphrase shouldn't be written into saved state.
    var password by remember { mutableStateOf("") }
    val submit = { onSubmit(username, password) }

    Form("Sign in", screen.serverUrl) {
        OutlinedTextField(
            value = username,
            onValueChange = { username = it },
            label = { Text("Username") },
            singleLine = true,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next),
            modifier = Modifier.fillMaxWidth(),
        )
        PassphraseField(password, { password = it }, "Passphrase", last = true, onDone = submit)
        ErrorText(screen.error)
        SubmitButton("Sign in", screen.busy, enabled = username.isNotBlank() && password.isNotEmpty(), onClick = submit)
        TextButton(onClick = onChangeServer) { Text("Use a different server") }
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

    Form(
        "Choose a new passphrase",
        "${screen.username}, your passphrase was set by an admin. Choose your own to continue: at least 12 characters.",
    ) {
        PassphraseField(current, { current = it }, "Current passphrase", last = false)
        PassphraseField(new, { new = it }, "New passphrase", last = false)
        PassphraseField(confirm, { confirm = it }, "New passphrase again", last = true, onDone = submit)
        ErrorText(screen.error)
        SubmitButton("Change passphrase", screen.busy, enabled = current.isNotEmpty() && new.isNotEmpty(), onClick = submit)
        TextButton(onClick = onSignOut) { Text("Sign out") }
    }
}
