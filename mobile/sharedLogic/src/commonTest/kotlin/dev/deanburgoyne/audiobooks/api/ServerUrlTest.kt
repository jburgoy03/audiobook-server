package dev.deanburgoyne.audiobooks.api

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class ServerUrlTest {
    @Test
    fun local_addresses_are_recognised() {
        for (url in listOf(
            "http://10.0.2.2:5043", "http://192.168.1.20", "http://172.20.0.5", "http://169.254.1.1",
            "http://100.101.102.103", "https://nas.local", "https://server.tailnet-abc.ts.net",
            "http://[fd12:3456::1]:8080",
        )) assertTrue(isLocalNetworkAddress(url), url)
    }

    @Test
    fun public_and_loopback_addresses_are_not() {
        for (url in listOf(
            "https://audiobooks.deanburgoyne.dev", "http://172.32.0.1", "http://100.128.0.1",
            "http://8.8.8.8", "http://localhost:5043", "http://127.0.0.1",
        )) assertFalse(isLocalNetworkAddress(url), url)
    }

    @Test
    fun addresses_are_normalised() {
        assertEquals("https://books.test", normalizeServerUrl(" books.test/ "))
        assertEquals("http://10.0.2.2:5043", normalizeServerUrl("http://10.0.2.2:5043"))
    }
}
