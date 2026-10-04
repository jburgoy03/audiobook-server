package dev.deanburgoyne.audiobooks.api

import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.engine.HttpClientEngine
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.request.HttpRequestBuilder
import io.ktor.client.request.bearerAuth
import io.ktor.client.request.get
import io.ktor.client.request.post
import io.ktor.client.request.request
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.http.ContentType
import io.ktor.http.HttpMethod
import io.ktor.http.HttpStatusCode
import io.ktor.http.contentType
import io.ktor.http.isSuccess
import io.ktor.serialization.kotlinx.json.json
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.json.Json

/** The API versions this build understands (ServerInfo.apiVersion). */
val SupportedApiVersions = 1..1

sealed interface ProbeResult {
    data class Compatible(val serverUrl: String, val info: ServerInfo) : ProbeResult
    data object InvalidUrl : ProbeResult
    data class Unreachable(val reason: String?) : ProbeResult
    /** Answered, but not as an AudiobookServer (another site, or a proxy's error page). */
    data object NotAudiobookServer : ProbeResult
    data class Incompatible(val info: ServerInfo) : ProbeResult {
        val serverIsOlder: Boolean get() = info.apiVersion < SupportedApiVersions.first
    }
}

/** The server answered with an error; [message] is its ProblemDetails detail when it gave one. */
class ApiException(val status: Int, message: String) : Exception(message)

/** The refresh token was refused (expired, or revoked by a passphrase change). Sign in again. */
class SessionExpiredException : Exception("Your session has ended. Sign in again.")

internal val ApiJson = Json {
    // The server adds fields over time; an older app must not fail on them.
    ignoreUnknownKeys = true
    explicitNulls = false
}

/**
 * The server, as the app sees it. Shared by Android and iOS: platform code supplies
 * only the HTTP engine and where the session is kept.
 *
 * Authentication is Identity's bearer tokens: a 1-hour access token and a 30-day
 * refresh token. Rather than track expiry, requests go out with the access token and
 * a 401 triggers one refresh and one retry. Refreshes are single-flight: when several
 * requests hit a 401 at once, one refreshes and the rest reuse its result, so a burst
 * of requests after an hour asleep costs one refresh, not one each.
 *
 * Written by hand rather than with Ktor's Auth plugin, whose refresh is triggered by a
 * WWW-Authenticate challenge this server's bearer handler doesn't send; doing it here
 * keeps the rule ("401 → refresh once → retry once") visible and testable.
 */
class AudiobookApi(
    engine: HttpClientEngine,
    private val store: SessionStore,
) {
    private val http = HttpClient(engine) {
        install(ContentNegotiation) { json(ApiJson) }
        // Statuses are handled per call: a 401 means refresh, a 204 means "none yet".
        expectSuccess = false
    }

    private val refreshLock = Mutex()

    private val _sessionEnded = MutableSharedFlow<Unit>(extraBufferCapacity = 1)

    /** Emits when a refresh is refused mid-session, so the UI can return to sign-in. */
    val sessionEnded: SharedFlow<Unit> = _sessionEnded.asSharedFlow()

    val session: Session? get() = store.load()

    /** Is there an AudiobookServer at [input], and does this app speak its API? */
    suspend fun probe(input: String): ProbeResult {
        val serverUrl = normalizeServerUrl(input) ?: return ProbeResult.InvalidUrl

        val response = try {
            http.get("$serverUrl/api/server-info")
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            return ProbeResult.Unreachable(e.message)
        }

        if (!response.status.isSuccess()) return ProbeResult.NotAudiobookServer
        val info = try {
            response.body<ServerInfo>()
        } catch (e: CancellationException) {
            throw e
        } catch (_: Exception) {
            return ProbeResult.NotAudiobookServer
        }

        return when {
            info.product != "AudiobookServer" -> ProbeResult.NotAudiobookServer
            info.apiVersion !in SupportedApiVersions -> ProbeResult.Incompatible(info)
            else -> ProbeResult.Compatible(serverUrl, info)
        }
    }

    /** Signs in to [serverUrl] (already probed) and keeps the session. */
    suspend fun signIn(serverUrl: String, username: String, password: String): CurrentUser {
        // No ?useCookies: without it the server answers with tokens in the body.
        val response = http.post("$serverUrl/api/auth/login") {
            contentType(ContentType.Application.Json)
            setBody(LoginRequest(username, password))
        }
        if (response.status == HttpStatusCode.Unauthorized)
            throw ApiException(401, "That username and passphrase don't match.")
        response.throwIfError()

        store.save(Session(serverUrl, username, response.body<AccessTokenResponse>().toTokens()))
        return me()
    }

    suspend fun me(): CurrentUser =
        authorized { get("auth/me") }.throwIfError().body()

    /**
     * Changes the passphrase, then signs in again with it. The change revokes every
     * session including this one (the server's security stamp moves), and this
     * session's tokens still carry the must-change claim, so a fresh sign-in is the
     * only way forward.
     */
    suspend fun changePassword(currentPassword: String, newPassword: String): CurrentUser {
        val session = store.load() ?: throw SessionExpiredException()
        val username = session.username ?: throw SessionExpiredException()

        authorized {
            post("auth/change-password") { setBody(ChangePasswordRequest(currentPassword, newPassword)) }
        }.throwIfError()

        return signIn(session.serverUrl, username, newPassword)
    }

    /** Bearer tokens have nothing to revoke server-side: forgetting them is signing out. */
    fun signOut() {
        store.load()?.let { store.save(it.copy(tokens = null)) }
    }

    /**
     * Runs [request] with the access token; on a 401, refreshes once and runs it again.
     * [request] gets a builder whose paths are relative to /api/.
     */
    private suspend fun authorized(request: suspend AuthorizedRequests.() -> HttpResponse): HttpResponse {
        val sent = store.load()?.tokens ?: throw SessionExpiredException()

        val first = AuthorizedRequests(sent.accessToken).request()
        if (first.status != HttpStatusCode.Unauthorized) return first

        val fresh = refresh(stale = sent)
        return AuthorizedRequests(fresh.accessToken).request()
    }

    private suspend fun refresh(stale: Tokens): Tokens = refreshLock.withLock {
        val session = store.load()
        val current = session?.tokens ?: throw SessionExpiredException()

        // Someone else refreshed while this caller waited for the lock: use theirs.
        if (current != stale) return current

        val response = http.post("${session.serverUrl}/api/auth/refresh") {
            contentType(ContentType.Application.Json)
            setBody(RefreshRequest(current.refreshToken))
        }

        if (response.status == HttpStatusCode.Unauthorized) {
            store.save(session.copy(tokens = null))
            _sessionEnded.tryEmit(Unit)
            throw SessionExpiredException()
        }
        response.throwIfError()

        val tokens = response.body<AccessTokenResponse>().toTokens()
        store.save(session.copy(tokens = tokens))
        tokens
    }

    private inner class AuthorizedRequests(private val accessToken: String) {
        private val base get() = (store.load() ?: throw SessionExpiredException()).serverUrl

        suspend fun get(path: String, block: HttpRequestBuilder.() -> Unit = {}): HttpResponse =
            send(HttpMethod.Get, path, block)

        suspend fun post(path: String, block: HttpRequestBuilder.() -> Unit = {}): HttpResponse =
            send(HttpMethod.Post, path) { contentType(ContentType.Application.Json); block() }

        private suspend fun send(method: HttpMethod, path: String, block: HttpRequestBuilder.() -> Unit) =
            http.request("$base/api/$path") {
                this.method = method
                bearerAuth(accessToken)
                block()
            }
    }

    private suspend fun HttpResponse.throwIfError(): HttpResponse {
        if (status.isSuccess()) return this
        val problem = try { body<Problem>() } catch (e: CancellationException) { throw e } catch (_: Exception) { null }
        throw ApiException(status.value, problem?.detail ?: problem?.title ?: status.description)
    }

    private fun AccessTokenResponse.toTokens() = Tokens(accessToken, refreshToken)
}
