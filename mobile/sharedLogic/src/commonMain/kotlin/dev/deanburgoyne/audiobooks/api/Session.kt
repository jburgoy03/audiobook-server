package dev.deanburgoyne.audiobooks.api

import kotlinx.serialization.Serializable

@Serializable
data class Tokens(val accessToken: String, val refreshToken: String)

/**
 * What survives an app restart. The server and username stay after signing out, so
 * signing back in is one passphrase away; only the tokens are discarded.
 */
data class Session(val serverUrl: String, val username: String?, val tokens: Tokens?)

/**
 * Where the session lives. Android keeps the tokens encrypted with a Keystore key;
 * iOS will use the Keychain. Small and synchronous: it's read on startup and written
 * on sign-in, refresh and sign-out, never in a loop.
 */
interface SessionStore {
    fun load(): Session?
    fun save(session: Session)
}

class InMemorySessionStore(private var session: Session? = null) : SessionStore {
    override fun load(): Session? = session
    override fun save(session: Session) { this.session = session }
}
