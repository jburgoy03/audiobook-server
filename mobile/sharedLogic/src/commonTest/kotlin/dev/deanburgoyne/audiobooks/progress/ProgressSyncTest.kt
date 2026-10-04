package dev.deanburgoyne.audiobooks.progress

import dev.deanburgoyne.audiobooks.api.AudiobookApi
import dev.deanburgoyne.audiobooks.api.InMemorySessionStore
import dev.deanburgoyne.audiobooks.api.Progress
import dev.deanburgoyne.audiobooks.api.ProgressReport
import dev.deanburgoyne.audiobooks.api.Session
import dev.deanburgoyne.audiobooks.api.Tokens
import io.ktor.client.engine.mock.MockEngine
import io.ktor.client.engine.mock.respond
import io.ktor.client.engine.mock.toByteArray
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpStatusCode
import io.ktor.http.headersOf
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

private const val Me = "phone-id"

/**
 * A fake server: [serverProgress] is what GET /books/b1/progress answers (null = 204),
 * every POST is recorded, and [online] = false makes requests fail like no network.
 */
private class FakeServer {
    var serverProgress: String? = null
    var online = true
    var reject: String? = null // a stored progress JSON to reject reports with
    val sent = mutableListOf<ProgressReport>()

    fun api() = AudiobookApi(
        MockEngine { request ->
            if (!online) throw RuntimeException("offline")
            val json = headersOf(HttpHeaders.ContentType, "application/json")
            when (request.url.encodedPath) {
                "/api/books/b1/progress" ->
                    serverProgress?.let { respond(it, HttpStatusCode.OK, json) } ?: respond("", HttpStatusCode.NoContent)
                "/api/progress" -> {
                    val report = Json.decodeFromString<ProgressReport>(request.body.toByteArray().decodeToString())
                    sent += report
                    val stored = reject ?: progress(report.positionSeconds, Me, report.reportedAt)
                    respond("""{"accepted":${reject == null},"reason":"x","progress":$stored}""", HttpStatusCode.OK, json)
                }
                else -> error("unexpected ${request.url}")
            }
        },
        InMemorySessionStore(Session("https://books.test", "dean", Tokens("a", "r"))),
    )
}

private fun progress(position: Double, device: String, reportedAt: String = "2026-10-04T10:00:00Z") =
    """{"bookId":"b1","positionSeconds":$position,"reportedAt":"$reportedAt","updatedAt":"$reportedAt",
       "deviceId":"$device","deviceName":"Firefox","isFinished":false}"""

private var tick = 0
private fun TestScope.sync(server: FakeServer, pending: PendingReports = InMemoryPendingReports()) =
    ProgressSync(server.api(), Device(Me, "Pixel"), pending, InMemoryPositionCache(), now = { "2026-10-04T12:00:${(tick++ % 60).toString().padStart(2, '0')}Z" }, backgroundScope)

class ProgressSyncTest {
    @Test
    fun before_reconciling_reports_are_ordinary_and_after_they_override() = runTest {
        val server = FakeServer()
        val sync = sync(server)

        sync.report("b1", 100.0)
        sync.flush()
        assertFalse(server.sent.last().override)

        sync.startedAndReconciled("b1", 100.0) // the server has nothing further along
        sync.report("b1", 50.0)     // a rewind
        sync.flush()
        assertTrue(server.sent.last().override)
        assertNull(sync.offer.value)
    }

    @Test
    fun another_device_ahead_is_offered_and_reports_stay_ordinary_until_answered() = runTest {
        val server = FakeServer().apply { serverProgress = progress(5000.0, "web") }
        val sync = sync(server)

        sync.startedAndReconciled("b1", 1000.0)
        val offer = assertNotNull(sync.offer.value)
        assertEquals(5000.0, offer.positionSeconds)

        sync.report("b1", 1010.0)
        sync.flush()
        assertFalse(server.sent.last().override, "must not overwrite the other device before the listener decides")

        sync.dismissOffer(1020.0)
        sync.flush()
        assertTrue(server.sent.last().override)
        assertEquals(1020.0, server.sent.last().positionSeconds)

        sync.startedAndReconciled("b1", 1020.0) // the same server state again: already answered
        assertNull(sync.offer.value)
    }

    @Test
    fun small_leads_own_positions_and_finished_books_are_not_offered() = runTest {
        for (stored in listOf(progress(1020.0, "web"), progress(9000.0, Me))) {
            val server = FakeServer().apply { serverProgress = stored }
            val sync = sync(server)
            sync.startedAndReconciled("b1", 1000.0)
            assertNull(sync.offer.value, stored)
        }
    }

    @Test
    fun accepting_returns_the_position_to_jump_to() = runTest {
        val server = FakeServer().apply { serverProgress = progress(5000.0, "web") }
        val sync = sync(server)
        sync.startedAndReconciled("b1", 1000.0)

        assertEquals(5000.0, sync.acceptOffer()?.positionSeconds)
        assertNull(sync.offer.value)
    }

    @Test
    fun offline_reports_wait_and_only_the_newest_per_book_is_sent() = runTest {
        val server = FakeServer().apply { online = false }
        val pending = InMemoryPendingReports()
        val sync = sync(server, pending)

        sync.report("b1", 100.0)
        sync.report("b1", 200.0)
        sync.flush()
        assertEquals(listOf(200.0), pending.all().map { it.positionSeconds })

        server.online = true
        sync.flush()
        assertEquals(listOf(200.0), server.sent.map { it.positionSeconds })
        assertTrue(pending.all().isEmpty())
    }

    @Test
    fun a_rejected_report_turns_into_an_offer() = runTest {
        val server = FakeServer().apply { reject = progress(8000.0, "web") }
        val sync = sync(server)

        sync.report("b1", 100.0)
        sync.flush()

        assertEquals(8000.0, sync.offer.value?.positionSeconds)
    }

    @Test
    fun a_repeated_position_is_not_reported_again() = runTest {
        val server = FakeServer()
        val sync = sync(server)

        sync.report("b1", 100.0)
        sync.report("b1", 100.2)
        sync.flush()

        assertEquals(1, server.sent.size)
    }

    @Test
    fun the_device_remembers_where_it_was_and_unsent_positions_beat_the_server() = runTest {
        val server = FakeServer().apply { online = false }
        val sync = sync(server)

        sync.report("b1", 700.0)
        sync.flush() // offline: still pending

        // The library loads an older server position: the unsent one wins.
        sync.rememberServer(listOf(Json.decodeFromString<Progress>(progress(100.0, "web"))))
        assertEquals(700.0, sync.knownPositions()["b1"]?.positionSeconds)

        server.online = true
        sync.flush()
        sync.rememberServer(listOf(Json.decodeFromString<Progress>(progress(900.0, "web"))))
        assertEquals(900.0, sync.knownPositions()["b1"]?.positionSeconds)
    }
}
