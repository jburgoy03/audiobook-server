package dev.deanburgoyne.audiobooks

import android.app.Application
import dev.deanburgoyne.audiobooks.api.AndroidSessionStore
import dev.deanburgoyne.audiobooks.api.AudiobookApi
import dev.deanburgoyne.audiobooks.api.platformHttpEngine

/**
 * One API client for the whole process: activities come and go (rotation, the system
 * reclaiming memory), but the HTTP connection pool and the refresh lock must not be
 * duplicated. Built by hand; a DI framework would be more machinery than three objects
 * need.
 */
class AudiobooksApplication : Application() {
    val api: AudiobookApi by lazy { AudiobookApi(platformHttpEngine(), AndroidSessionStore(this)) }
}
