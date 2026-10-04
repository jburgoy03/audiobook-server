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
