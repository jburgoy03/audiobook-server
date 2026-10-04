package dev.deanburgoyne.audiobooks.downloads

import androidx.room.ConstructedBy
import androidx.room.Dao
import androidx.room.Database
import androidx.room.Entity
import androidx.room.ForeignKey
import androidx.room.PrimaryKey
import androidx.room.Query
import androidx.room.RoomDatabase
import androidx.room.RoomDatabaseConstructor
import androidx.room.Transaction
import androidx.room.Upsert
import kotlinx.coroutines.flow.Flow

enum class DownloadState { Queued, Downloading, Complete, Failed }

/**
 * A book kept on the device. [detailJson] is GET /api/books/{id} exactly as the server
 * sent it (files, chapters, blurb), so the book page and the player work offline.
 */
@Entity(tableName = "downloaded_books")
data class DownloadedBook(
    @PrimaryKey val bookId: String,
    val title: String,
    val author: String?,
    val detailJson: String,
    /** The server's contentVersion when this was downloaded. */
    val contentVersion: String?,
    /** The server's contentVersion as last seen in the library; different means stale. */
    val serverContentVersion: String?,
    val totalBytes: Long,
    val downloadedBytes: Long,
    val state: DownloadState,
    val error: String?,
    val addedAtMillis: Long,
) {
    /**
     * The server's files changed since the download (a rescan replaced them): the
     * local timeline may not match the server's any more, so positions could drift.
     * Unknown versions (an older server, or not yet compared) count as current.
     */
    val isStale: Boolean
        get() = contentVersion != null && serverContentVersion != null && contentVersion != serverContentVersion
}

/** One file of a downloaded book, by sequence. Its location comes from [DownloadFiles]. */
@Entity(
    tableName = "downloaded_files",
    primaryKeys = ["bookId", "sequence"],
    foreignKeys = [ForeignKey(DownloadedBook::class, ["bookId"], ["bookId"], onDelete = ForeignKey.CASCADE)],
)
data class DownloadedFile(
    val bookId: String,
    val sequence: Int,
    val sizeBytes: Long,
    val complete: Boolean,
)

@Dao
interface DownloadDao {
    @Query("SELECT * FROM downloaded_books ORDER BY addedAtMillis DESC")
    fun observeBooks(): Flow<List<DownloadedBook>>

    @Query("SELECT * FROM downloaded_books ORDER BY addedAtMillis DESC")
    suspend fun allBooks(): List<DownloadedBook>

    @Query("SELECT * FROM downloaded_books WHERE bookId = :bookId")
    suspend fun book(bookId: String): DownloadedBook?

    @Query("SELECT * FROM downloaded_files WHERE bookId = :bookId ORDER BY sequence")
    suspend fun files(bookId: String): List<DownloadedFile>

    @Upsert
    suspend fun upsertBook(book: DownloadedBook)

    @Upsert
    suspend fun upsertFiles(files: List<DownloadedFile>)

    @Query("DELETE FROM downloaded_books WHERE bookId = :bookId")
    suspend fun delete(bookId: String)

    /** A fresh start for [book]: any earlier download of it (and its file rows) goes. */
    @Transaction
    suspend fun replace(book: DownloadedBook, files: List<DownloadedFile>) {
        delete(book.bookId)
        upsertBook(book)
        upsertFiles(files)
    }

    @Query("UPDATE downloaded_books SET state = :state, error = :error WHERE bookId = :bookId")
    suspend fun setState(bookId: String, state: DownloadState, error: String? = null)

    @Query("UPDATE downloaded_books SET downloadedBytes = :bytes WHERE bookId = :bookId")
    suspend fun setDownloadedBytes(bookId: String, bytes: Long)

    @Query("UPDATE downloaded_files SET complete = 1 WHERE bookId = :bookId AND sequence = :sequence")
    suspend fun markFileComplete(bookId: String, sequence: Int)

    @Query("UPDATE downloaded_books SET serverContentVersion = :version WHERE bookId = :bookId")
    suspend fun setServerContentVersion(bookId: String, version: String?)
}

@Database(entities = [DownloadedBook::class, DownloadedFile::class], version = 1)
@ConstructedBy(DownloadDatabaseConstructor::class)
abstract class DownloadDatabase : RoomDatabase() {
    abstract fun downloads(): DownloadDao
}

// Room generates the actual implementations per platform.
@Suppress("KotlinNoActualForExpect")
expect object DownloadDatabaseConstructor : RoomDatabaseConstructor<DownloadDatabase> {
    override fun initialize(): DownloadDatabase
}
