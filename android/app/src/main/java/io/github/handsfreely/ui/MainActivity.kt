package io.github.handsfreely.ui

import android.Manifest
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.os.Process
import android.provider.Settings
import android.widget.Toast
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.appcompat.app.AppCompatDelegate
import androidx.core.os.LocaleListCompat
import androidx.lifecycle.lifecycleScope
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import io.github.handsfreely.HandsfreelyApp
import io.github.handsfreely.R
import io.github.handsfreely.data.DeviceState
import io.github.handsfreely.link.LinkService
import io.github.handsfreely.link.LinkState
import io.github.handsfreely.link.PairingFailure
import io.github.handsfreely.link.PairingState
import io.github.handsfreely.net.PairingCode
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

class MainActivity : AppCompatActivity() {

    private val permissionsLauncher = registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) {
        refresh()
        LinkService.start(this)
    }

    private val scanLauncher = registerForActivityResult(ScanContract()) { result ->
        val text = result.contents ?: return@registerForActivityResult
        val code = PairingCode.parse(text)
        if (code == null) {
            LinkState.setPairing(PairingState.Failed(PairingFailure.BAD_CODE))
            return@registerForActivityResult
        }
        pair(code)
    }

    private val cameraLauncher = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) launchScanner() else LinkState.setPairing(PairingState.Failed(PairingFailure.NO_PERMISSION))
    }

    private var permissionState = PermissionState()
        set(value) {
            field = value
            permissionsVersion.intValue++
        }
    private val permissionsVersion = androidx.compose.runtime.mutableIntStateOf(0)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        refresh()
        LinkService.start(this)
        setContent {
            HandsfreelyTheme {
                permissionsVersion.intValue
                MainScreen(
                    permissions = permissionState,
                    store = HandsfreelyApp.store(this),
                    onConnectPc = ::startPairing,
                    onForget = { id -> LinkService.instance?.forget(id) ?: HandsfreelyApp.store(this).remove(id) },
                    onRequest = ::request,
                    onRevoke = ::revoke,
                    onLanguage = ::setLanguage,
                    currentLanguage = AppCompatDelegate.getApplicationLocales().toLanguageTags().take(2),
                )
            }
        }
    }

    override fun onResume() {
        super.onResume()
        refresh()
    }

    private fun refresh() {
        permissionState = PermissionState(
            bluetooth = DeviceState.bluetoothGranted(this),
            camera = DeviceState.has(this, Manifest.permission.CAMERA),
            groups = DeviceState.grantedGroups(this).toSet(),
            postNotifications = Build.VERSION.SDK_INT < 33 || DeviceState.has(this, Manifest.permission.POST_NOTIFICATIONS),
            batteryUnrestricted = getSystemService(PowerManager::class.java).isIgnoringBatteryOptimizations(packageName),
            fullCallControl = DeviceState.has(this, Manifest.permission.MANAGE_ONGOING_CALLS),
        )
    }

    private fun request(item: PermissionItem) {
        when (item) {
            PermissionItem.BLUETOOTH -> if (Build.VERSION.SDK_INT >= 31) permissionsLauncher.launch(arrayOf(Manifest.permission.BLUETOOTH_CONNECT))
            PermissionItem.CAMERA -> permissionsLauncher.launch(arrayOf(Manifest.permission.CAMERA))
            PermissionItem.POST_NOTIFICATIONS -> if (Build.VERSION.SDK_INT >= 33) permissionsLauncher.launch(arrayOf(Manifest.permission.POST_NOTIFICATIONS))
            PermissionItem.BATTERY -> startActivity(
                Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:$packageName"))
            )
            else -> permissionsLauncher.launch(DeviceState.groups[item.group].orEmpty().toTypedArray())
        }
    }

    private fun revoke(item: PermissionItem) {
        when {
            item == PermissionItem.POST_NOTIFICATIONS -> startActivity(
                Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, packageName)
            )
            item == PermissionItem.BATTERY -> {
                Toast.makeText(this, R.string.revoke_battery_hint, Toast.LENGTH_LONG).show()
                startActivity(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS))
            }
            Build.VERSION.SDK_INT >= 33 && item.runtime -> {
                revokeSelfPermissionsOnKill(permissionsOf(item))
                LinkService.stop(this)
                finishAndRemoveTask()
                Handler(Looper.getMainLooper()).postDelayed({ Process.killProcess(Process.myPid()) }, 500)
            }
            else -> {
                Toast.makeText(this, R.string.revoke_settings_hint, Toast.LENGTH_LONG).show()
                startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:$packageName")))
            }
        }
    }

    private fun permissionsOf(item: PermissionItem): List<String> = when (item) {
        PermissionItem.BLUETOOTH -> listOf(Manifest.permission.BLUETOOTH_CONNECT)
        PermissionItem.CAMERA -> listOf(Manifest.permission.CAMERA)
        else -> DeviceState.groups[item.group].orEmpty()
    }

    private fun startPairing() {
        if (!DeviceState.bluetoothGranted(this)) {
            request(PermissionItem.BLUETOOTH)
            return
        }
        if (DeviceState.has(this, Manifest.permission.CAMERA)) launchScanner()
        else cameraLauncher.launch(Manifest.permission.CAMERA)
    }

    private fun launchScanner() {
        scanLauncher.launch(
            ScanOptions()
                .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                .setPrompt(getString(R.string.scan_prompt))
                .setBeepEnabled(false)
                .setOrientationLocked(false)
                .setCaptureActivity(ScanActivity::class.java)
        )
    }

    private fun pair(code: PairingCode) {
        LinkService.start(this)
        lifecycleScope.launch {
            LinkState.setPairing(PairingState.Working(code.pcName))
            var service = LinkService.instance
            var waited = 0
            while (service == null && waited < 5000) {
                delay(100)
                waited += 100
                service = LinkService.instance
            }
            if (service == null) LinkState.setPairing(PairingState.Failed(PairingFailure.CONNECT_FAILED))
            else service.pair(code)
        }
    }

    private fun setLanguage(tag: String?) {
        AppCompatDelegate.setApplicationLocales(
            if (tag == null) LocaleListCompat.getEmptyLocaleList() else LocaleListCompat.forLanguageTags(tag)
        )
    }
}
