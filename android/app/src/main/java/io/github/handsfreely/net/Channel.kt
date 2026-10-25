package io.github.handsfreely.net

import io.github.handsfreely.crypto.FrameCipher
import io.github.handsfreely.crypto.ProtocolException
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.EOFException
import java.io.InputStream
import java.io.OutputStream

class FrameStream(input: InputStream, output: OutputStream) {
    private val input = DataInputStream(input)
    private val output = DataOutputStream(output.buffered())

    fun read(): ByteArray? {
        val first = input.read()
        if (first < 0) return null
        val rest = ByteArray(3)
        input.readFully(rest)
        val length = (first.toLong() shl 24) or
            ((rest[0].toLong() and 0xFF) shl 16) or
            ((rest[1].toLong() and 0xFF) shl 8) or
            (rest[2].toLong() and 0xFF)
        if (length > MAX_FRAME) throw ProtocolException("frame too large: $length")
        val body = ByteArray(length.toInt())
        input.readFully(body)
        return body
    }

    @Synchronized
    fun write(body: ByteArray) {
        if (body.size > MAX_FRAME) throw IllegalArgumentException("frame too large")
        output.writeInt(body.size)
        output.write(body)
        output.flush()
    }

    companion object {
        const val MAX_FRAME = 8 * 1024 * 1024
    }
}

class SecureChannel(private val frames: FrameStream, sendKey: ByteArray, receiveKey: ByteArray) {
    private val send = FrameCipher(sendKey)
    private val receive = FrameCipher(receiveKey)

    @Synchronized
    fun send(plaintext: ByteArray) = frames.write(send.encrypt(plaintext))

    fun receive(): ByteArray? = frames.read()?.let(receive::decrypt)

    fun receiveOrThrow(): ByteArray = receive() ?: throw EOFException("connection closed")
}
