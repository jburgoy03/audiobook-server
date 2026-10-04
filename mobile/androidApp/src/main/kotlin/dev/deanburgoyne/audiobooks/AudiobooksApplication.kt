package dev.deanburgoyne.audiobooks

import android.app.Application
import java.time.Instant
import dev.deanburgoyne.audiobooks.api.AndroidSessionStore
import dev.deanburgoyne.audiobooks.api.AudiobookApi
import dev.deanburgoyne.audiobooks.api.platformHttpEngine
import dev.deanburgoyne.audiobooks.playback.AndroidBookPlayer
import dev.deanburgoyne.audiobooks.playback.BookPlayer
import dev.deanburgoyne.audiobooks.progress.AndroidPendingReports
import dev.deanburgoyne.audiobooks.progress.ProgressSync
import dev.deanburgoyne.audiobooks.progress.androidDevice
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.launch

/**
 * One API client for the whole process: activities come and go (rotation, the system
 * reclaiming memory), but the HTTP connection pool and the refresh lock must not be
 * duplicated. Built by hand; a DI framework would be more machinery than three objects
 * need.
 */
class AudiobooksApplication : Application() {
    val api: AudiobookApi by lazy { AudiobookApi(platformHttpEngine(), AndroidSessionStore(this)) }

    private val scope = MainScope()

    /**
     * Shared by the playback service (which reports) and the UI (which shows the jump
     * offer). Both run in this process, so one instance is the single source of truth.
     */
    val progress: ProgressSync by lazy {
        ProgressSync(api, androidDevice(this), AndroidPendingReports(this), now = { Instant.now().toString() }, scope)
    }

    override fun onCreate() {
        super.onCreate()
        // Anything left from an offline session or a killed process goes now.
        scope.launch { progress.flush() }
    }

    /** Connects to the playback service on first use (the UI's first frame). */
    val player: BookPlayer by lazy { AndroidBookPlayer(this, api) }
}
