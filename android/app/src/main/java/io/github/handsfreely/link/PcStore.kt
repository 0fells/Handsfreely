package io.github.handsfreely.link

import android.content.Context
import android.util.Base64
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import org.json.JSONArray
import org.json.JSONObject
import java.security.MessageDigest

data class PcRecord(
    val id: String,
    val name: String,
    val bluetoothAddress: String,
    val publicKey: ByteArray,
    val pairedAt: Long,
) {
    val colonAddress: String get() = bluetoothAddress.chunked(2).joinToString(":")

    companion object {
        fun idFor(publicKey: ByteArray): String =
            MessageDigest.getInstance("SHA-256").digest(publicKey).copyOf(8).joinToString("") { "%02x".format(it) }
    }
}

class PcStore(context: Context) {
    private val prefs = context.getSharedPreferences("pcs", Context.MODE_PRIVATE)
    private val _pcs = MutableStateFlow(load())

    val pcs: StateFlow<List<PcRecord>> = _pcs

    fun find(id: String) = _pcs.value.firstOrNull { it.id == id }

    @Synchronized
    fun add(pc: PcRecord) {
        save(_pcs.value.filter { it.id != pc.id } + pc)
    }

    @Synchronized
    fun rename(id: String, name: String) {
        save(_pcs.value.map { if (it.id == id) it.copy(name = name) else it })
    }

    @Synchronized
    fun remove(id: String) {
        save(_pcs.value.filter { it.id != id })
    }

    private fun load(): List<PcRecord> = runCatching {
        val arr = JSONArray(prefs.getString("list", "[]"))
        (0 until arr.length()).map {
            val o = arr.getJSONObject(it)
            PcRecord(
                o.getString("id"), o.getString("name"), o.getString("mac"),
                Base64.decode(o.getString("key"), Base64.NO_WRAP), o.getLong("pairedAt"),
            )
        }
    }.getOrDefault(emptyList())

    private fun save(list: List<PcRecord>) {
        val arr = JSONArray()
        list.forEach {
            arr.put(
                JSONObject()
                    .put("id", it.id).put("name", it.name).put("mac", it.bluetoothAddress)
                    .put("key", Base64.encodeToString(it.publicKey, Base64.NO_WRAP)).put("pairedAt", it.pairedAt)
            )
        }
        prefs.edit().putString("list", arr.toString()).apply()
        _pcs.value = list
    }
}
