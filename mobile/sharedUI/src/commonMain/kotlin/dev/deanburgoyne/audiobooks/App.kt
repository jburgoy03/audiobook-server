package dev.deanburgoyne.audiobooks

import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.lifecycle.viewmodel.compose.viewModel
import coil3.compose.setSingletonImageLoaderFactory
import coil3.ImageLoader
import coil3.network.ktor3.KtorNetworkFetcherFactory
import dev.deanburgoyne.audiobooks.api.AudiobookApi
import dev.deanburgoyne.audiobooks.library.LibraryHost
import dev.deanburgoyne.audiobooks.ui.AudiobooksTheme
import dev.deanburgoyne.audiobooks.playback.BookPlayer
import dev.deanburgoyne.audiobooks.downloads.DownloadSettings
import dev.deanburgoyne.audiobooks.downloads.Downloads
import dev.deanburgoyne.audiobooks.progress.ProgressSync
import dev.deanburgoyne.audiobooks.session.ChangePasswordScreen
import dev.deanburgoyne.audiobooks.session.ChooseServerScreen
import dev.deanburgoyne.audiobooks.session.LocalNetworkPermission
import dev.deanburgoyne.audiobooks.session.NoLocalNetworkPermission
import dev.deanburgoyne.audiobooks.session.SessionScreen
import dev.deanburgoyne.audiobooks.session.SessionViewModel
import dev.deanburgoyne.audiobooks.session.SignInScreen
import dev.deanburgoyne.audiobooks.session.StartingScreen
import dev.deanburgoyne.audiobooks.session.UnreachableScreen

/**
 * The app's root: the session decides between the sign-in flow and the library.
 */
@Composable
fun App(
    api: AudiobookApi,
    player: BookPlayer,
    progress: ProgressSync,
    downloads: Downloads,
    downloadSettings: DownloadSettings,
    localNetwork: LocalNetworkPermission = NoLocalNetworkPermission,
    // Separate from [localNetwork] so the view model, which outlives activities, holds
    // only a check (application context), never the activity's permission launcher.
    localNetworkGranted: () -> Boolean = { true },
) {
    // Covers load through the API's own client, so they carry the bearer token (and
    // refresh it on a 401) like every other request. Coil keeps them in its disk cache
    // by URL regardless of Cache-Control, which suits covers: they rarely change.
    setSingletonImageLoaderFactory { context ->
        ImageLoader.Builder(context)
            .components { add(KtorNetworkFetcherFactory(httpClient = api.httpClient)) }
            .build()
    }

    val session = viewModel { SessionViewModel(api, localNetworkGranted) }
    val screen by session.screen.collectAsState()

    // Ask whenever the state says to: after a rotation this simply asks again (the
    // system answers at once if the user already decided).
    val ask = (screen as? SessionScreen.ChooseServer)?.askLocalNetwork == true
    LaunchedEffect(ask) {
        if (ask) localNetwork.request(session::onLocalNetworkAnswer)
    }

    AudiobooksTheme {
        Surface {
            when (val s = screen) {
                SessionScreen.Starting -> StartingScreen()
                is SessionScreen.Unreachable -> UnreachableScreen(s, onRetry = session::resume, onChangeServer = session::chooseServer)
                is SessionScreen.ChooseServer -> ChooseServerScreen(s, onSubmit = session::submitServer)
                is SessionScreen.SignIn -> SignInScreen(s, onSubmit = session::signIn, onChangeServer = session::chooseServer)
                is SessionScreen.ChangePassword -> ChangePasswordScreen(s, onSubmit = session::changePassword, onSignOut = session::signOut)
                is SessionScreen.SignedIn -> LibraryHost(api, player, progress, downloads, downloadSettings, onSignOut = {
                    // Signing out stops a book this account was playing.
                    player.stop()
                    session.signOut()
                })
            }
        }
    }
}
