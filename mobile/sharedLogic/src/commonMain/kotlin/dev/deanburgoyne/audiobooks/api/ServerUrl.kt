package dev.deanburgoyne.audiobooks.api

import io.ktor.http.URLProtocol
import io.ktor.http.Url

/**
 * What a person types, as a base URL: "audiobooks.example.com" becomes
 * "https://audiobooks.example.com". HTTPS is assumed unless they write http://
 * themselves; a trailing slash is dropped so paths can be appended. Null if it can't
 * be a server address at all.
 */
fun normalizeServerUrl(input: String): String? {
    val trimmed = input.trim().trimEnd('/')
    if (trimmed.isEmpty() || trimmed.any { it.isWhitespace() }) return null

    val withScheme = if ("://" in trimmed) trimmed else "https://$trimmed"
    val url = try { Url(withScheme) } catch (_: Exception) { return null }

    if (url.protocol != URLProtocol.HTTP && url.protocol != URLProtocol.HTTPS) return null
    if (url.host.isBlank()) return null
    return withScheme
}

/**
 * Whether [serverUrl] points into a local network, which Android 17 guards with a
 * runtime permission (ACCESS_LOCAL_NETWORK); without it, connections time out.
 *
 * Judged from the address as typed, without a DNS lookup: private and link-local
 * IPv4 (10/8, 172.16/12, 192.168/16, 169.254/16), Tailscale's 100.64/10, unique- and
 * link-local IPv6, and the names that conventionally resolve to those (.local mDNS,
 * Tailscale's .ts.net). A plain hostname that happens to resolve to a LAN address
 * ("nas" on a home router's DNS) isn't caught here; it fails as a timeout instead.
 */
fun isLocalNetworkAddress(serverUrl: String): Boolean {
    val host = try { Url(serverUrl).host.lowercase().removePrefix("[").removeSuffix("]") } catch (_: Exception) { return false }

    if (host.endsWith(".local") || host.endsWith(".ts.net")) return true

    if (':' in host) return host.startsWith("fc") || host.startsWith("fd") || host.startsWith("fe8")

    val octets = host.split('.')
    if (octets.size != 4) return false
    val (a, b) = octets.map { it.toIntOrNull() ?: return false }
    return a == 10 ||
        (a == 172 && b in 16..31) ||
        (a == 192 && b == 168) ||
        (a == 169 && b == 254) ||
        (a == 100 && b in 64..127)
}
