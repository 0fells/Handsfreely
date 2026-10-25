package io.github.handsfreely.calls

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.telephony.TelephonyManager
import androidx.core.content.ContextCompat
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import org.json.JSONArray
import org.json.JSONObject

data class CallInfo(
    val id: String,
    val state: String,
    val number: String,
    val name: String?,
    val direction: String,
    val since: Long,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("id", id).put("state", state).put("number", number)
        .put("name", name ?: JSONObject.NULL).put("direction", direction).put("since", since)
}

fun List<CallInfo>.toJson(): JSONObject = JSONObject().put("calls", JSONArray(map { it.toJson() }))

object CallHub {
    private val _calls = MutableStateFlow<List<CallInfo>>(emptyList())
    val calls: StateFlow<List<CallInfo>> = _calls

    @Volatile
    var companionActive = false
        private set

    @Volatile
    var pendingOutgoing: String? = null

    private var receiver: BroadcastReceiver? = null
    private var legacyState = TelephonyManager.EXTRA_STATE_IDLE
    private var legacyNumber: String? = null
    private var legacySince = 0L
    private var legacyIncoming = false

    fun start(context: Context, nameFor: (String?) -> String?) {
        if (receiver != null) return
        receiver = object : BroadcastReceiver() {
            override fun onReceive(ctx: Context, intent: Intent) = onPhoneState(intent, nameFor)
        }
        ContextCompat.registerReceiver(
            context, receiver, IntentFilter(TelephonyManager.ACTION_PHONE_STATE_CHANGED), ContextCompat.RECEIVER_EXPORTED,
        )
    }

    fun stop(context: Context) {
        receiver?.let { runCatching { context.unregisterReceiver(it) } }
        receiver = null
    }

    fun publishFromCompanion(calls: List<CallInfo>) {
        companionActive = true
        _calls.value = calls
    }

    fun companionUnbound() {
        companionActive = false
        _calls.value = emptyList()
    }

    @Suppress("DEPRECATION")
    private fun onPhoneState(intent: Intent, nameFor: (String?) -> String?) {
        val state = intent.getStringExtra(TelephonyManager.EXTRA_STATE) ?: return
        val number = intent.getStringExtra(TelephonyManager.EXTRA_INCOMING_NUMBER)
        if (number != null) legacyNumber = number
        if (companionActive) return
        when (state) {
            TelephonyManager.EXTRA_STATE_RINGING -> {
                legacyIncoming = true
                legacySince = System.currentTimeMillis()
            }
            TelephonyManager.EXTRA_STATE_OFFHOOK -> {
                if (legacyState == TelephonyManager.EXTRA_STATE_IDLE) {
                    legacyIncoming = false
                    legacyNumber = pendingOutgoing ?: legacyNumber
                }
                legacySince = System.currentTimeMillis()
            }
            TelephonyManager.EXTRA_STATE_IDLE -> {
                legacyNumber = null
                pendingOutgoing = null
            }
        }
        legacyState = state
        _calls.value = when (state) {
            TelephonyManager.EXTRA_STATE_IDLE -> emptyList()
            else -> {
                val n = legacyNumber ?: ""
                listOf(
                    CallInfo(
                        id = "legacy",
                        state = if (state == TelephonyManager.EXTRA_STATE_RINGING) "ringing" else "active",
                        number = n,
                        name = nameFor(n),
                        direction = if (legacyIncoming) "in" else "out",
                        since = legacySince,
                    )
                )
            }
        }
    }
}
