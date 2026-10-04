package dev.deanburgoyne.audiobooks.progress

import dev.deanburgoyne.audiobooks.api.ApiException
import dev.deanburgoyne.audiobooks.api.AudiobookApi
import dev.deanburgoyne.audiobooks.api.Progress
import dev.deanburgoyne.audiobooks.api.ProgressReport
import dev.deanburgoyne.audiobooks.api.SessionExpiredException
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlin.math.abs

/** This install, as the server's device list knows it. [id] is a UUID chosen once. */
data class Device(val id: String, val name: String)

/** Another device is meaningfully further along in the playing book. */
data class JumpOffer(val bookId: String, val positionSeconds: Double, val deviceName: String?, val reportedAt: String)

/**
 * Reports not yet accepted by the server: at most one per book, the newest, since a
 * device's newer report supersedes its older ones anyway. Kept on disk, so listening
 * offline (or the app being killed mid-send) loses nothing.
 */
interface PendingReports {
    fun all(): List<ProgressReport>
    /** Replaces any pending report for the same book. */
    fun put(report: ProgressReport)
    /** Removes [report] if it's still the pending one for its book (not replaced since). */
    fun remove(report: ProgressReport)
}

class InMemoryPendingReports : PendingReports {
    private val byBook = linkedMapOf<String, ProgressReport>()
    override fun all() = byBook.values.toList()
    override fun put(report: ProgressReport) { byBook.remove(report.bookId); byBook[report.bookId] = report }
    override fun remove(report: ProgressReport) { if (byBook[report.bookId] == report) byBook.remove(report.bookId) }
}

/**
 * Sending listening progress, and the decision of when this device's position should
 * override the server's. The same rules as the web client (web/src/player/
 * useBookPlayer.ts), against the server's (Core/Progress/ProgressRules.cs):
 *
 * - When a book starts or resumes, compare with the server ("reconcile"). If another
 *   device is more than [JumpThresholdSeconds] ahead, offer to jump there.
 * - Once reconciled with no offer open, this device is the one playing and is
 *   authoritative: reports go as overrides, so rewinding sticks.
 * - Until then (offline at start, or an offer the listener hasn't answered), reports
 *   are ordinary and the server's furthest-wins rule applies, so this device can't
 *   overwrite another one's progress before the listener has decided.
 * - A report the server rejects (another device is ahead) is treated like a reconcile
 *   that found it: an offer.
 *
 * The platform calls [started] and [report]; everything else happens here. All calls
 * on one thread (the main thread on Android).
 */
class ProgressSync(
    private val api: AudiobookApi,
    private val device: Device,
    private val pending: PendingReports,
    private val now: () -> String,
    private val scope: CoroutineScope,
) {
    private val _offer = MutableStateFlow<JumpOffer?>(null)
    val offer: StateFlow<JumpOffer?> = _offer.asStateFlow()

    private var bookId: String? = null
    private var reconciled = false
    /** The server state (by reportedAt) the listener already answered, so it isn't offered again. */
    private var answered: String? = null
    private var lastReported: Pair<Double, Boolean>? = null
    private val sending = Mutex()

    /** Playback of [bookId] started or resumed at [positionSeconds]. */
    fun started(bookId: String, positionSeconds: Double) {
        select(bookId)
        scope.launch { reconcile(bookId, positionSeconds) }
    }

    /** [started], waiting for the comparison with the server (tests). */
    internal suspend fun startedAndReconciled(bookId: String, positionSeconds: Double) {
        select(bookId)
        reconcile(bookId, positionSeconds)
    }

    /**
     * The position to save: on pause, after a seek, at the end, and periodically while
     * playing. Repeats of the last report are skipped: re-asserting an old position as
     * the newest would, under the same-device rule, undo another device's progress.
     */
    fun report(bookId: String, positionSeconds: Double, isFinished: Boolean = false) {
        select(bookId)
        val last = lastReported
        if (last != null && abs(last.first - positionSeconds) < 0.5 && last.second == isFinished) return
        lastReported = positionSeconds to isFinished

        pending.put(
            ProgressReport(
                bookId = bookId,
                positionSeconds = positionSeconds,
                reportedAt = now(),
                deviceId = device.id,
                deviceName = device.name,
                isFinished = isFinished,
                override = reconciled && _offer.value == null,
            )
        )
        scope.launch { flush() }
    }

    /** The listener chose the other device's position. Returns it, to seek there. */
    fun acceptOffer(): JumpOffer? {
        val offer = _offer.value ?: return null
        answered = offer.reportedAt
        _offer.value = null
        lastReported = null
        return offer
    }

    /** The listener chose to stay: say so to the server, as an override. */
    fun dismissOffer(currentPositionSeconds: Double) {
        val offer = _offer.value ?: return
        answered = offer.reportedAt
        _offer.value = null
        lastReported = null
        report(offer.bookId, currentPositionSeconds)
    }

    /**
     * Sends what's pending, oldest first. Stops at the first network failure and
     * leaves the rest for the next attempt (the next report, or the app starting).
     */
    suspend fun flush() = sending.withLock {
        for (report in pending.all()) {
            val result = try {
                api.reportProgress(report)
            } catch (e: CancellationException) {
                throw e
            } catch (_: SessionExpiredException) {
                return@withLock // signed out; the reports wait for the next sign-in
            } catch (e: ApiException) {
                // 4xx won't succeed on a retry (the book was removed, or the account
                // can't save right now): drop it rather than block the queue. 5xx: retry later.
                if (e.status in 400..499) { pending.remove(report); continue }
                return@withLock
            } catch (_: Exception) {
                return@withLock // offline
            }

            pending.remove(report)
            if (!result.accepted && report.bookId == bookId) {
                considerOffer(report.bookId, result.progress, report.positionSeconds)
            }
        }
    }

    private suspend fun reconcile(bookId: String, positionSeconds: Double) {
        val server = try {
            api.bookProgress(bookId)
        } catch (e: CancellationException) {
            throw e
        } catch (_: Exception) {
            return // offline: stay unreconciled, so reports stay furthest-wins
        }
        if (bookId != this.bookId) return
        reconciled = true
        considerOffer(bookId, server, positionSeconds)
    }

    private fun considerOffer(bookId: String, server: Progress?, positionSeconds: Double) {
        if (server == null || server.deviceId == device.id || server.isFinished) return
        if (server.reportedAt == answered || server.reportedAt == _offer.value?.reportedAt) return
        if (server.positionSeconds <= positionSeconds + JumpThresholdSeconds) return
        _offer.value = JumpOffer(bookId, server.positionSeconds, server.deviceName, server.reportedAt)
    }

    /** A different book: everything about the last one's sync no longer applies. */
    private fun select(bookId: String) {
        if (bookId == this.bookId) return
        this.bookId = bookId
        reconciled = false
        answered = null
        lastReported = null
        _offer.value = null
    }

    companion object {
        /** Reports go every 30 s, so less than this is ordinary lag between two devices. */
        const val JumpThresholdSeconds = 30.0
        const val ReportIntervalMillis = 30_000L
    }
}
