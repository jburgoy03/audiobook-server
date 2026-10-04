package dev.deanburgoyne.audiobooks.progress

import android.content.Context
import android.os.Build
import android.provider.Settings
import dev.deanburgoyne.audiobooks.api.ApiJson
import dev.deanburgoyne.audiobooks.api.ProgressReport
import kotlinx.serialization.encodeToString
import kotlinx.serialization.decodeFromString
import java.util.UUID

/**
 * Pending reports in SharedPreferences, one JSON entry per book. Small by construction
 * (one report per book in the queue), so no database is needed for it.
 */
class AndroidPendingReports(context: Context) : PendingReports {
    private val prefs = context.getSharedPreferences("pending-progress", Context.MODE_PRIVATE)

    @Synchronized
    override fun all(): List<ProgressReport> =
        prefs.all.values.mapNotNull { (it as? String)?.let { json -> runCatching { ApiJson.decodeFromString<ProgressReport>(json) }.getOrNull() } }
            .sortedBy { it.reportedAt }

    @Synchronized
    override fun put(report: ProgressReport) {
        prefs.edit().putString(report.bookId, ApiJson.encodeToString(report)).apply()
    }

    @Synchronized
    override fun remove(report: ProgressReport) {
        val stored = prefs.getString(report.bookId, null) ?: return
        if (runCatching { ApiJson.decodeFromString<ProgressReport>(stored) }.getOrNull() == report)
            prefs.edit().remove(report.bookId).apply()
    }
}

/**
 * This install's identity for progress: a UUID made on first use (kept until the app
 * is uninstalled or its data cleared), and the phone's own name as set in Settings
 * ("Dean's Pixel"), falling back to the model. That name is what the web shows in
 * "Pixel 8 is 12 min ahead".
 */
fun androidDevice(context: Context): Device {
    val prefs = context.getSharedPreferences("device", Context.MODE_PRIVATE)
    val id = prefs.getString("id", null) ?: UUID.randomUUID().toString().also { prefs.edit().putString("id", it).apply() }
    val name = Settings.Global.getString(context.contentResolver, Settings.Global.DEVICE_NAME)?.takeIf { it.isNotBlank() }
        ?: "${Build.MANUFACTURER} ${Build.MODEL}"
    return Device(id, name)
}
