package dev.deanburgoyne.audiobooks

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import android.graphics.Color
import androidx.activity.SystemBarStyle
import androidx.activity.enableEdgeToEdge
import dev.deanburgoyne.audiobooks.downloads.DownloadSettings

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        // Dark only, like the web client: light status and navigation icons on black.
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.dark(Color.TRANSPARENT),
        )
        super.onCreate(savedInstanceState)

        val app = application as AudiobooksApplication
        val api = app.api
        val player = app.player
        // Read outside the lambda: `applicationContext` inside it would capture this
        // activity, and the view model keeps the lambda after the activity is gone.
        val appContext = applicationContext
        setContent {
            App(
                api,
                player,
                app.progress,
                app.downloads,
                object : DownloadSettings {
                    override var wifiOnly by app::downloadOnWifiOnly
                },
                localNetwork = rememberLocalNetworkPermission(this),
                localNetworkGranted = { hasLocalNetworkPermission(appContext) },
            )
        }
    }
}