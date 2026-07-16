using System;

namespace CrossDeckHost;

public static class ThemeManager
{
    public static string AccentColor { get; set; } = "#00d4ff"; // Default: Neon Cyan

    public static void ApplyTheme(System.Windows.Window window)
    {
        // Dark theme colors from DESIGN.md (Obsidian Cyber-Intelligence)
        var bg = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#080810");
        var cardBg = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0E0E10");
        var textFg = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF");
        var borderBrush = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1F1F23");
        var accentBrush = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(AccentColor);

        window.Background = new System.Windows.Media.SolidColorBrush(bg);
        window.Foreground = new System.Windows.Media.SolidColorBrush(textFg);

        // Apply custom fonts
        window.FontFamily = new System.Windows.Media.FontFamily("Hanken Grotesk, Segoe UI, Arial");

        // Recursively style child controls
        StyleLogicalTree(window, bg, cardBg, textFg, borderBrush, accentBrush);
    }

    private static void StyleLogicalTree(System.Windows.DependencyObject parent, System.Windows.Media.Color bg, System.Windows.Media.Color cardBg, System.Windows.Media.Color textFg, System.Windows.Media.Color borderBrush, System.Windows.Media.Color accentBrush)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

            if (child is System.Windows.Controls.TextBlock tb)
            {
                tb.Foreground = new System.Windows.Media.SolidColorBrush(textFg);
                // Highlight headers/titles with Hanken Grotesk bold
                if (tb.FontSize >= 14)
                {
                    tb.FontWeight = System.Windows.FontWeights.Bold;
                }
            }
            else if (child is System.Windows.Controls.TextBox txt)
            {
                txt.Background = new System.Windows.Media.SolidColorBrush(cardBg);
                txt.Foreground = new System.Windows.Media.SolidColorBrush(textFg);
                txt.BorderBrush = new System.Windows.Media.SolidColorBrush(borderBrush);
                txt.BorderThickness = new System.Windows.Thickness(1.5);
                txt.Padding = new System.Windows.Thickness(6);
                txt.FontFamily = new System.Windows.Media.FontFamily("JetBrains Mono, Consolas, Courier New");
            }
            else if (child is System.Windows.Controls.ComboBox combo)
            {
                combo.Background = new System.Windows.Media.SolidColorBrush(cardBg);
                combo.Foreground = new System.Windows.Media.SolidColorBrush(textFg);
                combo.BorderBrush = new System.Windows.Media.SolidColorBrush(borderBrush);
            }
            else if (child is System.Windows.Controls.Border border)
            {
                // Card styling
                border.Background = new System.Windows.Media.SolidColorBrush(cardBg);
                border.BorderBrush = new System.Windows.Media.SolidColorBrush(borderBrush);
                border.BorderThickness = new System.Windows.Thickness(1);
            }
            else if (child is System.Windows.Controls.Button btn)
            {
                var hex = btn.Background?.ToString() ?? "";
                if (hex == "#FFFFEBEE" || hex == "#FFC62828" || hex == "#FFE53935") // Delete buttons
                {
                    btn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3A1515"));
                    btn.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF8080"));
                    btn.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF8080"));
                }
                else if (hex == "#FFE0F7FA" || hex == "#FF006064") // Preset / Add buttons
                {
                    btn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0C2D30"));
                    btn.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#80DEEA"));
                    btn.BorderBrush = new System.Windows.Media.SolidColorBrush(accentBrush);
                }
                else if (hex == "#FF2196F3" || hex == "#FF4CAF50") // Save / Enter Action
                {
                    btn.Background = new System.Windows.Media.SolidColorBrush(accentBrush);
                    btn.Foreground = new System.Windows.Media.SolidColorBrush(bg);
                    btn.BorderBrush = new System.Windows.Media.SolidColorBrush(accentBrush);
                }
                else
                {
                    btn.Background = new System.Windows.Media.SolidColorBrush(cardBg);
                    btn.Foreground = new System.Windows.Media.SolidColorBrush(textFg);
                    btn.BorderBrush = new System.Windows.Media.SolidColorBrush(borderBrush);
                }
                btn.BorderThickness = new System.Windows.Thickness(1.5);
            }

            StyleLogicalTree(child, bg, cardBg, textFg, borderBrush, accentBrush);
        }
    }
}
