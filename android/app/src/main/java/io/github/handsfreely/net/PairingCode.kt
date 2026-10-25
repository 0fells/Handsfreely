package io.github.handsfreely.net

import io.github.handsfreely.crypto.Handshake
import io.github.handsfreely.crypto.P256
import java.net.URLDecoder
import java.net.URLEncoder
import java.util.Base64

class PairingCode(val bluetoothAddress: String, val pcPublicKey: ByteArray, val token: ByteArray, val pcName: String) {

    fun toUri(): String =
        "$PREFIX" + "a=$bluetoothAddress&k=${b64(pcPublicKey)}&t=${b64(token)}&n=${encode(pcName)}"

    val colonAddress: String get() = bluetoothAddress.chunked(2).joinToString(":")

    companion object {
        private const val PREFIX = "handsfreely:1?"
        private val MAC = Regex("^[0-9A-Fa-f]{12}$")

        fun parse(text: String): PairingCode? {
            if (!text.startsWith(PREFIX)) return null
            val fields = text.removePrefix(PREFIX).split('&').associate {
                val eq = it.indexOf('=')
                if (eq <= 0) return null
                it.substring(0, eq) to it.substring(eq + 1)
            }
            val mac = fields["a"]?.takeIf(MAC::matches) ?: return null
            val key = fields["k"]?.let(::unb64) ?: return null
            val token = fields["t"]?.let(::unb64) ?: return null
            if (key.size != P256.PUBLIC_KEY_SIZE || token.size != Handshake.TOKEN_SIZE) return null
            val name = fields["n"]?.let { runCatching { URLDecoder.decode(it.replace("+", "%2B"), "UTF-8") }.getOrNull() } ?: ""
            return PairingCode(mac.uppercase(), key, token, name)
        }

        fun b64(data: ByteArray): String = Base64.getUrlEncoder().withoutPadding().encodeToString(data)

        private fun unb64(text: String): ByteArray? = runCatching { Base64.getUrlDecoder().decode(text) }.getOrNull()

        private fun encode(s: String) = URLEncoder.encode(s, "UTF-8").replace("+", "%20")
    }
}
