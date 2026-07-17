package com.crossdeck.client.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// True black, not Void (#0B0B0F) — "pitch black" was explicitly requested. Void stays as the
// cross-platform-matched token for everything else; only the screen background goes pure black.
private val PitchBlack = Color(0xFF000000)

private fun darkScheme(accent: Color) = darkColorScheme(
    primary = accent,
    secondary = VoltViolet,
    background = PitchBlack,
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
 * Dark-only, always — matches Windows (no light `ResourceDictionary` swap exists there either).
 * `darkTheme` stays a param (not hardcoded in the body) so a future light-theme toggle has a real
 * seam to plug into, but the default no longer follows `isSystemInDarkTheme()`: a phone in system
 * light mode was rendering this app in near-white, which read as a bug rather than an intentional
 * light theme.
 *
 * @param accentColor the live per-profile custom accent (mirrors Windows' ThemeManager.AccentColor
 *   / DynamicResource "Brush.Accent") — defaults to SignalCyan to match the Windows-side default.
 */
@Composable
fun CrossDeckTheme(
    accentColor: Color = SignalCyan,
    darkTheme: Boolean = true,
    content: @Composable () -> Unit
) {
    val colorScheme = if (darkTheme) darkScheme(accentColor) else lightScheme(accentColor)
    MaterialTheme(
        colorScheme = colorScheme,
        typography = CrossDeckTypography,
        content = content
    )
}
