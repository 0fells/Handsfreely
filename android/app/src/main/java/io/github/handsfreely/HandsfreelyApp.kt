package io.github.handsfreely

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import io.github.handsfreely.link.PcStore

class HandsfreelyApp : Application() {
    override fun onCreate() {
        super.onCreate()
        val channel = NotificationChannel(CHANNEL_LINK, getString(R.string.channel_link), NotificationManager.IMPORTANCE_LOW)
        getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }

    companion object {
        const val CHANNEL_LINK = "link"

        @Volatile
        private var pcStore: PcStore? = null

        fun store(context: Context): PcStore = pcStore ?: synchronized(this) {
            pcStore ?: PcStore(context.applicationContext).also { pcStore = it }
        }
    }
}
