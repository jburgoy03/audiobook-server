package dev.deanburgoyne.audiobooks.api

import io.ktor.client.engine.mock.MockEngine
import io.ktor.client.engine.mock.MockRequestHandleScope
import io.ktor.client.engine.mock.respond
import io.ktor.client.engine.mock.respondError
import io.ktor.client.engine.mock.toByteArray
import io.ktor.client.request.HttpRequestData
import io.ktor.client.request.HttpResponseData
import io.ktor.client.request.get
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpStatusCode
import io.ktor.http.headersOf
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue

private const val Server = "https://books.test"
private val JsonHeaders = headersOf(HttpHeaders.ContentType, "application/json")

private fun MockRequestHandleScope.json(body: String, status: HttpStatusCode = HttpStatusCode.OK) =
    respond(body, status, JsonHeaders)

private fun tokens(n: Int) = """{"tokenType":"Bearer","accessToken":"access-$n","expiresIn":3600,"refreshToken":"refresh-$n"}"""
private const val Me = """{"username":"dean","isAdmin":true,"mustChangePassword":false,"someFutureField":1}"""

private fun api(
    store: SessionStore = InMemorySessionStore(),
    handler: suspend MockRequestHandleScope.(HttpRequestData) -> HttpResponseData,
) = AudiobookApi(MockEngine(handler), store)

private fun signedIn(access: String = "access-1") =
    InMemorySessionStore(Session(Server, "dean", Tokens(access, "refresh-1")))

class ProbeTest {
    @Test
    fun a_compatible_server_is_found_and_https_is_assumed() = runTest {
        val result = api { request ->
            assertEquals("$Server/api/server-info", request.url.toString())
            json("""{"product":"AudiobookServer","version":"1.0.0","apiVersion":1}""")
        }.probe("  books.test/ ")

        assertIs<ProbeResult.Compatible>(result)
        assertEquals(Server, result.serverUrl)
    }

    @Test
    fun another_website_is_not_an_audiobook_server() = runTest {
        val html = api { respond("<html>hello</html>", HttpStatusCode.OK, headersOf(HttpHeaders.ContentType, "text/html")) }
        assertEquals(ProbeResult.NotAudiobookServer, html.probe(Server))

        val missing = api { respondError(HttpStatusCode.NotFound) }
        assertEquals(ProbeResult.NotAudiobookServer, missing.probe(Server))

        val otherProduct = api { json("""{"product":"Jellyfin","version":"10","apiVersion":1}""") }
        assertEquals(ProbeResult.NotAudiobookServer, otherProduct.probe(Server))
    }

    @Test
    fun a_newer_api_is_incompatible_and_says_which_side_is_older() = runTest {
        val result = api { json("""{"product":"AudiobookServer","version":"9.0.0","apiVersion":99}""") }.probe(Server)

        assertIs<ProbeResult.Incompatible>(result)
        assertEquals(false, result.serverIsOlder)
    }

    @Test
    fun nonsense_is_rejected_before_any_request() = runTest {
        val api = api { error("no request expected") }
        assertEquals(ProbeResult.InvalidUrl, api.probe(""))
        assertEquals(ProbeResult.InvalidUrl, api.probe("not a url"))
        assertEquals(ProbeResult.InvalidUrl, api.probe("ftp://books.test"))
    }
}

class SessionTest {
    @Test
    fun signing_in_keeps_the_tokens_and_returns_the_user() = runTest {
        val store = InMemorySessionStore()
        val user = api(store) { request ->
            when (request.url.encodedPath) {
                "/api/auth/login" -> json(tokens(1))
                "/api/auth/me" -> {
                    assertEquals("Bearer access-1", request.headers[HttpHeaders.Authorization])
                    json(Me)
                }
                else -> error("unexpected ${request.url}")
            }
        }.signIn(Server, "dean", "a long passphrase")

        assertEquals("dean", user.username)
        assertEquals(Tokens("access-1", "refresh-1"), store.load()?.tokens)
    }

    @Test
    fun a_wrong_passphrase_is_a_plain_message_and_stores_nothing() = runTest {
        val store = InMemorySessionStore()
        val error = assertFailsWith<ApiException> {
            api(store) { respondError(HttpStatusCode.Unauthorized) }.signIn(Server, "dean", "wrong")
        }
        assertEquals(401, error.status)
        assertNull(store.load())
    }

    @Test
    fun a_server_error_carries_the_problem_detail() = runTest {
        val error = assertFailsWith<ApiException> {
            api { json("""{"status":429,"detail":"Too many failed attempts."}""", HttpStatusCode.TooManyRequests) }
                .signIn(Server, "dean", "x")
        }
        assertEquals("Too many failed attempts.", error.message)
    }

    @Test
    fun an_expired_access_token_is_refreshed_once_and_the_request_retried() = runTest {
        val store = signedIn()
        var refreshes = 0
        val api = api(store) { request ->
            when (request.url.encodedPath) {
                "/api/auth/refresh" -> { refreshes++; json(tokens(2)) }
                "/api/auth/me" ->
                    if (request.headers[HttpHeaders.Authorization] == "Bearer access-2") json(Me)
                    else respondError(HttpStatusCode.Unauthorized)
                else -> error("unexpected ${request.url}")
            }
        }

        // Five requests at once, all holding the expired token.
        val users = List(5) { async { api.me() } }.awaitAll()

        assertEquals(5, users.size)
        assertEquals(1, refreshes)
        assertEquals("access-2", store.load()?.tokens?.accessToken)
    }

    @Test
    fun a_refused_refresh_ends_the_session_but_keeps_the_server() = runTest {
        val store = signedIn()
        val api = api(store) { respondError(HttpStatusCode.Unauthorized) }
        var ended = false
        backgroundScope.launch { api.sessionEnded.collect { ended = true } }
        testScheduler.runCurrent() // subscribed before the event, which isn't replayed

        assertFailsWith<SessionExpiredException> { api.me() }
        testScheduler.runCurrent()

        assertNull(store.load()?.tokens)
        assertEquals(Server, store.load()?.serverUrl)
        assertTrue(ended)
    }

    @Test
    fun changing_the_passphrase_signs_in_again_with_the_new_one() = runTest {
        val store = signedIn()
        var signedInWith: String? = null
        api(store) { request ->
            when (request.url.encodedPath) {
                "/api/auth/change-password" -> respond("", HttpStatusCode.NoContent)
                "/api/auth/login" -> {
                    signedInWith = request.body.toByteArray().decodeToString()
                    json(tokens(3))
                }
                "/api/auth/me" -> json(Me)
                else -> error("unexpected ${request.url}")
            }
        }.changePassword("old passphrase", "a brand new passphrase")

        assertTrue(signedInWith!!.contains("a brand new passphrase"))
        assertEquals("access-3", store.load()?.tokens?.accessToken)
    }
}

class LibraryTest {
    @Test
    fun the_token_goes_only_to_the_signed_in_server() = runTest {
        var leaked: String? = "not called"
        val api = api(signedIn()) { request ->
            leaked = request.headers[HttpHeaders.Authorization]
            json("[]")
        }

        api.httpClient.get("https://elsewhere.test/image.jpg")
        assertNull(leaked)

        api.books()
        assertEquals("Bearer access-1", leaked)
    }

    @Test
    fun books_and_details_parse_and_a_missing_book_is_null() = runTest {
        val api = api(signedIn()) { request ->
            when (request.url.encodedPath) {
                "/api/books" -> json("""[{"id":"b1","title":"Dune","author":"Frank Herbert","durationSeconds":75600.5,
                    "hasCover":true,"files":2,"chapters":48,"addedAt":"2026-10-04T12:00:00+00:00","contentVersion":"abc"}]""")
                "/api/books/b1" -> json("""{"id":"b1","title":"Dune","durationSeconds":75600.5,"hasCover":true,
                    "files":[{"sequence":0,"startOffsetSeconds":0,"durationSeconds":37800,"mimeType":"audio/mpeg","sizeBytes":1000}],
                    "chapters":[{"sequence":0,"title":"Book One","startOffsetSeconds":0,"endOffsetSeconds":3600}]}""")
                else -> respondError(HttpStatusCode.NotFound)
            }
        }

        assertEquals("Frank Herbert", api.books().single().author)
        assertEquals("Book One", api.book("b1")!!.chapters.single().title)
        assertNull(api.book("gone"))
        assertEquals("$Server/api/books/b1/cover?size=640", api.coverUrl("b1", 640))
    }
}

class StreamAuthTest {
    @Test
    fun a_player_gets_the_header_only_for_its_own_server() {
        val api = api(signedIn()) { error("no request expected") }
        assertEquals("Bearer access-1", api.authorizationFor(api.streamUrl("b1", 0)))
        assertNull(api.authorizationFor("https://elsewhere.test/a.mp3"))
    }

    @Test
    fun a_stale_401_from_a_player_refreshes_once() = runTest {
        val store = signedIn()
        var refreshes = 0
        val api = api(store) { refreshes++; json(tokens(2)) }

        api.refreshAfterUnauthorized("Bearer access-1")
        api.refreshAfterUnauthorized("Bearer access-1") // a second stream, same stale token

        assertEquals(1, refreshes)
        assertEquals("Bearer access-2", api.authorizationFor(api.streamUrl("b1", 0)))
    }
}
