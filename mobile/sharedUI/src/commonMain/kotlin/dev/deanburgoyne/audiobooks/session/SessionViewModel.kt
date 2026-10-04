package dev.deanburgoyne.audiobooks.session

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dev.deanburgoyne.audiobooks.api.ApiException
import dev.deanburgoyne.audiobooks.api.AudiobookApi
import dev.deanburgoyne.audiobooks.api.CurrentUser
import dev.deanburgoyne.audiobooks.api.ProbeResult
import dev.deanburgoyne.audiobooks.api.SessionExpiredException
import dev.deanburgoyne.audiobooks.api.isLocalNetworkAddress
import dev.deanburgoyne.audiobooks.api.normalizeServerUrl
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** Where the app is between launch and the library. */
sealed interface SessionScreen {
    data object Starting : SessionScreen

    /** Couldn't reach the remembered server on launch. Not signed out: just retry. */
    data class Unreachable(val serverUrl: String, val message: String) : SessionScreen

    /**
     * [askLocalNetwork]: the address is on a local network and Android needs the user's
     * permission before the app may connect. The UI shows the system prompt and
     * reports back through onLocalNetworkAnswer; held here rather than in the UI so a
     * rotation mid-prompt asks again instead of losing the answer.
     */
    data class ChooseServer(
        val initial: String,
        val error: String? = null,
        val busy: Boolean = false,
        val askLocalNetwork: Boolean = false,
    ) : SessionScreen

    data class SignIn(
        val serverUrl: String,
        val username: String,
        val error: String? = null,
        val busy: Boolean = false,
    ) : SessionScreen

    data class ChangePassword(val username: String, val error: String? = null, val busy: Boolean = false) : SessionScreen

    data class SignedIn(val serverUrl: String, val user: CurrentUser) : SessionScreen
}

/**
 * [localNetworkGranted] only reads the permission (an application-context check), so
 * the view model can outlive the activity without holding on to it. Asking is the UI's
 * job.
 */
class SessionViewModel(
    private val api: AudiobookApi,
    private val localNetworkGranted: () -> Boolean,
) : ViewModel() {
    private val _screen = MutableStateFlow<SessionScreen>(SessionScreen.Starting)
    val screen: StateFlow<SessionScreen> = _screen.asStateFlow()

    init {
        viewModelScope.launch { resume() }
        // A refresh refused mid-session (passphrase changed elsewhere, 30 days unused).
        viewModelScope.launch { api.sessionEnded.collect { toSignIn(error = "Your session has ended. Sign in again.") } }
    }

    /** On launch: straight to the user if a session is kept, otherwise ask. */
    fun resume() {
        val session = api.session
        when {
            session == null -> _screen.value = SessionScreen.ChooseServer(initial = "")
            // Permission withdrawn in Settings since last time: ask before connecting.
            needsLocalNetwork(session.serverUrl) ->
                _screen.value = SessionScreen.ChooseServer(initial = session.serverUrl, askLocalNetwork = true)
            session.tokens == null -> toSignIn()
            else -> {
                _screen.value = SessionScreen.Starting
                viewModelScope.launch {
                    try {
                        route(session.serverUrl, api.me())
                    } catch (e: CancellationException) {
                        throw e
                    } catch (_: SessionExpiredException) {
                        toSignIn()
                    } catch (_: ApiException) {
                        // The server answered, with an error: say so rather than guess.
                        _screen.value = SessionScreen.Unreachable(session.serverUrl, "The server couldn't confirm your session.")
                    } catch (_: Exception) {
                        // No connection. The session is kept, so open the library: it
                        // shows downloaded books and reconnects on refresh. Nothing is
                        // trusted that the server hasn't already granted; every request
                        // is still checked when it's back.
                        _screen.value = SessionScreen.SignedIn(
                            session.serverUrl,
                            CurrentUser(session.username.orEmpty(), isAdmin = false, mustChangePassword = false),
                        )
                    }
                }
            }
        }
    }

    fun chooseServer() {
        _screen.value = SessionScreen.ChooseServer(initial = api.session?.serverUrl.orEmpty())
    }

    fun submitServer(input: String) {
        val url = normalizeServerUrl(input)
        if (url != null && needsLocalNetwork(url)) {
            _screen.value = SessionScreen.ChooseServer(initial = input, askLocalNetwork = true)
            return
        }
        probe(input)
    }

    fun onLocalNetworkAnswer(granted: Boolean) {
        val current = _screen.value as? SessionScreen.ChooseServer ?: return
        if (granted) {
            _screen.value = current.copy(askLocalNetwork = false)
            probe(current.initial)
        } else {
            _screen.value = current.copy(
                askLocalNetwork = false,
                error = "This server is on a local network. Without permission to use the local network, " +
                    "the app can't reach it. You can allow it in the app's settings (Nearby devices).",
            )
        }
    }

    private fun probe(input: String) = busy<SessionScreen.ChooseServer>({ copy(busy = true, error = null) }) {
        val error = when (val result = api.probe(input)) {
            is ProbeResult.Compatible -> {
                val kept = api.session?.takeIf { it.serverUrl == result.serverUrl }
                // The same server with a session still held (back from the permission
                // prompt on launch): carry on as if nothing happened.
                if (kept?.tokens != null) resume()
                else _screen.value = SessionScreen.SignIn(result.serverUrl, kept?.username.orEmpty())
                return@busy
            }
            ProbeResult.InvalidUrl -> "That doesn't look like a server address."
            // The technical reason too: for a self-hoster it's the useful part
            // ("CLEARTEXT not permitted", a certificate error, a refused connection).
            is ProbeResult.Unreachable -> "Couldn't reach that address." + (result.reason?.let { "\n\n$it" } ?: "")
            ProbeResult.NotAudiobookServer -> "Something answered, but it isn't an AudiobookServer."
            is ProbeResult.Incompatible ->
                if (result.serverIsOlder) "This server (${result.info.version}) is too old for this app. Update the server."
                else "This server (${result.info.version}) is newer than this app understands. Update the app."
        }
        _screen.value = SessionScreen.ChooseServer(initial = input, error = error)
    }

    fun signIn(username: String, password: String) {
        val current = _screen.value as? SessionScreen.SignIn ?: return
        busy<SessionScreen.SignIn>({ copy(busy = true, error = null, username = username) }) {
            try {
                route(current.serverUrl, api.signIn(current.serverUrl, username.trim(), password))
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                _screen.value = current.copy(username = username, error = describe(e))
            }
        }
    }

    fun changePassword(currentPassword: String, newPassword: String, confirm: String) {
        val current = _screen.value as? SessionScreen.ChangePassword ?: return
        if (newPassword != confirm) {
            _screen.value = current.copy(error = "The new passphrases don't match.")
            return
        }
        busy<SessionScreen.ChangePassword>({ copy(busy = true, error = null) }) {
            try {
                route(api.session!!.serverUrl, api.changePassword(currentPassword, newPassword))
            } catch (e: CancellationException) {
                throw e
            } catch (_: SessionExpiredException) {
                toSignIn()
            } catch (e: Exception) {
                _screen.value = current.copy(error = describe(e))
            }
        }
    }

    fun signOut() {
        api.signOut()
        toSignIn()
    }

    private fun needsLocalNetwork(serverUrl: String) =
        isLocalNetworkAddress(serverUrl) && !localNetworkGranted()

    private fun route(serverUrl: String, user: CurrentUser) {
        _screen.value =
            if (user.mustChangePassword) SessionScreen.ChangePassword(user.username)
            else SessionScreen.SignedIn(serverUrl, user)
    }

    private fun toSignIn(error: String? = null) {
        val session = api.session ?: return chooseServer()
        _screen.value = SessionScreen.SignIn(session.serverUrl, session.username.orEmpty(), error)
    }

    /** Marks the current screen busy (if it's a [T]) and runs [work]; ignores taps while busy. */
    private inline fun <reified T : SessionScreen> busy(
        crossinline mark: T.() -> SessionScreen,
        crossinline work: suspend () -> Unit,
    ) {
        val screen = _screen.value as? T ?: return
        if (screen.isBusy()) return
        _screen.update { screen.mark() }
        viewModelScope.launch { work() }
    }

    private fun describe(e: Exception): String = when (e) {
        is ApiException -> e.message ?: "The server refused that (${e.status})."
        else -> "Couldn't reach the server. Check your connection."
    }
}

private fun SessionScreen.isBusy() = when (this) {
    is SessionScreen.ChooseServer -> busy
    is SessionScreen.SignIn -> busy
    is SessionScreen.ChangePassword -> busy
    else -> false
}
