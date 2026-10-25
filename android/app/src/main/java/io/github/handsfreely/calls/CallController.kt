package io.github.handsfreely.calls

import android.Manifest
import android.content.Context
import android.net.Uri
import android.os.Bundle
import android.telecom.TelecomManager
import io.github.handsfreely.data.DeviceState
import io.github.handsfreely.net.ErrorCodes
import io.github.handsfreely.net.RpcException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class CallController(private val context: Context) {
    private val telecom get() = context.getSystemService(TelecomManager::class.java)

    val fullControl: Boolean get() = DeviceState.has(context, Manifest.permission.MANAGE_ONGOING_CALLS)

    suspend fun dial(number: String) = onMain {
        require(Manifest.permission.CALL_PHONE)
        if (number.isBlank()) throw RpcException(ErrorCodes.BAD_REQUEST, "empty number")
        CallHub.pendingOutgoing = number
        telecom.placeCall(Uri.fromParts("tel", number, null), Bundle())
    }

    @Suppress("DEPRECATION")
    suspend fun answer() = onMain {
        if (CompanionInCallService.instance?.answer() == true) return@onMain
        require(Manifest.permission.ANSWER_PHONE_CALLS)
        telecom.acceptRingingCall()
    }

    @Suppress("DEPRECATION")
    suspend fun reject() = onMain {
        if (CompanionInCallService.instance?.reject() == true) return@onMain
        require(Manifest.permission.ANSWER_PHONE_CALLS)
        telecom.endCall()
    }

    @Suppress("DEPRECATION")
    suspend fun end() = onMain {
        if (CompanionInCallService.instance?.end() == true) return@onMain
        require(Manifest.permission.ANSWER_PHONE_CALLS)
        telecom.endCall()
    }

    suspend fun hold(on: Boolean) = companion { it.hold(on) }
    suspend fun mute(on: Boolean) = companion { it.mute(on) }
    suspend fun dtmf(digit: Char) = companion { it.dtmf(digit) }
    suspend fun audioToPhone(toPhone: Boolean) = companion { it.audioToPhone(toPhone) }

    private suspend fun companion(action: (CompanionInCallService) -> Boolean) = onMain {
        val service = CompanionInCallService.instance ?: throw RpcException(ErrorCodes.UNSUPPORTED, "call companion is not bound")
        if (!action(service)) throw RpcException(ErrorCodes.FAILED, "no suitable call")
    }

    private fun require(permission: String) {
        if (!DeviceState.has(context, permission)) throw RpcException(ErrorCodes.PERMISSION, permission)
    }

    private suspend fun onMain(block: () -> Unit) = withContext(Dispatchers.Main) { block() }
}
