package dev.deanburgoyne.audiobooks

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge()
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
                localNetwork = rememberLocalNetworkPermission(this),
                localNetworkGranted = { hasLocalNetworkPermission(appContext) },
            )
        }
    }
}