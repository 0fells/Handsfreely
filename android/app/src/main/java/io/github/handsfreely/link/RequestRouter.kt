package io.github.handsfreely.link

import android.Manifest
import android.content.Context
import io.github.handsfreely.calls.CallController
import io.github.handsfreely.data.CallLogSource
import io.github.handsfreely.data.ContactsSource
import io.github.handsfreely.data.DeviceState
import io.github.handsfreely.net.ErrorCodes
import io.github.handsfreely.net.RequestHandler
import io.github.handsfreely.net.RpcException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject

class RequestRouter(private val context: Context) : RequestHandler {
    val contacts = ContactsSource(context)
    private val callLog = CallLogSource(context)
    val calls = CallController(context)

    override suspend fun handle(type: String, data: JSONObject?): JSONObject? = withContext(Dispatchers.IO) {
        try {
            route(type, data ?: JSONObject())
        } catch (e: SecurityException) {
            throw RpcException(ErrorCodes.PERMISSION, e.message ?: "permission denied")
        }
    }

    private suspend fun route(type: String, d: JSONObject): JSONObject? = when (type) {
        "ping" -> null
        "device.info" -> DeviceState.info(context, calls.fullControl)
        "contacts.list" -> need(Manifest.permission.READ_CONTACTS) { contacts.list() }
        "contacts.photo" -> need(Manifest.permission.READ_CONTACTS) { contacts.photo(d.str("id")) }
        "calllog.list" -> need(Manifest.permission.READ_CALL_LOG) { callLog.list(d.optInt("limit", 100), d.optLongOrNull("before")) }
        "call.dial" -> { calls.dial(d.str("number")); null }
        "call.answer" -> { calls.answer(); null }
        "call.reject" -> { calls.reject(); null }
        "call.end" -> { calls.end(); null }
        "call.hold" -> { calls.hold(d.optBoolean("on")); null }
        "call.mute" -> { calls.mute(d.optBoolean("on")); null }
        "call.dtmf" -> { calls.dtmf(d.str("digit").firstOrNull { it.isDigit() || it == '*' || it == '#' } ?: bad("digit")); null }
        "call.audio" -> { calls.audioToPhone(d.str("target") == "phone"); null }
        else -> throw RpcException(ErrorCodes.UNKNOWN_TYPE, type)
    }

    private inline fun <T> need(permission: String, block: () -> T): T {
        if (!DeviceState.has(context, permission)) throw RpcException(ErrorCodes.PERMISSION, permission)
        return block()
    }

    private fun JSONObject.str(name: String): String = optString(name, "").ifEmpty { bad(name) }

    private fun JSONObject.optLongOrNull(name: String): Long? = if (has(name) && !isNull(name)) optLong(name) else null

    private fun bad(field: String): Nothing = throw RpcException(ErrorCodes.BAD_REQUEST, "missing $field")
}
