package io.github.handsfreely.data

import android.content.ContentUris
import android.content.Context
import android.net.Uri
import android.provider.ContactsContract
import android.provider.ContactsContract.CommonDataKinds.Phone
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject

class ContactsSource(private val context: Context) {
    private val resolver get() = context.contentResolver

    fun list(): JSONObject {
        val contacts = LinkedHashMap<Long, JSONObject>()
        val phones = HashMap<Long, JSONArray>()
        val seen = HashMap<Long, MutableSet<String>>()
        resolver.query(
            Phone.CONTENT_URI,
            arrayOf(Phone.CONTACT_ID, Phone.DISPLAY_NAME_PRIMARY, Phone.NUMBER, Phone.TYPE, Phone.STARRED, Phone.PHOTO_THUMBNAIL_URI),
            null, null, "${Phone.DISPLAY_NAME_PRIMARY} COLLATE LOCALIZED ASC",
        )?.use { c ->
            while (c.moveToNext()) {
                val id = c.getLong(0)
                val number = c.getString(2) ?: continue
                if (!seen.getOrPut(id) { HashSet() }.add(number.filter { it.isDigit() || it == '+' })) continue
                contacts.getOrPut(id) {
                    JSONObject()
                        .put("id", id.toString())
                        .put("name", c.getString(1) ?: number)
                        .put("starred", c.getInt(4) != 0)
                        .put("hasPhoto", c.getString(5) != null)
                }
                phones.getOrPut(id) { JSONArray() }.put(JSONObject().put("number", number).put("type", phoneType(c.getInt(3))))
            }
        }
        val arr = JSONArray()
        contacts.forEach { (id, o) -> arr.put(o.put("phones", phones[id] ?: JSONArray())) }
        return JSONObject().put("contacts", arr)
    }

    fun photo(id: String): JSONObject {
        val uri = ContentUris.withAppendedId(ContactsContract.Contacts.CONTENT_URI, id.toLongOrNull() ?: return JSONObject().put("data", JSONObject.NULL))
        val bytes = ContactsContract.Contacts.openContactPhotoInputStream(resolver, uri, false)?.use { it.readBytes() }
        return JSONObject().put("data", bytes?.let { Base64.encodeToString(it, Base64.NO_WRAP) } ?: JSONObject.NULL)
    }

    fun nameFor(number: String?): String? {
        if (number.isNullOrBlank()) return null
        val uri = Uri.withAppendedPath(ContactsContract.PhoneLookup.CONTENT_FILTER_URI, Uri.encode(number))
        return runCatching {
            resolver.query(uri, arrayOf(ContactsContract.PhoneLookup.DISPLAY_NAME), null, null, null)?.use {
                if (it.moveToFirst()) it.getString(0) else null
            }
        }.getOrNull()
    }

    private fun phoneType(t: Int) = when (t) {
        Phone.TYPE_MOBILE, Phone.TYPE_WORK_MOBILE -> "mobile"
        Phone.TYPE_HOME -> "home"
        Phone.TYPE_WORK, Phone.TYPE_COMPANY_MAIN -> "work"
        else -> "other"
    }
}
