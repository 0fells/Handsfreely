package io.github.handsfreely.link

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import io.github.handsfreely.crypto.P256
import java.security.KeyStore
import java.security.PrivateKey
import java.security.interfaces.ECPrivateKey
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class Identity private constructor(val privateKey: PrivateKey, val publicKey: ByteArray) {

    companion object {
        private const val PREFS = "identity"
        private const val WRAP_ALIAS = "handsfreely_identity_wrap"

        @Volatile
        private var cached: Identity? = null

        fun get(context: Context): Identity = cached ?: synchronized(this) {
            cached ?: load(context).also { cached = it }
        }

        private fun load(context: Context): Identity {
            val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            val sealed = prefs.getString("d", null)
            val pub = prefs.getString("pub", null)
            if (sealed != null && pub != null) {
                runCatching {
                    val d = unwrap(Base64.decode(sealed, Base64.NO_WRAP))
                    return Identity(P256.privateFromScalar(d), Base64.decode(pub, Base64.NO_WRAP))
                }
            }
            val pair = P256.generate()
            val d = fixed32((pair.private as ECPrivateKey).s.toByteArray())
            val encoded = P256.encode(pair.public)
            prefs.edit()
                .putString("d", Base64.encodeToString(wrap(d), Base64.NO_WRAP))
                .putString("pub", Base64.encodeToString(encoded, Base64.NO_WRAP))
                .apply()
            return Identity(P256.privateFromScalar(d), encoded)
        }

        private fun wrapKey(): SecretKey {
            val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
            (ks.getKey(WRAP_ALIAS, null) as? SecretKey)?.let { return it }
            val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
            gen.init(
                KeyGenParameterSpec.Builder(WRAP_ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .setKeySize(256)
                    .build()
            )
            return gen.generateKey()
        }

        private fun wrap(data: ByteArray): ByteArray {
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.ENCRYPT_MODE, wrapKey())
            return cipher.iv + cipher.doFinal(data)
        }

        private fun unwrap(sealed: ByteArray): ByteArray {
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, wrapKey(), GCMParameterSpec(128, sealed, 0, 12))
            return cipher.doFinal(sealed, 12, sealed.size - 12)
        }

        private fun fixed32(raw: ByteArray): ByteArray = when {
            raw.size == 32 -> raw
            raw.size > 32 -> raw.copyOfRange(raw.size - 32, raw.size)
            else -> ByteArray(32 - raw.size) + raw
        }
    }
}
