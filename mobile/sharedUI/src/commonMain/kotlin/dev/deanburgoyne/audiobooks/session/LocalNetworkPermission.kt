package dev.deanburgoyne.audiobooks.session

/**
 * Android 17's local network permission, as the shared UI sees it. The platform
 * supplies it: Android asks with the system dialog; other platforms (and older
 * Android) never need to.
 */
interface LocalNetworkPermission {
    fun isGranted(): Boolean

    /** Shows the system prompt if needed; [onResult] gets the answer. */
    fun request(onResult: (granted: Boolean) -> Unit)
}

object NoLocalNetworkPermission : LocalNetworkPermission {
    override fun isGranted() = true
    override fun request(onResult: (Boolean) -> Unit) = onResult(true)
}
