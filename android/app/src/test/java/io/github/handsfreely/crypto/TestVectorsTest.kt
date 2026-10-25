package io.github.handsfreely.crypto

import io.github.handsfreely.net.PairingCode
import org.json.JSONObject
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Test
import java.io.File

class TestVectorsTest {
    private val v = JSONObject(File("../../docs/test-vectors.json").readText())

    private fun hex(name: String): ByteArray = v.getString(name).chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    @Test
    fun handshakeAndFramesMatchDotNet() {
        val m1 = hex("m1")
        val m2 = hex("m2")
        val phoneStaticPub = m1.copyOfRange(4, 69)
        val phoneEphPub = m1.copyOfRange(69, 134)
        assertArrayEquals(hex("phoneStaticPublic"), phoneStaticPub)
        assertArrayEquals(m1, Handshake.buildM1(HandshakeMode.PAIR, phoneStaticPub, phoneEphPub))

        val pcEphPub = Handshake.parseM2(m2)
        val pcStatic = hex("pcStaticPublic")
        val phoneStatic = P256.privateFromScalar(hex("phoneStaticPrivate"))
        val phoneEph = P256.privateFromScalar(hex("phoneEphPrivate"))

        val keys = Handshake.deriveKeys(
            pcStatic, m1, m2,
            P256.sharedSecret(phoneEph, P256.decode(pcEphPub)),
            P256.sharedSecret(phoneEph, P256.decode(pcStatic)),
            P256.sharedSecret(phoneStatic, P256.decode(pcEphPub)),
            hex("token"),
        )
        assertArrayEquals(hex("keyPhoneToPc"), keys.phoneToPc)
        assertArrayEquals(hex("keyPcToPhone"), keys.pcToPhone)

        val up = FrameCipher(keys.phoneToPc)
        assertArrayEquals(hex("frame0"), up.encrypt(hex("plaintext")))
        assertArrayEquals(hex("frame1"), up.encrypt(hex("plaintext")))

        val pcSide = FrameCipher(keys.phoneToPc)
        assertArrayEquals(hex("plaintext"), pcSide.decrypt(hex("frame0")))
    }

    @Test
    fun publicKeyRoundTripAndValidation() {
        val pair = P256.generate()
        val encoded = P256.encode(pair.public)
        assertEquals(65, encoded.size)
        assertArrayEquals(encoded, P256.encode(P256.decode(encoded)))
        val bad = encoded.copyOf().also { it[64] = (it[64] + 1).toByte() }
        assertThrows(ProtocolException::class.java) { P256.decode(bad) }
    }

    @Test
    fun rejectedHandshakeStatus() {
        val m2 = byteArrayOf('H'.code.toByte(), 'F'.code.toByte(), 1, 1)
        val e = assertThrows(HandshakeRejectedException::class.java) { Handshake.parseM2(m2) }
        assertEquals(HandshakeStatus.UNKNOWN_DEVICE, e.status)
    }

    @Test
    fun pairingCodeParsing() {
        val key = P256.encode(P256.generate().public)
        val token = ByteArray(16) { it.toByte() }
        val text = PairingCode("08BFB854BA57", key, token, "Рабочий ПК & co").toUri()
        val parsed = PairingCode.parse(text)
        assertNotNull(parsed)
        assertEquals("08BFB854BA57", parsed!!.bluetoothAddress)
        assertArrayEquals(key, parsed.pcPublicKey)
        assertArrayEquals(token, parsed.token)
        assertEquals("Рабочий ПК & co", parsed.pcName)
        assertNull(PairingCode.parse("https://example.com"))
        assertNull(PairingCode.parse("handsfreely:1?a=XYZ&k=AA&t=AA"))
    }
}
