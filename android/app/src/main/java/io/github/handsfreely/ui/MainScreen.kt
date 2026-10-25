package io.github.handsfreely.ui

import androidx.annotation.StringRes
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Language
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import io.github.handsfreely.BuildConfigInfo
import io.github.handsfreely.R
import io.github.handsfreely.link.LinkState
import io.github.handsfreely.link.PairingFailure
import io.github.handsfreely.link.PairingState
import io.github.handsfreely.link.PcRecord
import io.github.handsfreely.link.PcStatus
import io.github.handsfreely.link.PcStore

data class PermissionState(
    val bluetooth: Boolean = false,
    val groups: Set<String> = emptySet(),
    val postNotifications: Boolean = false,
    val batteryUnrestricted: Boolean = false,
    val fullCallControl: Boolean = false,
)

enum class PermissionItem(@StringRes val title: Int, @StringRes val hint: Int, val group: String) {
    BLUETOOTH(R.string.perm_bluetooth, R.string.perm_bluetooth_hint, ""),
    PHONE(R.string.perm_phone, R.string.perm_phone_hint, "phone"),
    CONTACTS(R.string.perm_contacts, R.string.perm_contacts_hint, "contacts"),
    CALLLOG(R.string.perm_calllog, R.string.perm_calllog_hint, "calllog"),
    POST_NOTIFICATIONS(R.string.perm_post, R.string.perm_post_hint, ""),
    BATTERY(R.string.perm_battery, R.string.perm_battery_hint, "");

    fun granted(s: PermissionState) = when (this) {
        BLUETOOTH -> s.bluetooth
        POST_NOTIFICATIONS -> s.postNotifications
        BATTERY -> s.batteryUnrestricted
        else -> group in s.groups
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MainScreen(
    permissions: PermissionState,
    store: PcStore,
    onConnectPc: () -> Unit,
    onForget: (String) -> Unit,
    onRequest: (PermissionItem) -> Unit,
    onLanguage: (String?) -> Unit,
    currentLanguage: String,
) {
    val pcs by store.pcs.collectAsState()
    val status by LinkState.status.collectAsState()
    val pairing by LinkState.pairing.collectAsState()
    var forget by remember { mutableStateOf<PcRecord?>(null) }
    var languageMenu by remember { mutableStateOf(false) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(stringResource(R.string.app_name)) },
                actions = {
                    Box {
                        IconButton(onClick = { languageMenu = true }) {
                            Icon(Icons.Filled.Language, contentDescription = stringResource(R.string.language))
                        }
                        DropdownMenu(expanded = languageMenu, onDismissRequest = { languageMenu = false }) {
                            listOf(null to R.string.language_system, "ru" to R.string.language_ru, "en" to R.string.language_en).forEach { (tag, label) ->
                                DropdownMenuItem(
                                    text = { Text(stringResource(label) + if ((tag ?: "") == currentLanguage) "  ✓" else "") },
                                    onClick = {
                                        languageMenu = false
                                        onLanguage(tag)
                                    },
                                )
                            }
                        }
                    }
                },
            )
        },
    ) { padding ->
        LazyColumn(
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = padding.calculateTopPadding() + 8.dp, bottom = padding.calculateBottomPadding() + 24.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item { SectionTitle(R.string.section_pcs) }
            if (pcs.isEmpty()) item { Text(stringResource(R.string.no_pcs_hint), style = MaterialTheme.typography.bodyMedium) }
            items(pcs, key = { it.id }) { pc -> PcCard(pc, status[pc.id] ?: PcStatus.OFFLINE) { forget = pc } }
            item {
                Button(onClick = onConnectPc, modifier = Modifier.fillMaxWidth()) {
                    Icon(Icons.Filled.QrCodeScanner, contentDescription = null)
                    Spacer(Modifier.width(8.dp))
                    Text(stringResource(R.string.connect_pc))
                }
            }
            item { PairingCard(pairing) }

            item { SectionTitle(R.string.section_permissions) }
            items(PermissionItem.entries) { PermissionRow(it, it.granted(permissions)) { onRequest(it) } }
            item { Text(stringResource(R.string.sms_hint), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }

            item { SectionTitle(R.string.section_call_control) }
            item {
                Text(
                    stringResource(if (permissions.fullCallControl) R.string.call_control_full else R.string.call_control_basic),
                    style = MaterialTheme.typography.bodyMedium,
                )
            }

            item {
                val context = LocalContext.current
                Text(
                    stringResource(R.string.footer, BuildConfigInfo.versionName(context)),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 16.dp),
                )
            }
        }
    }

    forget?.let { pc ->
        AlertDialog(
            onDismissRequest = { forget = null },
            text = { Text(stringResource(R.string.remove_confirm, pc.name)) },
            confirmButton = {
                TextButton(onClick = {
                    onForget(pc.id)
                    forget = null
                }) { Text(stringResource(R.string.remove)) }
            },
            dismissButton = { TextButton(onClick = { forget = null }) { Text(stringResource(R.string.cancel)) } },
        )
    }
}

@Composable
private fun SectionTitle(@StringRes title: Int) {
    Text(stringResource(title), style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(top = 8.dp))
}

@Composable
private fun PcCard(pc: PcRecord, status: PcStatus, onRemove: () -> Unit) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Filled.Computer, contentDescription = null, modifier = Modifier.size(32.dp))
            Spacer(Modifier.width(16.dp))
            Column(Modifier.weight(1f)) {
                Text(pc.name, style = MaterialTheme.typography.titleSmall)
                Text(
                    stringResource(
                        when (status) {
                            PcStatus.CONNECTED -> R.string.pc_connected
                            PcStatus.CONNECTING -> R.string.pc_connecting
                            PcStatus.OFFLINE -> R.string.pc_offline
                            PcStatus.FORGOTTEN -> R.string.pc_forgotten
                        }
                    ),
                    style = MaterialTheme.typography.bodySmall,
                    color = if (status == PcStatus.CONNECTED) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            IconButton(onClick = onRemove) { Icon(Icons.Filled.Delete, contentDescription = stringResource(R.string.remove)) }
        }
    }
}

@Composable
private fun PairingCard(state: PairingState) {
    val text = when (state) {
        PairingState.Idle -> return
        is PairingState.Working -> stringResource(R.string.pairing_working, state.pcName)
        is PairingState.Done -> stringResource(R.string.pairing_done, state.pcName)
        is PairingState.Failed -> stringResource(
            when (state.reason) {
                PairingFailure.BAD_CODE -> R.string.pairing_bad_code
                PairingFailure.BLUETOOTH_OFF -> R.string.pairing_bt_off
                PairingFailure.NO_PERMISSION -> R.string.pairing_no_permission
                PairingFailure.WINDOW_CLOSED -> R.string.pairing_window_closed
                PairingFailure.REJECTED -> R.string.pairing_rejected
                PairingFailure.CONNECT_FAILED -> R.string.pairing_connect_failed
            }
        )
    }
    Card(modifier = Modifier.fillMaxWidth()) {
        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
            if (state is PairingState.Working) {
                CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                Spacer(Modifier.width(12.dp))
            }
            Text(text, style = MaterialTheme.typography.bodyMedium)
        }
    }
}

@Composable
private fun PermissionRow(item: PermissionItem, granted: Boolean, onRequest: () -> Unit) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(stringResource(item.title), style = MaterialTheme.typography.bodyLarge)
            Text(stringResource(item.hint), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Spacer(Modifier.width(12.dp))
        if (granted) {
            Icon(Icons.Filled.CheckCircle, contentDescription = stringResource(R.string.granted), tint = MaterialTheme.colorScheme.primary)
        } else {
            FilledTonalButton(onClick = onRequest) { Text(stringResource(R.string.allow)) }
        }
    }
}
