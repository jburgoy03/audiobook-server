package dev.deanburgoyne.audiobooks.api

import kotlinx.serialization.Serializable

// Written against docs/openapi.json in the server repo. The server only ever adds
// fields (docs/api-contract.md), so every DTO tolerates unknown ones (see ApiJson).

@Serializable
data class ServerInfo(val product: String, val version: String, val apiVersion: Int)

@Serializable
data class CurrentUser(val username: String, val isAdmin: Boolean, val mustChangePassword: Boolean)

@Serializable
internal data class LoginRequest(val username: String, val password: String)

@Serializable
internal data class RefreshRequest(val refreshToken: String)

/** Identity's bearer token response; expiresIn is in seconds. */
@Serializable
internal data class AccessTokenResponse(
    val accessToken: String,
    val expiresIn: Long,
    val refreshToken: String,
)

@Serializable
internal data class ChangePasswordRequest(val currentPassword: String, val newPassword: String)

/** RFC 7807 ProblemDetails: the server's only error shape. */
@Serializable
internal data class Problem(val status: Int? = null, val title: String? = null, val detail: String? = null)

// Timestamps stay ISO-8601 strings for now: nothing on screen does date maths yet,
// and it keeps a date-time library out of the dependency list until something does.

/** One row of GET /api/books. [files] and [chapters] are counts. */
@Serializable
data class BookSummary(
    val id: String,
    val title: String,
    val author: String? = null,
    val durationSeconds: Double,
    val hasCover: Boolean,
    val files: Int,
    val chapters: Int,
    val addedAt: String,
    val contentVersion: String? = null,
)

@Serializable
data class BookDetail(
    val id: String,
    val title: String,
    val subtitle: String? = null,
    val author: String? = null,
    val narrator: String? = null,
    val description: String? = null,
    val descriptionSource: String? = null,
    val publishedYear: Int? = null,
    val durationSeconds: Double,
    val hasCover: Boolean,
    val credit: String? = null,
    val files: List<BookFile>,
    val chapters: List<Chapter>,
    val contentVersion: String? = null,
)

/** A file on the book's timeline, addressed by [sequence] (never by an ID). */
@Serializable
data class BookFile(
    val sequence: Int,
    val startOffsetSeconds: Double,
    val durationSeconds: Double,
    val mimeType: String? = null,
    val sizeBytes: Long,
)

@Serializable
data class Chapter(
    val sequence: Int,
    val title: String,
    val startOffsetSeconds: Double,
    val endOffsetSeconds: Double,
)

/** The server's position for one book: one number on the book's timeline. */
@Serializable
data class Progress(
    val bookId: String,
    val positionSeconds: Double,
    val reportedAt: String,
    val updatedAt: String,
    val deviceId: String? = null,
    val deviceName: String? = null,
    val isFinished: Boolean,
)
