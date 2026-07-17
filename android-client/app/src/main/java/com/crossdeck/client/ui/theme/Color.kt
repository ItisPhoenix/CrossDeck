package com.crossdeck.client.ui.theme

import androidx.compose.ui.graphics.Color

/**
 * Design tokens. These values must match windows-host/CrossDeckHost/Resources/Colors.xaml
 * exactly — the two platforms had drifted before (0xFF00F2FE here vs #00d4ff on Windows for
 * the same "brand cyan" role).
 */

// Neutrals — dark
val Void = Color(0xFF0B0B0F)
val Panel = Color(0xFF16161C)
val Hairline = Color(0xFF26262E)
val Paper = Color(0xFFF2F3F5)
val Mist = Color(0xFF9AA0AC)

// Neutrals — light
val BaseLight = Color(0xFFF7F7F9)
val PanelLight = Color(0xFFFFFFFF)
val HairlineLight = Color(0xFFE4E4EA)
val PaperLight = Color(0xFF14141A)
val MistLight = Color(0xFF6B7280)

// Brand accents — identical hex to Windows' Resources/Colors.xaml, do not let these drift
val SignalCyan = Color(0xFF00E5FF)
val VoltViolet = Color(0xFF8B5CF6)

// Semantic
val Go = Color(0xFF22C55E)
val Caution = Color(0xFFF5A524)
val Alarm = Color(0xFFEF4444)
