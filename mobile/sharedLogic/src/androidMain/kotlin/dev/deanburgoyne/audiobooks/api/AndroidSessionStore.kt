package dev.deanburgoyne.audiobooks.api

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString

/**
 * The session in SharedPreferences, with the tokens encrypted by an AES key that
 * lives in the Android Keystore and never leaves it.
 *
 * Why encrypt files that are already private to the app: a refresh token is a 30-day
 * credential, and app data can leave the device in a backup. Keystore keys are never
 * backed up, so a restored blob can't be decrypted and simply reads as "signed out".
 * The server URL and username aren't secret and stay readable.
 *
 * Plain Keystore rather than androidx.security's EncryptedSharedPreferences, which
 * Google has deprecated.
 */
class AndroidSessionStore(context: Context) : SessionStore {
    private val prefs = context.getSharedPreferences("session", Context.MODE_PRIVATE)

    override fun load(): Session? {
        val serverUrl = prefs.getString(KEY_SERVER, null) ?: return null
        val tokens = prefs.getString(KEY_TOKENS, null)?.let { sealed ->
            // Undecryptable (restored from a backup, key reset): treat as signed out.
            runCatching { ApiJson.decodeFromString<Tokens>(decrypt(sealed)) }.getOrNull()
        }
        return Session(serverUrl, prefs.getString(KEY_USERNAME, null), tokens)
    }

    override fun save(session: Session) {
        prefs.edit()
            .putString(KEY_SERVER, session.serverUrl)
            .putString(KEY_USERNAME, session.username)
            .putString(KEY_TOKENS, session.tokens?.let { encrypt(ApiJson.encodeToString(it)) })
            .apply()
    }

    private fun encrypt(plain: String): String {
        val cipher = Cipher.getInstance(TRANSFORMATION).apply { init(Cipher.ENCRYPT_MODE, key()) }
        // The IV (12 bytes, chosen by the cipher) is stored in front of the ciphertext.
        return Base64.encodeToString(cipher.iv + cipher.doFinal(plain.encodeToByteArray()), Base64.NO_WRAP)
    }

    private fun decrypt(sealed: String): String {
        val bytes = Base64.decode(sealed, Base64.NO_WRAP)
        val cipher = Cipher.getInstance(TRANSFORMATION).apply {
            init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, bytes, 0, IV_LENGTH))
        }
        return cipher.doFinal(bytes, IV_LENGTH, bytes.size - IV_LENGTH).decodeToString()
    }

    private fun key(): SecretKey {
        val keyStore = KeyStore.getInstance(KEYSTORE).apply { load(null) }
        (keyStore.getKey(KEY_ALIAS, null) as SecretKey?)?.let { return it }

        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KEYSTORE).run {
            init(
                KeyGenParameterSpec.Builder(KEY_ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .setKeySize(256)
                    .build()
            )
            generateKey()
        }
    }

    private companion object {
        const val KEYSTORE = "AndroidKeyStore"
        const val KEY_ALIAS = "audiobooks.session"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val IV_LENGTH = 12
        const val KEY_SERVER = "serverUrl"
        const val KEY_USERNAME = "username"
        const val KEY_TOKENS = "tokens"
    }
}
