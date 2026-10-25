package io.github.handsfreely.screenshots

import android.content.Context
import androidx.activity.ComponentActivity
import androidx.compose.ui.test.hasScrollAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performScrollToNode
import androidx.test.core.app.ApplicationProvider
import com.github.takahirom.roborazzi.captureRoboImage
import io.github.handsfreely.R
import io.github.handsfreely.link.LinkState
import io.github.handsfreely.link.PcRecord
import io.github.handsfreely.link.PcStatus
import io.github.handsfreely.link.PcStore
import io.github.handsfreely.ui.HandsfreelyTheme
import io.github.handsfreely.ui.MainScreen
import io.github.handsfreely.ui.PermissionState
import org.junit.After
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import java.io.File

@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [34])
class ScreenshotTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private val outDir = File(System.getProperty("handsfreely.screenshots.dir") ?: "build/screenshots")

    @After
    fun tearDown() {
        LinkState.status.value.keys.forEach { LinkState.remove(it) }
    }

    @Test
    @Config(qualifiers = "ru-w411dp-h891dp-xxhdpi")
    fun russian() = shoot(listOf("Рабочий ноутбук", "Домашний ПК"), "")

    @Test
    @Config(qualifiers = "en-w411dp-h891dp-xxhdpi")
    fun english() = shoot(listOf("Work laptop", "Home PC"), "-en")

    @Test
    @Config(qualifiers = "ru-w411dp-h891dp-night-xxhdpi")
    fun russianDark() = shoot(listOf("Рабочий ноутбук", "Домашний ПК"), "-dark", accessScreen = false)

    @Test
    @Config(qualifiers = "en-w411dp-h891dp-night-xxhdpi")
    fun englishDark() = shoot(listOf("Work laptop", "Home PC"), "-dark-en", accessScreen = false)

    private fun shoot(pcNames: List<String>, suffix: String, accessScreen: Boolean = true) {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val store = PcStore(context)
        store.pcs.value.forEach { store.remove(it.id) }
        pcNames.forEachIndexed { i, name ->
            val key = ByteArray(65) { (it + i * 7).toByte() }
            val pc = PcRecord(PcRecord.idFor(key), name, "00000000000$i", key, 0)
            store.add(pc)
            LinkState.set(pc.id, if (i == 0) PcStatus.CONNECTED else PcStatus.OFFLINE)
        }
        val permissions = PermissionState(
            bluetooth = true,
            camera = true,
            groups = setOf("phone", "contacts", "calllog"),
            postNotifications = true,
            batteryUnrestricted = false,
            fullCallControl = false,
        )
        compose.setContent {
            HandsfreelyTheme(dynamicColor = false) {
                MainScreen(
                    permissions = permissions,
                    store = store,
                    onConnectPc = {},
                    onForget = {},
                    onRequest = {},
                    onRevoke = {},
                    onLanguage = {},
                    currentLanguage = "",
                    sdk = 34,
                )
            }
        }
        outDir.mkdirs()
        compose.waitForIdle()
        compose.onRoot().captureRoboImage(File(outDir, "android-main$suffix.png").path)
        if (accessScreen) {
            val footer = compose.activity.getString(R.string.section_call_control)
            compose.onNode(hasScrollAction()).performScrollToNode(hasText(footer))
            compose.waitForIdle()
            compose.onRoot().captureRoboImage(File(outDir, "android-access$suffix.png").path)
        }
    }
}
