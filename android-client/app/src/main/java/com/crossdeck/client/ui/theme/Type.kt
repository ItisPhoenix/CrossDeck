package com.crossdeck.client.ui.theme

import androidx.compose.material3.Typography
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

/**
 * UI text uses the platform default (Roboto/system font) deliberately — respect the platform
 * rather than forcing Windows' font choice onto Android. Data (IP/PIN/tokens/hashes) always
 * renders monospace on both platforms — that's the cross-platform consistency rule, not the
 * body font.
 */
val MonoFontFamily = FontFamily.Monospace // resolves to Roboto Mono on stock Android

val CrossDeckTypography = Typography(
    titleMedium = TextStyle(fontWeight = FontWeight.SemiBold, fontSize = 18.sp),
    bodyMedium = TextStyle(fontWeight = FontWeight.Normal, fontSize = 14.sp),
    bodySmall = TextStyle(fontWeight = FontWeight.Normal, fontSize = 12.sp),
    labelSmall = TextStyle(fontWeight = FontWeight.Medium, fontSize = 11.sp)
)

/** Use for any IP address, PIN, token, or hash — mirrors Windows' Cascadia Mono/Consolas rule. */
val DataTextStyle = TextStyle(
    fontFamily = MonoFontFamily,
    fontWeight = FontWeight.Bold,
    fontSize = 18.sp
)
