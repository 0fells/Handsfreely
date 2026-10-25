package io.github.handsfreely.link

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import io.github.handsfreely.HandsfreelyApp

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (HandsfreelyApp.store(context).pcs.value.isNotEmpty()) LinkService.start(context)
    }
}
