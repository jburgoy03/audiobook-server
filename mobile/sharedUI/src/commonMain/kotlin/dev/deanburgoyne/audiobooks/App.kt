package dev.deanburgoyne.audiobooks

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.lifecycle.viewmodel.compose.viewModel
import dev.deanburgoyne.audiobooks.api.AudiobookApi
import dev.deanburgoyne.audiobooks.session.ChangePasswordScreen
import dev.deanburgoyne.audiobooks.session.ChooseServerScreen
import dev.deanburgoyne.audiobooks.session.LocalNetworkPermission
import dev.deanburgoyne.audiobooks.session.NoLocalNetworkPermission
import dev.deanburgoyne.audiobooks.session.SessionScreen
import dev.deanburgoyne.audiobooks.session.SessionViewModel
import dev.deanburgoyne.audiobooks.session.SignInScreen
import dev.deanburgoyne.audiobooks.session.SignedInScreen
import dev.deanburgoyne.audiobooks.session.StartingScreen
import dev.deanburgoyne.audiobooks.session.UnreachableScreen

/**
 * The app's root. A plain `when` over the session state stands in for navigation
 * until there are screens to navigate between (the library, a book, the player).
 */
@Composable
fun App(
    api: AudiobookApi,
    localNetwork: LocalNetworkPermission = NoLocalNetworkPermission,
    // Separate from [localNetwork] so the view model, which outlives activities, holds
    // only a check (application context), never the activity's permission launcher.
    localNetworkGranted: () -> Boolean = { true },
) {
    val session = viewModel { SessionViewModel(api, localNetworkGranted) }
    val screen by session.screen.collectAsState()

    // Ask whenever the state says to: after a rotation this simply asks again (the
    // system answers at once if the user already decided).
    val ask = (screen as? SessionScreen.ChooseServer)?.askLocalNetwork == true
    LaunchedEffect(ask) {
        if (ask) localNetwork.request(session::onLocalNetworkAnswer)
    }

    MaterialTheme {
        Surface {
            when (val s = screen) {
                SessionScreen.Starting -> StartingScreen()
                is SessionScreen.Unreachable -> UnreachableScreen(s, onRetry = session::resume, onChangeServer = session::chooseServer)
                is SessionScreen.ChooseServer -> ChooseServerScreen(s, onSubmit = session::submitServer)
                is SessionScreen.SignIn -> SignInScreen(s, onSubmit = session::signIn, onChangeServer = session::chooseServer)
                is SessionScreen.ChangePassword -> ChangePasswordScreen(s, onSubmit = session::changePassword, onSignOut = session::signOut)
                is SessionScreen.SignedIn -> SignedInScreen(s, onSignOut = session::signOut)
            }
        }
    }
}
