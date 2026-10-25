package io.github.handsfreely.data

import android.content.ContentResolver
import android.content.Context
import android.os.Bundle
import android.provider.CallLog
import org.json.JSONArray
import org.json.JSONObject

class CallLogSource(private val context: Context) {

    fun list(limit: Int, before: Long?): JSONObject {
        val args = Bundle().apply {
            if (before != null) {
                putString(ContentResolver.QUERY_ARG_SQL_SELECTION, "${CallLog.Calls.DATE} < ?")
                putStringArray(ContentResolver.QUERY_ARG_SQL_SELECTION_ARGS, arrayOf(before.toString()))
            }
            putString(ContentResolver.QUERY_ARG_SQL_SORT_ORDER, "${CallLog.Calls.DATE} DESC")
            putInt(ContentResolver.QUERY_ARG_LIMIT, limit.coerceIn(1, 1000))
        }
        val arr = JSONArray()
        context.contentResolver.query(
            CallLog.Calls.CONTENT_URI,
            arrayOf(CallLog.Calls._ID, CallLog.Calls.NUMBER, CallLog.Calls.CACHED_NAME, CallLog.Calls.TYPE, CallLog.Calls.DATE, CallLog.Calls.DURATION),
            args, null,
        )?.use { c ->
            while (c.moveToNext()) {
                arr.put(
                    JSONObject()
                        .put("id", c.getLong(0).toString())
                        .put("number", c.getString(1) ?: "")
                        .put("name", c.getString(2)?.takeIf { it.isNotBlank() } ?: JSONObject.NULL)
                        .put("type", type(c.getInt(3)))
                        .put("date", c.getLong(4))
                        .put("duration", c.getInt(5))
                )
            }
        }
        return JSONObject().put("calls", arr)
    }

    private fun type(t: Int) = when (t) {
        CallLog.Calls.INCOMING_TYPE, CallLog.Calls.ANSWERED_EXTERNALLY_TYPE -> "in"
        CallLog.Calls.OUTGOING_TYPE -> "out"
        CallLog.Calls.MISSED_TYPE -> "missed"
        CallLog.Calls.REJECTED_TYPE -> "rejected"
        CallLog.Calls.BLOCKED_TYPE -> "blocked"
        CallLog.Calls.VOICEMAIL_TYPE -> "voicemail"
        else -> "in"
    }
}
