package io.github.handsfreely.link

import android.annotation.SuppressLint
import android.app.Notification
import android.app.PendingIntent
import android.app.Service
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothServerSocket
import android.bluetooth.BluetoothSocket
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.database.ContentObserver
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.provider.CallLog
import android.provider.ContactsContract
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import io.github.handsfreely.HandsfreelyApp
import io.github.handsfreely.R
import io.github.handsfreely.calls.CallHub
import io.github.handsfreely.calls.toJson
import io.github.handsfreely.crypto.HandshakeMode
import io.github.handsfreely.crypto.HandshakeRejectedException
import io.github.handsfreely.crypto.HandshakeStatus
import io.github.handsfreely.data.DeviceState
import io.github.handsfreely.net.PairingCode
import io.github.handsfreely.ui.MainActivity
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import java.io.IOException
import java.util.concurrent.ConcurrentHashMap

class LinkService : Service() {
    private val scope = CoroutineScope(SupervisorJob())
    private val links = ConcurrentHashMap<String, PcLink>()
    private val loops = ConcurrentHashMap<String, Job>()
    private val wake = ConcurrentHashMap<String, Channel<Unit>>()
    private val main = Handler(Looper.getMainLooper())
    private lateinit var router: RequestRouter
    private lateinit var store: PcStore
    private var lastStatus = ""

    @Volatile
    private var serverSocket: BluetoothServerSocket? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        instance = this
        store = HandsfreelyApp.store(this)
        router = RequestRouter(this)
        ServiceCompat.startForeground(this, NOTIFICATION_ID, notification(), ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)

        CallHub.start(this) { router.contacts.nameFor(it) }
        scope.launch { CallHub.calls.drop(1).collect { broadcast("call.state", it.toJson()) } }
        scope.launch {
            store.pcs.collect { list ->
                val ids = list.map { it.id }.toSet()
                (loops.keys - ids).forEach { id ->
                    loops.remove(id)?.cancel()
                    links.remove(id)?.close()
                    LinkState.remove(id)
                }
                list.filter { !loops.containsKey(it.id) }.forEach { pc -> loops[pc.id] = scope.launch { maintain(pc.id) } }
                updateNotification()
            }
        }

        registerSystemReceivers()
        startServer()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        wakeAll()
        return START_STICKY
    }

    override fun onDestroy() {
        instance = null
        CallHub.stop(this)
        runCatching { unregisterReceiver(systemReceiver) }
        contentResolver.unregisterContentObserver(contactsObserver)
        contentResolver.unregisterContentObserver(callLogObserver)
        links.values.forEach { it.close() }
        runCatching { serverSocket?.close() }
        scope.cancel()
        super.onDestroy()
    }

    private suspend fun maintain(pcId: String) {
        val signal = Channel<Unit>(Channel.CONFLATED)
        wake[pcId] = signal
        var backoff = MIN_BACKOFF_MS
        while (true) {
            val pc = store.find(pcId) ?: return
            if (links.containsKey(pcId)) {
                signal.receive()
                backoff = MIN_BACKOFF_MS
                continue
            }
            LinkState.set(pcId, PcStatus.CONNECTING)
            try {
                startLink(pc, PcLink.connect(this, pc.colonAddress, pc.publicKey, HandshakeMode.RESUME, null))
                continue
            } catch (e: HandshakeRejectedException) {
                if (e.status == HandshakeStatus.UNKNOWN_DEVICE) {
                    LinkState.set(pcId, PcStatus.FORGOTTEN)
                    updateNotification()
                    return
                }
            } catch (e: Exception) {
            }
            if (!links.containsKey(pcId)) {
                LinkState.set(pcId, PcStatus.OFFLINE)
                updateNotification()
            }
            withTimeoutOrNull(backoff) { signal.receive() }
            backoff = (backoff * 2).coerceAtMost(MAX_BACKOFF_MS)
        }
    }

    private fun startLink(pc: PcRecord, link: PcLink) {
        links.put(pc.id, link)?.takeIf { it !== link }?.close()
        onConnected(pc, link)
        scope.launch {
            runCatching { link.run(scope, router) }
            link.close()
            if (links.remove(pc.id, link)) {
                LinkState.set(pc.id, PcStatus.OFFLINE)
                updateNotification()
            }
            wake[pc.id]?.trySend(Unit)
        }
    }

    @SuppressLint("MissingPermission")
    private fun startServer() {
        scope.launch(Dispatchers.IO) {
            while (isActive) {
                val adapter = getSystemService(BluetoothManager::class.java)?.adapter
                val server = if (adapter?.isEnabled == true) {
                    runCatching { adapter.listenUsingRfcommWithServiceRecord("Handsfreely", PcLink.PHONE_SERVICE_UUID) }.getOrNull()
                } else null
                if (server == null) {
                    delay(5_000)
                    continue
                }
                serverSocket = server
                try {
                    while (isActive) {
                        val socket = server.accept()
                        launch { acceptFromPc(socket) }
                    }
                } catch (e: IOException) {
                } finally {
                    runCatching { server.close() }
                }
                delay(1_000)
            }
        }
    }

    private suspend fun acceptFromPc(socket: BluetoothSocket) {
        val mac = socket.remoteDevice.address.replace(":", "").uppercase()
        val pc = store.pcs.value.firstOrNull { it.bluetoothAddress.equals(mac, ignoreCase = true) }
        if (pc == null) {
            runCatching { socket.close() }
            return
        }
        try {
            startLink(pc, PcLink.handshake(this, socket, pc.publicKey, HandshakeMode.RESUME, null))
        } catch (e: Exception) {
            runCatching { socket.close() }
        }
    }

    private fun onConnected(pc: PcRecord, link: PcLink) {
        if (link.pcName.isNotBlank() && link.pcName != pc.name) store.rename(pc.id, link.pcName)
        LinkState.set(pc.id, PcStatus.CONNECTED)
        updateNotification()
        if (CallHub.calls.value.isNotEmpty()) link.sendEvent("call.state", CallHub.calls.value.toJson())
    }

    suspend fun pair(code: PairingCode) {
        LinkState.setPairing(PairingState.Working(code.pcName))
        try {
            val link = PcLink.connect(this, code.colonAddress, code.pcPublicKey, HandshakeMode.PAIR, code.token)
            val pc = PcRecord(
                PcRecord.idFor(code.pcPublicKey), link.pcName.ifBlank { code.pcName }, code.bluetoothAddress,
                code.pcPublicKey, System.currentTimeMillis(),
            )
            startLink(pc, link)
            store.add(pc)
            LinkState.setPairing(PairingState.Done(pc.name))
        } catch (e: HandshakeRejectedException) {
            LinkState.setPairing(
                PairingState.Failed(if (e.status == HandshakeStatus.PAIRING_CLOSED) PairingFailure.WINDOW_CLOSED else PairingFailure.REJECTED)
            )
        } catch (e: SecurityException) {
            LinkState.setPairing(PairingState.Failed(PairingFailure.NO_PERMISSION))
        } catch (e: Exception) {
            val off = getSystemService(BluetoothManager::class.java)?.adapter?.isEnabled == false
            LinkState.setPairing(PairingState.Failed(if (off) PairingFailure.BLUETOOTH_OFF else PairingFailure.CONNECT_FAILED, e.message))
        }
    }

    fun forget(pcId: String) {
        loops.remove(pcId)?.cancel()
        links.remove(pcId)?.close()
        store.remove(pcId)
        LinkState.remove(pcId)
    }

    private fun broadcast(type: String, data: org.json.JSONObject? = null) {
        links.values.forEach { it.sendEvent(type, data) }
    }

    private fun wakeAll() = wake.values.forEach { it.trySend(Unit) }

    private val systemReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            when (intent.action) {
                BluetoothAdapter.ACTION_STATE_CHANGED ->
                    if (intent.getIntExtra(BluetoothAdapter.EXTRA_STATE, 0) == BluetoothAdapter.STATE_ON) wakeAll()
                BluetoothDevice.ACTION_ACL_CONNECTED -> wakeAll()
                Intent.ACTION_BATTERY_CHANGED -> {
                    val status = DeviceState.status(context)
                    val key = "${status.optInt("battery")}/${status.optBoolean("charging")}"
                    if (key != lastStatus) {
                        lastStatus = key
                        broadcast("device.status", status)
                    }
                }
            }
        }
    }

    private val contactsObserver = debouncedObserver { broadcast("contacts.changed") }
    private val callLogObserver = debouncedObserver { broadcast("calllog.changed") }

    private fun debouncedObserver(action: () -> Unit) = object : ContentObserver(main) {
        private val run = Runnable(action)
        override fun onChange(selfChange: Boolean) {
            main.removeCallbacks(run)
            main.postDelayed(run, 3000)
        }
    }

    private fun registerSystemReceivers() {
        val filter = IntentFilter().apply {
            addAction(BluetoothAdapter.ACTION_STATE_CHANGED)
            addAction(BluetoothDevice.ACTION_ACL_CONNECTED)
            addAction(Intent.ACTION_BATTERY_CHANGED)
        }
        ContextCompat.registerReceiver(this, systemReceiver, filter, ContextCompat.RECEIVER_EXPORTED)
        runCatching { contentResolver.registerContentObserver(ContactsContract.Contacts.CONTENT_URI, true, contactsObserver) }
        runCatching { contentResolver.registerContentObserver(CallLog.Calls.CONTENT_URI, true, callLogObserver) }
    }

    private fun notification(): Notification {
        val connected = store.pcs.value.filter { LinkState.status.value[it.id] == PcStatus.CONNECTED }.map { it.name }
        val text = when {
            store.pcs.value.isEmpty() -> getString(R.string.notif_no_pcs)
            connected.isEmpty() -> getString(R.string.notif_waiting)
            else -> getString(R.string.notif_connected, connected.joinToString(", "))
        }
        val open = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        return NotificationCompat.Builder(this, HandsfreelyApp.CHANNEL_LINK)
            .setSmallIcon(R.drawable.ic_stat_link)
            .setColor(ContextCompat.getColor(this, R.color.ic_launcher_background))
            .setContentTitle(getString(R.string.app_name))
            .setContentText(text)
            .setContentIntent(open)
            .setOngoing(true)
            .setSilent(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }

    private fun updateNotification() {
        main.post {
            runCatching { getSystemService(android.app.NotificationManager::class.java).notify(NOTIFICATION_ID, notification()) }
        }
    }

    companion object {
        private const val NOTIFICATION_ID = 1
        private const val MIN_BACKOFF_MS = 5_000L
        private const val MAX_BACKOFF_MS = 20_000L

        @Volatile
        var instance: LinkService? = null
            private set

        fun start(context: Context) {
            if (!DeviceState.bluetoothGranted(context)) return
            ContextCompat.startForegroundService(context, Intent(context, LinkService::class.java))
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, LinkService::class.java))
        }
    }
}
