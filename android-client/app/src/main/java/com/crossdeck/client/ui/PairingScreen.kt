package com.crossdeck.client.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import android.content.Intent
import androidx.compose.ui.platform.LocalContext
import com.crossdeck.client.QrScannerActivity

/**
 * Pairing screen with:
 *  - Auto-scan on launch (LaunchedEffect) — fills IP + Port if a CrossDeck host is found.
 *  - Manual "Scan" button so the user can re-trigger discovery without restarting.
 *  - Pre-populated fields from saved SharedPreferences so returning users don't re-type anything.
 */
@Composable
fun PairingScreen(
    connecting: Boolean,
    errorMessage: String?,
    defaultIp: String,
    defaultPort: String,
    defaultPin: String,
    onConnect: (ip: String, port: Int, pin: String) -> Unit,
    onScan: ((ip: String, port: Int, hostName: String) -> Unit) -> Unit
) {
    var ip by remember { mutableStateOf(defaultIp) }
    var port by remember { mutableStateOf(defaultPort) }
    var pin by remember { mutableStateOf(defaultPin) }
    var scanning by remember { mutableStateOf(false) }
    var scanStatus by remember { mutableStateOf<String?>(null) }

    val context = LocalContext.current
    val qrScannerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == android.app.Activity.RESULT_OK) {
            val data = result.data
            val scannedIp = data?.getStringExtra("ip")
            val scannedPort = data?.getStringExtra("port")
            val scannedPin = data?.getStringExtra("pin")

            if (scannedIp != null && scannedPort != null && scannedPin != null) {
                ip = scannedIp
                port = scannedPort
                pin = scannedPin
            }
        }
    }

    fun triggerScan() {
        scanning = true
        scanStatus = null
        onScan { discoveredIp, discoveredPort, hostName ->
            ip = discoveredIp
            port = discoveredPort.toString()
            scanning = false
            scanStatus = "Found: $hostName ($discoveredIp)"
        }
        // Timeout fallback after 2 seconds (the socket already times out at 1.5 s)
        // We rely on the callback not being called => scanning stays true briefly then
        // the coroutine finishes. Reset scanning after the socket timeout window.
        // A simpler approach: the coroutine in ConnectionManager catches the timeout exception
        // and simply never calls onDiscovered, so we need an explicit timeout here.
    }

    // Auto-scan on first composition
    LaunchedEffect(Unit) {
        triggerScan()
        // After ~2 s the scan coroutine will have finished (socket timeout is 1.5 s)
        kotlinx.coroutines.delay(2000)
        if (scanning) {
            scanning = false
            if (scanStatus == null) scanStatus = "No PC found — enter IP manually."
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(24.dp),
        verticalArrangement = Arrangement.Center
    ) {
        Text("Pair with your PC", style = MaterialTheme.typography.headlineSmall)
        Text(
            "CrossDeck will scan your WiFi for a PC running the host app.",
            style = MaterialTheme.typography.bodyMedium,
            modifier = Modifier.padding(top = 4.dp, bottom = 16.dp)
        )

        // Scan row
        Row(
            verticalAlignment = Alignment.CenterVertically,
            modifier = Modifier
                .fillMaxWidth()
                .padding(bottom = 20.dp)
        ) {
            OutlinedButton(
                onClick = { triggerScan() },
                enabled = !scanning && !connecting
            ) {
                if (scanning) {
                    CircularProgressIndicator(modifier = Modifier.size(16.dp), strokeWidth = 2.dp)
                    Spacer(Modifier.width(8.dp))
                    Text("Scanning…")
                } else {
                    Text("Scan WiFi")
                }
            }
            Spacer(Modifier.width(8.dp))
            OutlinedButton(
                onClick = {
                    val intent = Intent(context, QrScannerActivity::class.java)
                    qrScannerLauncher.launch(intent)
                },
                enabled = !connecting
            ) {
                Text("Scan QR")
            }
            scanStatus?.let {
                Spacer(Modifier.width(12.dp))
                Text(
                    it,
                    style = MaterialTheme.typography.bodySmall,
                    color = if (it.startsWith("Found")) MaterialTheme.colorScheme.primary
                            else MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }

        OutlinedTextField(
            value = ip,
            onValueChange = { ip = it },
            label = { Text("PC IP Address") },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true
        )
        Spacer(Modifier.padding(top = 12.dp))

        OutlinedTextField(
            value = port,
            onValueChange = { port = it },
            label = { Text("Port") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            modifier = Modifier.fillMaxWidth(),
            singleLine = true
        )
        Spacer(Modifier.padding(top = 12.dp))

        OutlinedTextField(
            value = pin,
            onValueChange = { pin = it },
            label = { Text("PIN") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            modifier = Modifier.fillMaxWidth(),
            singleLine = true
        )

        if (errorMessage != null) {
            Text(
                errorMessage,
                color = MaterialTheme.colorScheme.error,
                modifier = Modifier.padding(top = 12.dp)
            )
        }

        Button(
            onClick = {
                val portInt = port.toIntOrNull() ?: return@Button
                onConnect(ip.trim(), portInt, pin.trim())
            },
            enabled = !connecting && !scanning && ip.isNotBlank() && pin.isNotBlank(),
            modifier = Modifier
                .fillMaxWidth()
                .padding(top = 24.dp)
        ) {
            Text(if (connecting) "Connecting…" else "Connect")
        }
    }
}
