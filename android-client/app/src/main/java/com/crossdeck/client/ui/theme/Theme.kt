package com.crossdeck.client.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private fun darkScheme(accent: Color) = darkColorScheme(
    primary = accent,
    secondary = VoltViolet,
    background = Void,
    surface = Panel,
    onBackground = Paper,
    onSurface = Paper,
    onSurfaceVariant = Mist,
    outline = Hairline,
    error = Alarm
)

private fun lightScheme(accent: Color) = lightColorScheme(
    primary = accent,
    secondary = VoltViolet,
    background = BaseLight,
    surface = PanelLight,
    onBackground = PaperLight,
    onSurface = PaperLight,
    onSurfaceVariant = MistLight,
    outline = HairlineLight,
    error = Alarm
)

/**
 * Was dead code before this fix: MainActivity defined both a dark and a light color scheme but
 * only ever used the dark one — `isSystemInDarkTheme()` was imported and never called. Wiring it
 * up here means light mode (a planned Milestone 3 item) now actually has somewhere real to plug
 * into instead of needing this decided from scratch later.
 *
 * @param accentColor the live per-profile custom accent (mirrors Windows' ThemeManager.AccentColor
 *   / DynamicResource "Brush.Accent") — defaults to SignalCyan to match the Windows-side default.
 */
@Composable
fun CrossDeckTheme(
    accentColor: Color = SignalCyan,
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit
) {
    val colorScheme = if (darkTheme) darkScheme(accentColor) else lightScheme(accentColor)
    MaterialTheme(
        colorScheme = colorScheme,
        typography = CrossDeckTypography,
        content = content
    )
}
