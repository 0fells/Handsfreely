package io.github.handsfreely.data

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.os.BatteryManager
import android.os.Build
import android.provider.Settings
import android.telephony.TelephonyManager
import androidx.core.content.ContextCompat
import org.json.JSONArray
import org.json.JSONObject

object DeviceState {

    fun status(context: Context): JSONObject {
        val battery = context.registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
        val level = battery?.getIntExtra(BatteryManager.EXTRA_LEVEL, -1) ?: -1
        val scale = battery?.getIntExtra(BatteryManager.EXTRA_SCALE, 100) ?: 100
        val plugged = (battery?.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) ?: 0) != 0
        val telephony = context.getSystemService(TelephonyManager::class.java)
        val signal = runCatching { telephony.signalStrength?.level }.getOrNull() ?: 0
        return JSONObject()
            .put("battery", if (level >= 0 && scale > 0) level * 100 / scale else 0)
            .put("charging", plugged)
            .put("signal", signal.coerceIn(0, 4))
            .put("operator", telephony.networkOperatorName?.takeIf { it.isNotBlank() } ?: JSONObject.NULL)
    }

    fun info(context: Context, fullCallControl: Boolean): JSONObject =
        status(context)
            .put("name", deviceName(context))
            .put("model", model())
            .put("callControl", if (fullCallControl) "full" else "basic")
            .put("permissions", JSONArray(grantedGroups(context)))

    fun deviceName(context: Context): String =
        Settings.Global.getString(context.contentResolver, Settings.Global.DEVICE_NAME)?.takeIf { it.isNotBlank() } ?: Build.MODEL

    fun model(): String = "${Build.MANUFACTURER.replaceFirstChar { it.uppercase() }} ${Build.MODEL}"

    val groups: Map<String, List<String>> = mapOf(
        "contacts" to listOf(Manifest.permission.READ_CONTACTS),
        "calllog" to listOf(Manifest.permission.READ_CALL_LOG),
        "phone" to listOf(Manifest.permission.READ_PHONE_STATE, Manifest.permission.CALL_PHONE, Manifest.permission.ANSWER_PHONE_CALLS),
    )

    fun has(context: Context, permission: String) =
        ContextCompat.checkSelfPermission(context, permission) == PackageManager.PERMISSION_GRANTED

    fun grantedGroups(context: Context): List<String> =
        groups.filter { (_, perms) -> perms.all { has(context, it) } }.keys.toList()

    fun bluetoothGranted(context: Context): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.S || has(context, Manifest.permission.BLUETOOTH_CONNECT)
}
