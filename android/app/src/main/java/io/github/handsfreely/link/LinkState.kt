package io.github.handsfreely.link

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update

enum class PcStatus { OFFLINE, CONNECTING, CONNECTED, FORGOTTEN }

sealed interface PairingState {
    data object Idle : PairingState
    data class Working(val pcName: String) : PairingState
    data class Done(val pcName: String) : PairingState
    data class Failed(val reason: PairingFailure, val detail: String? = null) : PairingState
}

enum class PairingFailure { BAD_CODE, BLUETOOTH_OFF, NO_PERMISSION, WINDOW_CLOSED, REJECTED, CONNECT_FAILED }

object LinkState {
    private val _status = MutableStateFlow<Map<String, PcStatus>>(emptyMap())
    val status: StateFlow<Map<String, PcStatus>> = _status

    private val _pairing = MutableStateFlow<PairingState>(PairingState.Idle)
    val pairing: StateFlow<PairingState> = _pairing

    fun set(pcId: String, status: PcStatus) = _status.update { it + (pcId to status) }

    fun remove(pcId: String) = _status.update { it - pcId }

    fun setPairing(state: PairingState) {
        _pairing.value = state
    }
}
