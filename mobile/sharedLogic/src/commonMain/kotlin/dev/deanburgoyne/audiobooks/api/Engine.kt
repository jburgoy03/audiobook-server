package dev.deanburgoyne.audiobooks.api

import io.ktor.client.engine.HttpClientEngine

/** OkHttp on Android, NSURLSession (Darwin) on iOS. */
expect fun platformHttpEngine(): HttpClientEngine
