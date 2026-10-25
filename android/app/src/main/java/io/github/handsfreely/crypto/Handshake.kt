package io.github.handsfreely.crypto

import java.security.MessageDigest
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

enum class HandshakeMode(val code: Byte) { PAIR(1), RESUME(2) }

enum class HandshakeStatus(val code: Int) {
    OK(0), UNKNOWN_DEVICE(1), PAIRING_CLOSED(2), BAD_VERSION(3);

    companion object {
        fun of(code: Int) = entries.firstOrNull { it.code == code } ?: throw ProtocolException("bad handshake status $code")
    }
}

class HandshakeRejectedException(val status: HandshakeStatus) : Exception("handshake rejected: $status")

class SessionKeys(val phoneToPc: ByteArray, val pcToPhone: ByteArray)

object Handshake {
    const val VERSION: Byte = 1
    const val TOKEN_SIZE = 16

    private val MAGIC = byteArrayOf('H'.code.toByte(), 'F'.code.toByte(), VERSION)
    private val PROTOCOL_NAME = "Handsfreely/1".toByteArray(Charsets.US_ASCII)
    private val KEYS_INFO = "Handsfreely/1 keys".toByteArray(Charsets.US_ASCII)

    fun buildM1(mode: HandshakeMode, phoneStatic: ByteArray, phoneEph: ByteArray): ByteArray =
        MAGIC + byteArrayOf(mode.code) + phoneStatic + phoneEph

    fun parseM2(m: ByteArray): ByteArray {
        if (m.size < 4 || !m.copyOfRange(0, 3).contentEquals(MAGIC)) throw ProtocolException("bad M2")
        val status = HandshakeStatus.of(m[3].toInt())
        if (status != HandshakeStatus.OK) throw HandshakeRejectedException(status)
        if (m.size != 4 + P256.PUBLIC_KEY_SIZE) throw ProtocolException("bad M2 size")
        val pcEph = m.copyOfRange(4, m.size)
        P256.decode(pcEph)
        return pcEph
    }

    fun deriveKeys(
        pcStatic: ByteArray, m1: ByteArray, m2: ByteArray,
        dhEphEph: ByteArray, dhPhoneEphPcStatic: ByteArray, dhPhoneStaticPcEph: ByteArray,
        token: ByteArray,
    ): SessionKeys {
        val h = MessageDigest.getInstance("SHA-256").digest(PROTOCOL_NAME + pcStatic + m1 + m2)
        val ikm = dhEphEph + dhPhoneEphPcStatic + dhPhoneStaticPcEph + token
        val okm = hkdf(ikm, h, KEYS_INFO, 64)
        ikm.fill(0)
        return SessionKeys(okm.copyOfRange(0, 32), okm.copyOfRange(32, 64))
    }

    fun hkdf(ikm: ByteArray, salt: ByteArray, info: ByteArray, length: Int): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(salt, "HmacSHA256"))
        val prk = mac.doFinal(ikm)
        mac.init(SecretKeySpec(prk, "HmacSHA256"))
        val out = ByteArray(length)
        var t = ByteArray(0)
        var pos = 0
        var counter = 1
        while (pos < length) {
            mac.update(t)
            mac.update(info)
            mac.update(counter.toByte())
            t = mac.doFinal()
            val n = minOf(t.size, length - pos)
            System.arraycopy(t, 0, out, pos, n)
            pos += n
            counter++
        }
        return out
    }
}
