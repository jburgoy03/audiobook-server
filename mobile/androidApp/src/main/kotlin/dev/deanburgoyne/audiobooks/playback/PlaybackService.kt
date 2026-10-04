package dev.deanburgoyne.audiobooks.playback

import android.content.Intent
import androidx.annotation.OptIn
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.DataSourceBitmapLoader
import androidx.media3.datasource.HttpDataSource
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.session.CacheBitmapLoader
import androidx.media3.session.MediaSession
import androidx.media3.session.MediaSessionService
import dev.deanburgoyne.audiobooks.AudiobooksApplication
import dev.deanburgoyne.audiobooks.api.SessionExpiredException
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

/**
 * Owns the player, so audio carries on with the app in the background or closed, and
 * the system shows the notification and lock-screen controls (Media3 builds both from
 * the session). The UI talks to it through a MediaController (AndroidBookPlayer).
 */
@OptIn(UnstableApi::class)
class PlaybackService : MediaSessionService() {
    private var session: MediaSession? = null
    private val scope = MainScope()

    override fun onCreate() {
        super.onCreate()
        val api = (application as AudiobooksApplication).api
        val dataSource = authorizedDataSourceFactory(this, api)

        val player = ExoPlayer.Builder(this)
            .setMediaSourceFactory(DefaultMediaSourceFactory(dataSource))
            // Speech: other apps' sounds duck around it rather than over it, and
            // handleAudioFocus pauses for calls and resumes after.
            .setAudioAttributes(
                AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_SPEECH).build(),
                /* handleAudioFocus = */ true,
            )
            // Headphones unplugged or Bluetooth dropped: pause, don't play out loud.
            .setHandleAudioBecomingNoisy(true)
            // Keeps Wi-Fi and the CPU awake while streaming with the screen off.
            .setWakeMode(C.WAKE_MODE_NETWORK)
            .setSeekBackIncrementMs(30_000)
            .setSeekForwardIncrementMs(30_000)
            .build()

        player.addListener(RetryAfterRefresh(player, api))

        session = MediaSession.Builder(this, player)
            .setBitmapLoader(CacheBitmapLoader(DataSourceBitmapLoader.Builder(this).setDataSourceFactory(dataSource).build()))
            .build()
    }

    /**
     * An access token expires after an hour, and a long book outlives it: the next
     * range request (a seek, the next file) gets a 401 and the player stops with an
     * error. This refreshes (single-flight, shared with the rest of the app) and
     * prepares again, which resumes at the same position. Once per stale token, so a
     * server that keeps refusing can't loop.
     */
    private inner class RetryAfterRefresh(
        private val player: ExoPlayer,
        private val api: dev.deanburgoyne.audiobooks.api.AudiobookApi,
    ) : Player.Listener {
        private var retriedFor: String? = null

        override fun onPlayerError(error: PlaybackException) {
            val http = generateSequence<Throwable>(error) { it.cause }
                .filterIsInstance<HttpDataSource.InvalidResponseCodeException>()
                .firstOrNull() ?: return
            if (http.responseCode != 401) return

            val sent = http.dataSpec.httpRequestHeaders["Authorization"]
            if (sent == retriedFor) return
            retriedFor = sent

            scope.launch {
                try {
                    api.refreshAfterUnauthorized(sent)
                    player.prepare()
                } catch (e: CancellationException) {
                    throw e
                } catch (_: SessionExpiredException) {
                    // Signed out: the app shows sign-in (AudiobookApi.sessionEnded).
                    player.stop()
                } catch (_: Exception) {
                    // Offline mid-refresh: leave the error showing; play retries.
                }
            }
        }
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaSession? = session

    /** Swiped away from recents while paused: stop. While playing: keep going. */
    override fun onTaskRemoved(rootIntent: Intent?) {
        val player = session?.player
        if (player == null || !player.playWhenReady || player.mediaItemCount == 0) stopSelf()
    }

    override fun onDestroy() {
        scope.cancel()
        session?.run {
            player.release()
            release()
        }
        session = null
        super.onDestroy()
    }
}
