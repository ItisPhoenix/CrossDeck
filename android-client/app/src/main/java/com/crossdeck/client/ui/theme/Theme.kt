package com.crossdeck.client.ui.theme

import androidx.compose.material3.ColorScheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import dynamiccolor.DynamicColor
import dynamiccolor.MaterialDynamicColors
import hct.Hct
import scheme.SchemeFidelity

private val PitchBlack = Color(0xFF000000)

private val dynamicColors = MaterialDynamicColors()

/**
 * Builds a complete Material3 `ColorScheme` (all 48 roles) from one accent color via the vendored
 * `SchemeFidelity` generator. Every role is passed as an explicit named argument to `darkColorScheme`
 * — which function we call is irrelevant here since every one of its 48 parameters is overridden
 * below; `darkColorScheme` was picked arbitrarily over `lightColorScheme` for that reason. `isDark`
 * controls the *tones* `SchemeFidelity` computes internally (each role's `getArgb(scheme)` call reads
 * `scheme`'s own dark/light flag), not which Compose factory function we call.
 */
private fun buildScheme(accent: Color, isDark: Boolean): ColorScheme {
    val hct = Hct.fromInt(accent.toArgb())
    val scheme = SchemeFidelity(hct, isDark, 0.0)

    fun c(role: (MaterialDynamicColors) -> DynamicColor): Color =
        Color(role(dynamicColors).getArgb(scheme))

    return darkColorScheme(
        primary = c(MaterialDynamicColors::primary),
        onPrimary = c(MaterialDynamicColors::onPrimary),
        primaryContainer = c(MaterialDynamicColors::primaryContainer),
        onPrimaryContainer = c(MaterialDynamicColors::onPrimaryContainer),
        inversePrimary = c(MaterialDynamicColors::inversePrimary),
        secondary = c(MaterialDynamicColors::secondary),
        onSecondary = c(MaterialDynamicColors::onSecondary),
        secondaryContainer = c(MaterialDynamicColors::secondaryContainer),
        onSecondaryContainer = c(MaterialDynamicColors::onSecondaryContainer),
        tertiary = c(MaterialDynamicColors::tertiary),
        onTertiary = c(MaterialDynamicColors::onTertiary),
        tertiaryContainer = c(MaterialDynamicColors::tertiaryContainer),
        onTertiaryContainer = c(MaterialDynamicColors::onTertiaryContainer),
        background = if (isDark) PitchBlack else BaseLight,
        onBackground = if (isDark) Paper else PaperLight,
        surface = if (isDark) Panel else PanelLight,
        onSurface = if (isDark) Paper else PaperLight,
        surfaceVariant = c(MaterialDynamicColors::surfaceVariant),
        onSurfaceVariant = if (isDark) Mist else MistLight,
        surfaceTint = c(MaterialDynamicColors::primary),
        inverseSurface = c(MaterialDynamicColors::inverseSurface),
        inverseOnSurface = c(MaterialDynamicColors::inverseOnSurface),
        error = Alarm,
        onError = c(MaterialDynamicColors::onError),
        errorContainer = c(MaterialDynamicColors::errorContainer),
        onErrorContainer = c(MaterialDynamicColors::onErrorContainer),
        outline = if (isDark) Hairline else HairlineLight,
        outlineVariant = c(MaterialDynamicColors::outlineVariant),
        scrim = Color(0xFF000000),
        surfaceBright = c(MaterialDynamicColors::surfaceBright),
        surfaceDim = c(MaterialDynamicColors::surfaceDim),
        surfaceContainer = c(MaterialDynamicColors::surfaceContainer),
        surfaceContainerHigh = c(MaterialDynamicColors::surfaceContainerHigh),
        surfaceContainerHighest = c(MaterialDynamicColors::surfaceContainerHighest),
        surfaceContainerLow = c(MaterialDynamicColors::surfaceContainerLow),
        surfaceContainerLowest = c(MaterialDynamicColors::surfaceContainerLowest),
    )
}

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
    val colorScheme = buildScheme(accentColor, darkTheme)
    MaterialTheme(
        colorScheme = colorScheme,
        typography = CrossDeckTypography,
        content = content
    )
}
