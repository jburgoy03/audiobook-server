package dev.deanburgoyne.audiobooks.downloads

import android.content.Context
import androidx.room.Room
import androidx.sqlite.driver.bundled.BundledSQLiteDriver
import kotlinx.coroutines.Dispatchers
import java.io.File

fun downloadDatabase(context: Context): DownloadDatabase {
    val appContext = context.applicationContext
    return Room.databaseBuilder<DownloadDatabase>(
        context = appContext,
        name = appContext.getDatabasePath("downloads.db").absolutePath,
    )
        .setDriver(BundledSQLiteDriver())
        .setQueryCoroutineContext(Dispatchers.IO)
        .build()
}

/**
 * Books under the app's private storage (filesDir/books/<bookId>/<sequence>). Private
 * needs no storage permission, other apps can't read it, and Android deletes it with
 * the app. No file extension: the player detects the format from the bytes, as it does
 * when streaming.
 */
class AndroidDownloadFiles(context: Context) : DownloadFiles {
    private val root = File(context.filesDir, "books")

    override fun path(bookId: String, sequence: Int): String =
        File(File(root, bookId), sequence.toString()).absolutePath

    override fun exists(bookId: String, sequence: Int) = File(path(bookId, sequence)).isFile

    override fun deleteBook(bookId: String) {
        File(root, bookId).deleteRecursively()
    }
}
