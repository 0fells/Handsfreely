package io.github.handsfreely.crypto

import java.math.BigInteger
import java.security.KeyFactory
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.PrivateKey
import java.security.PublicKey
import java.security.interfaces.ECPublicKey
import java.security.spec.ECFieldFp
import java.security.spec.ECGenParameterSpec
import java.security.spec.ECParameterSpec
import java.security.spec.ECPoint
import java.security.spec.ECPrivateKeySpec
import java.security.spec.ECPublicKeySpec
import javax.crypto.KeyAgreement

class ProtocolException(message: String, cause: Throwable? = null) : Exception(message, cause)

object P256 {
    const val PUBLIC_KEY_SIZE = 65

    private val generator: KeyPairGenerator
        get() = KeyPairGenerator.getInstance("EC").apply { initialize(ECGenParameterSpec("secp256r1")) }

    val params: ECParameterSpec by lazy { (generator.generateKeyPair().public as ECPublicKey).params }

    fun generate(): KeyPair = generator.generateKeyPair()

    fun privateFromScalar(d: ByteArray): PrivateKey =
        KeyFactory.getInstance("EC").generatePrivate(ECPrivateKeySpec(BigInteger(1, d), params))

    fun encode(key: PublicKey): ByteArray {
        val w = (key as ECPublicKey).w
        return byteArrayOf(0x04) + fixed32(w.affineX) + fixed32(w.affineY)
    }

    fun decode(bytes: ByteArray): PublicKey {
        if (bytes.size != PUBLIC_KEY_SIZE || bytes[0] != 0x04.toByte()) throw ProtocolException("bad public key encoding")
        val x = BigInteger(1, bytes.copyOfRange(1, 33))
        val y = BigInteger(1, bytes.copyOfRange(33, 65))
        val curve = params.curve
        val p = (curve.field as ECFieldFp).p
        if (x >= p || y >= p) throw ProtocolException("public key out of range")
        val left = y.modPow(BigInteger.TWO, p)
        val right = x.modPow(BigInteger.valueOf(3), p).add(curve.a.multiply(x)).add(curve.b).mod(p)
        if (left != right) throw ProtocolException("public key is not on P-256")
        return KeyFactory.getInstance("EC").generatePublic(ECPublicKeySpec(ECPoint(x, y), params))
    }

    fun sharedSecret(own: PrivateKey, other: PublicKey): ByteArray =
        KeyAgreement.getInstance("ECDH").run {
            init(own)
            doPhase(other, true)
            generateSecret()
        }

    private fun fixed32(v: BigInteger): ByteArray {
        val raw = v.toByteArray()
        return when {
            raw.size == 32 -> raw
            raw.size > 32 -> raw.copyOfRange(raw.size - 32, raw.size)
            else -> ByteArray(32 - raw.size) + raw
        }
    }
}
