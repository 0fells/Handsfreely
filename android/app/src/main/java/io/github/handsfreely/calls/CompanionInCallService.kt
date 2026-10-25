package io.github.handsfreely.calls

import android.os.Handler
import android.os.Looper
import android.telecom.Call
import android.telecom.CallAudioState
import android.telecom.InCallService
import android.telecom.VideoProfile

class CompanionInCallService : InCallService() {

    private val callback = object : Call.Callback() {
        override fun onStateChanged(call: Call, state: Int) = publish()
        override fun onDetailsChanged(call: Call, details: Call.Details) = publish()
    }

    override fun onCreate() {
        super.onCreate()
        instance = this
    }

    override fun onDestroy() {
        instance = null
        CallHub.companionUnbound()
        super.onDestroy()
    }

    override fun onCallAdded(call: Call) {
        call.registerCallback(callback)
        publish()
    }

    override fun onCallRemoved(call: Call) {
        call.unregisterCallback(callback)
        publish()
    }

    private fun publish() {
        CallHub.publishFromCompanion(calls.map { it.toInfo() })
    }

    private fun Call.toInfo(): CallInfo {
        val d = details
        val number = d.handle?.schemeSpecificPart ?: ""
        val incoming = d.callDirection == Call.Details.DIRECTION_INCOMING
        return CallInfo(
            id = System.identityHashCode(this).toString(),
            state = when (state()) {
                Call.STATE_RINGING -> "ringing"
                Call.STATE_DIALING, Call.STATE_CONNECTING, Call.STATE_PULLING_CALL, Call.STATE_SELECT_PHONE_ACCOUNT -> "dialing"
                Call.STATE_ACTIVE -> "active"
                Call.STATE_HOLDING -> "holding"
                else -> "disconnected"
            },
            number = number,
            name = d.contactDisplayName ?: d.callerDisplayName,
            direction = if (incoming) "in" else "out",
            since = if (d.connectTimeMillis > 0) d.connectTimeMillis else d.creationTimeMillis,
        )
    }

    @Suppress("DEPRECATION")
    private fun Call.state(): Int = if (android.os.Build.VERSION.SDK_INT >= 31) details.state else state

    private fun ringing() = calls.firstOrNull { it.state() == Call.STATE_RINGING }
    private fun current() = calls.firstOrNull { it.state() == Call.STATE_ACTIVE }
        ?: calls.firstOrNull { it.state() != Call.STATE_DISCONNECTED }

    fun answer(): Boolean = ringing()?.also { it.answer(VideoProfile.STATE_AUDIO_ONLY) } != null
    fun reject(): Boolean = ringing()?.also { it.reject(false, null) } != null
    fun end(): Boolean = current()?.also { it.disconnect() } != null

    fun hold(on: Boolean): Boolean {
        val call = if (on) calls.firstOrNull { it.state() == Call.STATE_ACTIVE } else calls.firstOrNull { it.state() == Call.STATE_HOLDING }
        call ?: return false
        if (on) call.hold() else call.unhold()
        return true
    }

    fun mute(on: Boolean): Boolean {
        setMuted(on)
        return true
    }

    fun dtmf(digit: Char): Boolean {
        val call = calls.firstOrNull { it.state() == Call.STATE_ACTIVE } ?: return false
        call.playDtmfTone(digit)
        Handler(Looper.getMainLooper()).postDelayed({ call.stopDtmfTone() }, 200)
        return true
    }

    @Suppress("DEPRECATION")
    fun audioToPhone(toPhone: Boolean): Boolean {
        val state = callAudioState ?: return false
        val route = if (toPhone) {
            if (state.supportedRouteMask and CallAudioState.ROUTE_EARPIECE != 0) CallAudioState.ROUTE_EARPIECE else CallAudioState.ROUTE_SPEAKER
        } else {
            CallAudioState.ROUTE_BLUETOOTH
        }
        setAudioRoute(route)
        return true
    }

    companion object {
        @Volatile
        var instance: CompanionInCallService? = null
            private set
    }
}
