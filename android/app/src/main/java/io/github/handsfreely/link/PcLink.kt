package io.github.handsfreely.link

import android.annotation.SuppressLint
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothSocket
import android.content.Context
import android.os.Build
import io.github.handsfreely.BuildConfigInfo
import io.github.handsfreely.crypto.Handshake
import io.github.handsfreely.crypto.HandshakeMode
import io.github.handsfreely.crypto.P256
import io.github.handsfreely.crypto.ProtocolException
import io.github.handsfreely.data.DeviceState
import io.github.handsfreely.net.ErrorCodes
import io.github.handsfreely.net.FrameStream
import io.github.handsfreely.net.Message
import io.github.handsfreely.net.RequestHandler
import io.github.handsfreely.net.RpcException
import io.github.handsfreely.net.SecureChannel
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.launch
import kotlinx.coroutines.runInterruptible
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.IOException
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

class PcLink private constructor(
    private val socket: BluetoothSocket,
    private val channel: SecureChannel,
    val pcName: String,
) {
    private val sender = Executors.newSingleThreadExecutor()

    @Volatile
    var closed = false
        private set

    fun sendEvent(type: String, data: JSONObject? = null) {
        if (closed) return
        sender.execute {
            try {
                channel.send(Message(type, null, data).serialize())
            } catch (e: IOException) {
                close()
            }
        }
    }

    suspend fun run(scope: CoroutineScope, handler: RequestHandler) = withContext(Dispatchers.IO) {
        try {
            while (!closed) {
                val frame = channel.receive() ?: break
                val m = Message.parse(frame)
                val id = m.id ?: continue
                scope.launch {
                    val reply = try {
                        Message("result", id, handler.handle(m.type, m.data))
                    } catch (e: RpcException) {
                        Message("error", id, JSONObject().put("code", e.code).put("message", e.message ?: ""))
                    } catch (e: Exception) {
                        Message("error", id, JSONObject().put("code", ErrorCodes.FAILED).put("message", e.message ?: e.javaClass.simpleName))
                    }
                    if (!closed) sender.execute { runCatching { channel.send(reply.serialize()) }.onFailure { close() } }
                }
            }
        } catch (e: IOException) {
        } finally {
            close()
        }
    }

    fun close() {
        if (closed) return
        closed = true
        runCatching { socket.close() }
        sender.shutdown()
    }

    companion object {
        val SERVICE_UUID: UUID = UUID.fromString("e7530220-2b8a-460d-925c-0267c9b9b467")
        val PHONE_SERVICE_UUID: UUID = UUID.fromString("1c3afcb6-f84e-400c-9554-6c9dd39596a3")
        private const val HANDSHAKE_TIMEOUT_MS = 20_000L

        @SuppressLint("MissingPermission")
        suspend fun connect(
            context: Context, colonAddress: String, pcPublicKey: ByteArray, mode: HandshakeMode, token: ByteArray?,
        ): PcLink = withContext(Dispatchers.IO) {
            val adapter = context.getSystemService(BluetoothManager::class.java)?.adapter
                ?: throw IOException("no bluetooth adapter")
            if (!adapter.isEnabled) throw IOException("bluetooth is off")
            val socket = adapter.getRemoteDevice(colonAddress).createRfcommSocketToServiceRecord(SERVICE_UUID)
            handshake(context, socket, pcPublicKey, mode, token, connectFirst = true)
        }

        @OptIn(ExperimentalCoroutinesApi::class)
        suspend fun handshake(
            context: Context, socket: BluetoothSocket, pcPublicKey: ByteArray, mode: HandshakeMode, token: ByteArray?,
            connectFirst: Boolean = false,
        ): PcLink = withContext(Dispatchers.IO) {
            val watchdog = Executors.newSingleThreadScheduledExecutor()
            val timer = watchdog.schedule({ runCatching { socket.close() } }, HANDSHAKE_TIMEOUT_MS, TimeUnit.MILLISECONDS)
            try {
                if (connectFirst) runInterruptible { socket.connect() }
                val identity = Identity.get(context)
                val frames = FrameStream(socket.inputStream, socket.outputStream)
                val eph = P256.generate()
                val m1 = Handshake.buildM1(mode, identity.publicKey, P256.encode(eph.public))
                frames.write(m1)
                val m2 = frames.read() ?: throw IOException("closed before M2")
                val pcEph = P256.decode(Handshake.parseM2(m2))
                val keys = Handshake.deriveKeys(
                    pcPublicKey, m1, m2,
                    P256.sharedSecret(eph.private, pcEph),
                    P256.sharedSecret(eph.private, P256.decode(pcPublicKey)),
                    P256.sharedSecret(identity.privateKey, pcEph),
                    if (mode == HandshakeMode.PAIR) token ?: ByteArray(0) else ByteArray(0),
                )
                val channel = SecureChannel(frames, keys.phoneToPc, keys.pcToPhone)
                val hello = JSONObject()
                    .put("name", DeviceState.deviceName(context))
                    .put("model", DeviceState.model())
                    .put("appVersion", BuildConfigInfo.versionName(context))
                    .put("sdk", Build.VERSION.SDK_INT)
                channel.send(Message("hello", null, hello).serialize())
                val welcome = Message.parse(channel.receiveOrThrow())
                if (welcome.type != "welcome") throw ProtocolException("expected welcome")
                timer.cancel(false)
                PcLink(socket, channel, welcome.data?.optString("name").orEmpty())
            } catch (e: Exception) {
                runCatching { socket.close() }
                throw e
            } finally {
                watchdog.shutdownNow()
            }
        }
    }
}
