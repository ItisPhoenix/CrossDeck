package com.crossdeck.client.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.crossdeck.client.model.AppSettings
import com.crossdeck.client.ui.theme.SignalCyan

/**
 * Settings drawer content, extracted from DeckGridScreen.kt to keep that file from growing;
 * this owns nothing beyond rendering + calling back, all persistence lives in ConnectionManager.
 */
@Composable
fun SettingsPanel(
    accentColorHex: String,
    onAccentColorChange: (String) -> Unit,
    settings: AppSettings,
    onSettingsChange: (AppSettings) -> Unit,
    connectionHostInfo: String?,
    onForgetHost: () -> Unit,
    onClearIconCache: () -> Unit,
    haptic: () -> Unit
) {
    val accentColor = try {
        Color(android.graphics.Color.parseColor(accentColorHex))
    } catch (e: Exception) {
        SignalCyan
    }

    Box(
        modifier = Modifier
            .fillMaxWidth()
            .background(MaterialTheme.colorScheme.surface.copy(alpha = 0.9f), RoundedCornerShape(topStart = 24.dp, topEnd = 24.dp))
            .border(1.dp, accentColor.copy(alpha = 0.3f), RoundedCornerShape(topStart = 24.dp, topEnd = 24.dp))
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(max = 520.dp)
                .verticalScroll(rememberScrollState())
                .padding(24.dp)
        ) {
            Text("Select Theme Accent", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
            Spacer(modifier = Modifier.height(16.dp))
            
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(vertical = 8.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                listOf(
                    "Neon Cyan" to "#00E5FF",
                    "Neon Purple" to "#8b5cf6",
                    "Cyberpunk Yellow" to "#ffb703",
                    "Toxic Green" to "#39FF14",
                    "Crimson Red" to "#e63946"
                ).forEach { (_, hex) ->
                    val color = Color(android.graphics.Color.parseColor(hex))
                    val isSelected = hex.equals(accentColorHex, ignoreCase = true)
                    Box(
                        modifier = Modifier
                            .size(if (isSelected) 42.dp else 34.dp)
                            .background(color, RoundedCornerShape(21.dp))
                            .border(
                                width = if (isSelected) 2.5.dp else 1.dp,
                                color = if (isSelected) MaterialTheme.colorScheme.onSurface else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.2f),
                                shape = RoundedCornerShape(21.dp)
                            )
                            .clickable {
                                haptic()
                                onAccentColorChange(hex)
                            }
                    )
                }
            }

            HorizontalDivider(color = MaterialTheme.colorScheme.outline, modifier = Modifier.padding(vertical = 16.dp))
            Text("App Settings", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
            Spacer(modifier = Modifier.height(8.dp))

            SettingToggleRow("Haptic Feedback", settings.hapticsEnabled, accentColor) {
                onSettingsChange(settings.copy(hapticsEnabled = it))
            }
            SettingToggleRow("Compact Grid", settings.compactGrid, accentColor) {
                onSettingsChange(settings.copy(compactGrid = it))
            }
            SettingToggleRow("Keep Screen Awake", settings.keepScreenAwake, accentColor) {
                onSettingsChange(settings.copy(keepScreenAwake = it))
            }
            SettingToggleRow("Icon-Only Buttons", settings.iconOnlyMode, accentColor) {
                onSettingsChange(settings.copy(iconOnlyMode = it))
            }
            SettingToggleRow("Auto-Reconnect", settings.autoReconnect, accentColor) {
                onSettingsChange(settings.copy(autoReconnect = it))
            }
            SettingToggleRow("Confirm Before Run Command", settings.confirmRunCommand, accentColor) {
                onSettingsChange(settings.copy(confirmRunCommand = it))
            }

            HorizontalDivider(color = MaterialTheme.colorScheme.outline, modifier = Modifier.padding(vertical = 16.dp))

            Text("Connection", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
            Spacer(modifier = Modifier.height(8.dp))
            Text(
                connectionHostInfo ?: "Not connected",
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.bodyMedium
            )
            Spacer(modifier = Modifier.height(8.dp))
            TextButton(onClick = { haptic(); onForgetHost() }) {
                Text("Forget This PC", color = MaterialTheme.colorScheme.error)
            }

            HorizontalDivider(color = MaterialTheme.colorScheme.outline, modifier = Modifier.padding(vertical = 16.dp))

            TextButton(onClick = { haptic(); onClearIconCache() }) {
                Text("Clear Icon Cache", color = MaterialTheme.colorScheme.onSurface)
            }

            HorizontalDivider(color = MaterialTheme.colorScheme.outline, modifier = Modifier.padding(vertical = 16.dp))

            AboutSection()
        }
    }
}

@Composable
private fun SettingToggleRow(label: String, checked: Boolean, accentColor: Color, onCheckedChange: (Boolean) -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(label, color = MaterialTheme.colorScheme.onSurface, style = MaterialTheme.typography.bodyMedium)
        Switch(
            checked = checked,
            onCheckedChange = onCheckedChange,
            colors = SwitchDefaults.colors(
                checkedThumbColor = MaterialTheme.colorScheme.onSurface,
                checkedTrackColor = accentColor,
                uncheckedThumbColor = MaterialTheme.colorScheme.onSurfaceVariant,
                uncheckedTrackColor = MaterialTheme.colorScheme.background
            )
        )
    }
}

@Composable
private fun AboutSection() {
    val context = LocalContext.current
    val versionName = try {
        context.packageManager.getPackageInfo(context.packageName, 0).versionName ?: "dev"
    } catch (e: Exception) {
        "dev"
    }

    Text("About", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
    Spacer(modifier = Modifier.height(8.dp))
    Text("CrossDeck Client v$versionName", color = MaterialTheme.colorScheme.onSurfaceVariant)
    Text("Made by ItisPhoenix — github.com/ItisPhoenix", color = MaterialTheme.colorScheme.onSurfaceVariant)
    Text("Personal project — not licensed for redistribution.", color = MaterialTheme.colorScheme.onSurfaceVariant)
}
