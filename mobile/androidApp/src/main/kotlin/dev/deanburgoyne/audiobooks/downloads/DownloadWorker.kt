package dev.deanburgoyne.audiobooks.downloads

import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.content.pm.ServiceInfo
import androidx.core.app.NotificationCompat
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingWorkPolicy
import androidx.work.ForegroundInfo
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import androidx.work.workDataOf
import dev.deanburgoyne.audiobooks.AudiobooksApplication
import io.ktor.client.request.header
import io.ktor.client.request.prepareGet
import io.ktor.client.statement.bodyAsChannel
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpStatusCode
import io.ktor.utils.io.jvm.javaio.toInputStream
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.io.FileOutputStream
import java.io.IOException

/**
 * Downloads one book's files, one after another, resuming each from where it stopped.
 *
 * Resuming is HTTP Range, which the stream endpoint already supports for seeking: a
 * partly written file asks for "bytes=<length>-" and appends. A dropped connection
 * on file 3 of 20 costs the rest of file 3, not the book. The request goes through the
 * API's own client, so the token is attached and refreshed on a 401 like any other.
 *
 * Runs as a foreground job (a notification with progress), which is what lets a long
 * download carry on with the app closed.
 */
class DownloadWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {
    private val app = context.applicationContext as AudiobooksApplication

    override suspend fun doWork(): Result {
        val bookId = inputData.getString(KEY_BOOK_ID) ?: return Result.failure()
        val dao = app.downloads.dao
        val book = dao.book(bookId) ?: return Result.success() // removed while queued

        setForeground(foregroundInfo(book.title, book.downloadedBytes, book.totalBytes))
        dao.setState(bookId, DownloadState.Downloading)

        return try {
            var done = dao.files(bookId).filter { it.complete }.sumOf { it.sizeBytes }
            for (file in dao.files(bookId).filter { !it.complete }) {
                val target = File(app.downloadFiles.path(bookId, file.sequence))
                fetch(bookId, file.sequence, file.sizeBytes, target) { written ->
                    dao.setDownloadedBytes(bookId, done + written)
                    setForeground(foregroundInfo(book.title, done + written, book.totalBytes))
                }
                dao.markFileComplete(bookId, file.sequence)
                done += file.sizeBytes
                dao.setDownloadedBytes(bookId, done)
            }
            dao.setState(bookId, DownloadState.Complete)
            Result.success()
        } catch (e: CancellationException) {
            throw e // removed, or the system stopped the job; WorkManager decides what's next
        } catch (e: Exception) {
            // Network trouble is worth retrying (WorkManager backs off between tries);
            // after a few, it's failed and the book page offers a retry.
            if (runAttemptCount < MAX_ATTEMPTS) {
                dao.setState(bookId, DownloadState.Queued, e.message)
                Result.retry()
            } else {
                dao.setState(bookId, DownloadState.Failed, e.message ?: "Download failed")
                Result.failure()
            }
        }
    }

    private suspend fun fetch(
        bookId: String,
        sequence: Int,
        expectedBytes: Long,
        target: File,
        onProgress: suspend (writtenInThisFile: Long) -> Unit,
    ) {
        target.parentFile?.mkdirs()
        val existing = if (target.isFile) target.length() else 0L
        if (existing == expectedBytes) return

        app.api.httpClient.prepareGet(app.api.streamUrl(bookId, sequence)) {
            if (existing > 0) header(HttpHeaders.Range, "bytes=$existing-")
        }.execute { response ->
            val append = when (response.status) {
                HttpStatusCode.PartialContent -> true
                HttpStatusCode.OK -> false // the server sent it all: start the file again
                else -> throw IOException("Server answered ${response.status.value} for file $sequence")
            }
            var written = if (append) existing else 0L
            var lastReport = 0L

            withContext(Dispatchers.IO) {
                response.bodyAsChannel().toInputStream().use { input ->
                    FileOutputStream(target, append).use { output ->
                        val buffer = ByteArray(64 * 1024)
                        while (true) {
                            val n = input.read(buffer)
                            if (n < 0) break
                            output.write(buffer, 0, n)
                            written += n
                            // Progress about every 2 MB: often enough to move the bar,
                            // rarely enough not to hammer the database.
                            if (written - lastReport >= 2 * 1024 * 1024) {
                                lastReport = written
                                onProgress(written)
                            }
                        }
                    }
                }
            }
        }

        // A short file (the connection closed early without an error) mustn't be
        // marked complete: the next attempt resumes it.
        if (target.length() != expectedBytes)
            throw IOException("File $sequence is ${target.length()} of $expectedBytes bytes")
    }

    private fun foregroundInfo(title: String, done: Long, total: Long): ForegroundInfo {
        val manager = applicationContext.getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL, "Downloads", NotificationManager.IMPORTANCE_LOW)
        )
        val percent = if (total > 0) ((done * 100) / total).toInt() else 0
        val notification = NotificationCompat.Builder(applicationContext, CHANNEL)
            .setSmallIcon(android.R.drawable.stat_sys_download)
            .setContentTitle("Downloading $title")
            .setProgress(100, percent, total <= 0)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .build()
        return ForegroundInfo(id.hashCode(), notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
    }

    companion object {
        private const val KEY_BOOK_ID = "bookId"
        private const val CHANNEL = "downloads"
        private const val MAX_ATTEMPTS = 5

        fun workName(bookId: String) = "download-$bookId"

        fun enqueue(context: Context, bookId: String, wifiOnly: Boolean) {
            val request = OneTimeWorkRequestBuilder<DownloadWorker>()
                .setInputData(workDataOf(KEY_BOOK_ID to bookId))
                .setConstraints(
                    Constraints.Builder()
                        .setRequiredNetworkType(if (wifiOnly) NetworkType.UNMETERED else NetworkType.CONNECTED)
                        .setRequiresStorageNotLow(true)
                        .build()
                )
                .build()
            // One job per book; asking again while one is queued or running changes nothing.
            WorkManager.getInstance(context).enqueueUniqueWork(workName(bookId), ExistingWorkPolicy.KEEP, request)
        }
    }
}

/** WorkManager behind the shared [DownloadScheduler]. */
class WorkManagerDownloadScheduler(
    private val context: Context,
    private val wifiOnly: () -> Boolean,
) : DownloadScheduler {
    override fun schedule(bookId: String) = DownloadWorker.enqueue(context, bookId, wifiOnly())
    override fun cancel(bookId: String) {
        WorkManager.getInstance(context).cancelUniqueWork(DownloadWorker.workName(bookId))
    }
}
