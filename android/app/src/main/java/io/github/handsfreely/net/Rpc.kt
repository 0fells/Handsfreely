package io.github.handsfreely.net

import io.github.handsfreely.crypto.ProtocolException
import org.json.JSONException
import org.json.JSONObject

class Message(val type: String, val id: Long?, val data: JSONObject?) {
    fun serialize(): ByteArray {
        val o = JSONObject().put("t", type)
        if (id != null) o.put("id", id)
        if (data != null) o.put("d", data)
        return o.toString().toByteArray(Charsets.UTF_8)
    }

    companion object {
        fun parse(bytes: ByteArray): Message = try {
            val o = JSONObject(String(bytes, Charsets.UTF_8))
            Message(o.getString("t"), if (o.has("id")) o.getLong("id") else null, o.optJSONObject("d"))
        } catch (e: JSONException) {
            throw ProtocolException("bad message", e)
        }
    }
}

object ErrorCodes {
    const val UNKNOWN_TYPE = "unknown_type"
    const val BAD_REQUEST = "bad_request"
    const val PERMISSION = "permission"
    const val UNSUPPORTED = "unsupported"
    const val FAILED = "failed"
}

class RpcException(val code: String, message: String) : Exception(message)

fun interface RequestHandler {
    suspend fun handle(type: String, data: JSONObject?): JSONObject?
}
