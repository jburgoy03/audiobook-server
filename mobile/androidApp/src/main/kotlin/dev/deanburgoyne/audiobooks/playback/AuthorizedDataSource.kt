package dev.deanburgoyne.audiobooks.playback

import android.content.Context
import androidx.annotation.OptIn
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.DataSource
import androidx.media3.datasource.DefaultDataSource
import androidx.media3.datasource.DefaultHttpDataSource
import androidx.media3.datasource.ResolvingDataSource
import dev.deanburgoyne.audiobooks.api.AudiobookApi

/**
 * Media3's HTTP stack with the bearer token added per request, resolved when the
 * request is made rather than when the player is built, so a refreshed token is used
 * from the next request on. [AudiobookApi.authorizationFor] adds it only for the
 * signed-in server's URLs. Used for the audio and for the notification's artwork.
 */
@OptIn(UnstableApi::class)
fun authorizedDataSourceFactory(context: Context, api: AudiobookApi): DataSource.Factory {
    val http = DefaultHttpDataSource.Factory()
        .setUserAgent("AudiobooksAndroid")
        // An https→http redirect would send the token in the clear.
        .setAllowCrossProtocolRedirects(false)

    val authorized = ResolvingDataSource.Factory(http) { spec ->
        api.authorizationFor(spec.uri.toString())
            ?.let { spec.withAdditionalHeaders(mapOf("Authorization" to it)) }
            ?: spec
    }

    // DefaultDataSource handles file:// and content:// too, which downloads will use.
    return DefaultDataSource.Factory(context, authorized)
}
