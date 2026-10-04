package dev.deanburgoyne.audiobooks

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)

        val api = (application as AudiobooksApplication).api
        // Read outside the lambda: `applicationContext` inside it would capture this
        // activity, and the view model keeps the lambda after the activity is gone.
        val appContext = applicationContext
        setContent {
            App(
                api,
                localNetwork = rememberLocalNetworkPermission(this),
                localNetworkGranted = { hasLocalNetworkPermission(appContext) },
            )
        }
    }
}