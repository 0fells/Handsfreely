package io.github.handsfreely.crypto

import java.nio.ByteBuffer
import javax.crypto.AEADBadTagException
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

class FrameCipher(key: ByteArray) {
    private val key = SecretKeySpec(key, "AES")
    private var counter = 0L

    @Synchronized
    fun encrypt(plaintext: ByteArray): ByteArray {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key, GCMParameterSpec(TAG_BITS, nextNonce()))
        return cipher.doFinal(plaintext)
    }

    @Synchronized
    fun decrypt(frame: ByteArray): ByteArray {
        if (frame.size < TAG_BITS / 8) throw ProtocolException("encrypted frame too short")
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(TAG_BITS, nextNonce()))
        return try {
            cipher.doFinal(frame)
        } catch (e: AEADBadTagException) {
            throw ProtocolException("frame authentication failed", e)
        }
    }

    private fun nextNonce(): ByteArray {
        if (counter == -1L) throw ProtocolException("nonce counter exhausted")
        return ByteBuffer.allocate(12).putInt(0).putLong(counter++).array()
    }

    private companion object {
        const val TAG_BITS = 128
    }
}
