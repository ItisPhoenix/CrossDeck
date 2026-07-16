package com.crossdeck.client.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import android.content.Intent
import android.view.HapticFeedbackConstants
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import com.crossdeck.client.QrScannerActivity
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.tween
import androidx.compose.animation.core.RepeatMode
import androidx.compose.ui.draw.scale
import androidx.compose.ui.draw.alpha
import androidx.compose.foundation.layout.height

@Composable
fun PairingScreen(
    connecting: Boolean,
    errorMessage: String?,
    defaultIp: String,
    defaultPort: String,
    defaultPin: String,
    accentColorHex: String,
    onConnect: (ip: String, port: Int, pin: String) -> Unit,
    onScan: ((ip: String, port: Int, hostName: String) -> Unit) -> Unit
) {
    var ip by remember { mutableStateOf(defaultIp) }
    var port by remember { mutableStateOf(defaultPort) }
    var pin by remember { mutableStateOf(defaultPin) }
    var scanning by remember { mutableStateOf(false) }
    var scanStatus by remember { mutableStateOf<String?>(null) }

    val context = LocalContext.current
    val view = LocalView.current
    val accentColor = try {
        Color(android.graphics.Color.parseColor(accentColorHex))
    } catch (e: Exception) {
        Color(0xFF00F2FE)
    }

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
                val portInt = scannedPort.toIntOrNull()
                if (portInt != null) {
                    view.performHapticFeedback(HapticFeedbackConstants.LONG_PRESS)
                    onConnect(scannedIp.trim(), portInt, scannedPin.trim())
                }
            }
        }
    }

    fun triggerScan() {
        view.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
        scanning = true
        scanStatus = null
        onScan { discoveredIp, discoveredPort, hostName ->
            ip = discoveredIp
            port = discoveredPort.toString()
            scanning = false
            scanStatus = "Found: $hostName"
        }
    }

    // Auto-scan on first composition
    LaunchedEffect(Unit) {
        triggerScan()
        kotlinx.coroutines.delay(2000)
        if (scanning) {
            scanning = false
            if (scanStatus == null) scanStatus = "No PC found — enter IP manually."
        }
    }

    // Radar scanning wave pulse animation
    val infiniteTransition = rememberInfiniteTransition(label = "radar")
    val pulseScale by infiniteTransition.animateFloat(
        initialValue = 1.0f,
        targetValue = 1.6f,
        animationSpec = infiniteRepeatable(
            animation = tween(1200),
            repeatMode = RepeatMode.Restart
        ),
        label = "pulseScale"
    )
    val pulseAlpha by infiniteTransition.animateFloat(
        initialValue = 0.8f,
        targetValue = 0.0f,
        animationSpec = infiniteRepeatable(
            animation = tween(1200),
            repeatMode = RepeatMode.Restart
        ),
        label = "pulseAlpha"
    )

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color(0xFF080810))
            .padding(24.dp),
        contentAlignment = Alignment.Center
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .border(1.5.dp, Color(0xFF1F1F23), RoundedCornerShape(12.dp))
                .background(Color(0xFF0E0E10), RoundedCornerShape(12.dp))
                .padding(24.dp),
            verticalArrangement = Arrangement.Center
        ) {
            Text("✦ Pair with your PC", style = MaterialTheme.typography.headlineSmall, color = Color.White)
            Text(
                "CrossDeck will scan your WiFi for a PC running the host app.",
                style = MaterialTheme.typography.bodyMedium,
                color = Color.LightGray,
                modifier = Modifier.padding(top = 4.dp, bottom = 20.dp)
            )

            // Scan row with visual pulsing radar overlay when scanning
            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(bottom = 20.dp)
            ) {
                Box(contentAlignment = Alignment.Center) {
                    if (scanning) {
                        Box(
                            modifier = Modifier
                                .size(48.dp)
                                .scale(pulseScale)
                                .alpha(pulseAlpha)
                                .border(2.dp, accentColor, RoundedCornerShape(24.dp))
                        )
                    }
                    OutlinedButton(
                        onClick = { triggerScan() },
                        enabled = !scanning && !connecting,
                        colors = ButtonDefaults.outlinedButtonColors(contentColor = accentColor)
                    ) {
                        if (scanning) {
                            CircularProgressIndicator(modifier = Modifier.size(16.dp), strokeWidth = 2.dp, color = accentColor)
                            Spacer(Modifier.width(8.dp))
                            Text("Scanning…", color = Color.White)
                        } else {
                            Text("Scan WiFi", color = Color.White)
                        }
                    }
                }
                Spacer(Modifier.width(8.dp))
                OutlinedButton(
                    onClick = {
                        view.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
                        val intent = Intent(context, QrScannerActivity::class.java)
                        qrScannerLauncher.launch(intent)
                    },
                    enabled = !connecting
                ) {
                    Text("Scan QR", color = Color.White)
                }
                scanStatus?.let {
                    Spacer(Modifier.width(12.dp))
                    Text(
                        it,
                        style = MaterialTheme.typography.bodySmall,
                        color = if (it.startsWith("Found")) accentColor else Color.Gray
                    )
                }
            }

            OutlinedTextField(
                value = ip,
                onValueChange = { ip = it },
                label = { Text("PC IP Address", color = Color.Gray) },
                colors = OutlinedTextFieldDefaults.colors(
                    focusedTextColor = Color.White,
                    unfocusedTextColor = Color.White,
                    focusedBorderColor = accentColor,
                    unfocusedBorderColor = Color(0xFF1F1F23)
                ),
                modifier = Modifier.fillMaxWidth(),
                singleLine = true
            )
            Spacer(Modifier.height(12.dp))

            OutlinedTextField(
                value = port,
                onValueChange = { port = it },
                label = { Text("Port", color = Color.Gray) },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                colors = OutlinedTextFieldDefaults.colors(
                    focusedTextColor = Color.White,
                    unfocusedTextColor = Color.White,
                    focusedBorderColor = accentColor,
                    unfocusedBorderColor = Color(0xFF1F1F23)
                ),
                modifier = Modifier.fillMaxWidth(),
                singleLine = true
            )
            Spacer(Modifier.height(12.dp))

            OutlinedTextField(
                value = pin,
                onValueChange = { pin = it },
                label = { Text("PIN", color = Color.Gray) },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                colors = OutlinedTextFieldDefaults.colors(
                    focusedTextColor = Color.White,
                    unfocusedTextColor = Color.White,
                    focusedBorderColor = accentColor,
                    unfocusedBorderColor = Color(0xFF1F1F23)
                ),
                modifier = Modifier.fillMaxWidth(),
                singleLine = true
            )

            if (errorMessage != null) {
                Text(
                    errorMessage,
                    color = Color.Red,
                    modifier = Modifier.padding(top = 12.dp)
                )
            }

            Button(
                onClick = {
                    view.performHapticFeedback(HapticFeedbackConstants.CONFIRM)
                    val portInt = port.toIntOrNull() ?: return@Button
                    onConnect(ip.trim(), portInt, pin.trim())
                },
                enabled = !connecting && !scanning && ip.isNotBlank() && pin.isNotBlank(),
                colors = ButtonDefaults.buttonColors(containerColor = accentColor, contentColor = Color.Black),
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 24.dp)
            ) {
                Text(if (connecting) "Connecting…" else "Connect", fontWeight = androidx.compose.ui.text.font.FontWeight.Bold)
            }
        }
    }
}
