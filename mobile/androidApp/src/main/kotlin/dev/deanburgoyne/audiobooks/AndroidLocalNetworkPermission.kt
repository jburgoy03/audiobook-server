package dev.deanburgoyne.audiobooks

import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import dev.deanburgoyne.audiobooks.session.LocalNetworkPermission

/**
 * Android 17 (API 37) blocks connections to local network addresses (a home server,
 * Tailscale, the emulator's 10.0.2.2) unless the app holds ACCESS_LOCAL_NETWORK, part
 * of the Nearby devices group. Blocked connections don't fail cleanly: they time out.
 * Older versions don't have the permission and don't need it.
 */
private const val PERMISSION = "android.permission.ACCESS_LOCAL_NETWORK"
private const val INTRODUCED_IN = 37

fun hasLocalNetworkPermission(context: Context): Boolean =
    Build.VERSION.SDK_INT < INTRODUCED_IN ||
        context.checkSelfPermission(PERMISSION) == PackageManager.PERMISSION_GRANTED

@Composable
fun rememberLocalNetworkPermission(context: Context): LocalNetworkPermission {
    // One pending answer at a time; the view model only asks from one screen.
    val pending = remember { arrayOfNulls<(Boolean) -> Unit>(1) }
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        pending[0]?.invoke(granted)
        pending[0] = null
    }
    return remember(launcher) {
        object : LocalNetworkPermission {
            override fun isGranted() = hasLocalNetworkPermission(context)

            override fun request(onResult: (Boolean) -> Unit) {
                if (isGranted()) return onResult(true)
                pending[0] = onResult
                launcher.launch(PERMISSION)
            }
        }
    }
}
