package com.crossdeck.client.connection

import android.content.Context
import android.util.Base64
import java.nio.charset.StandardCharsets
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties

/**
 * Stores pairing state encrypted with an Android Keystore AES key. A migration from the v2.1
 * plaintext preference store happens once, before the legacy values are deleted.
 */
internal class SecurePairingStore(context: Context) {
    private val securePrefs = context.getSharedPreferences(SECURE_PREFS, Context.MODE_PRIVATE)
    private val legacyPrefs = context.getSharedPreferences(LEGACY_PREFS, Context.MODE_PRIVATE)
    private val key = loadOrCreateKey()

    init {
        migrateLegacyPairing()
    }

    fun getString(name: String): String? {
        val encoded = securePrefs.getString(name, null) ?: return null
        return try {
            decrypt(encoded)
        } catch (e: Exception) {
            // Treat a corrupted or non-restorable key as an unpaired client, never as plaintext.
            securePrefs.edit().remove(name).apply()
            null
        }
    }

    fun putString(name: String, value: String) {
        securePrefs.edit().putString(name, encrypt(value)).apply()
    }

    fun remove(name: String) {
        securePrefs.edit().remove(name).apply()
    }

    fun clear() {
        securePrefs.edit().clear().apply()
        // Also clear keys left by older versions if an upgrade was interrupted.
        legacyPrefs.edit().clear().apply()
    }

    private fun migrateLegacyPairing() {
        val names = listOf(KEY_IP, KEY_TOKEN, KEY_FINGERPRINT)
        val values = names.mapNotNull { name -> legacyPrefs.getString(name, null)?.let { name to it } }
            .toMutableList()
        if (legacyPrefs.all.containsKey(KEY_PORT)) {
            values += KEY_PORT to legacyPrefs.getInt(KEY_PORT, -1).toString()
        }
        val legacyPin = legacyPrefs.getString(KEY_PIN, null)
        if (values.isEmpty() && legacyPin == null) return

        val editor = securePrefs.edit()
        values.forEach { (name, value) -> editor.putString(name, encrypt(value)) }
        if (!editor.commit()) throw IllegalStateException("Unable to migrate secure pairing state")

        // Never carry the old PIN forward. It was only a bootstrap credential.
        if (!legacyPrefs.edit().clear().commit()) {
            throw IllegalStateException("Unable to remove legacy pairing state")
        }
    }

    private fun encrypt(value: String): String {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, key)
        val ciphertext = cipher.doFinal(value.toByteArray(StandardCharsets.UTF_8))
        val combined = ByteArray(cipher.iv.size + ciphertext.size)
        cipher.iv.copyInto(combined, 0)
        ciphertext.copyInto(combined, cipher.iv.size)
        return Base64.encodeToString(combined, Base64.NO_WRAP)
    }

    private fun decrypt(encoded: String): String {
        val combined = Base64.decode(encoded, Base64.NO_WRAP)
        if (combined.size <= GCM_IV_BYTES) throw IllegalArgumentException("Invalid encrypted pairing value")
        val iv = combined.copyOfRange(0, GCM_IV_BYTES)
        val ciphertext = combined.copyOfRange(GCM_IV_BYTES, combined.size)
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(GCM_TAG_BITS, iv))
        return String(cipher.doFinal(ciphertext), StandardCharsets.UTF_8)
    }

    private fun loadOrCreateKey(): SecretKey {
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }
        (keyStore.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }

        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE)
        generator.init(
            KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
            )
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setRandomizedEncryptionRequired(true)
                .build()
        )
        return generator.generateKey()
    }

    companion object {
        private const val ANDROID_KEYSTORE = "AndroidKeyStore"
        private const val KEY_ALIAS = "crossdeck_pairing_key"
        private const val SECURE_PREFS = "crossdeck_pairing_secure"
        private const val LEGACY_PREFS = "crossdeck_pairing"
        private const val TRANSFORMATION = "AES/GCM/NoPadding"
        private const val GCM_IV_BYTES = 12
        private const val GCM_TAG_BITS = 128

        private const val KEY_IP = "ip"
        private const val KEY_PORT = "port"
        private const val KEY_PIN = "pin"
        private const val KEY_TOKEN = "token"
        private const val KEY_FINGERPRINT = "fingerprint"
    }
}
