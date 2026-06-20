using System.Windows.Input;

namespace PanelTray.Services;

public static class HotkeyParser
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    public const string DefaultHotkey = "Ctrl+Alt+P";

    public static bool IsValid(string? text)
    {
        return TryParse(text, out _, out _);
    }

    public static bool TryParse(string? text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        var parts = (text ?? string.Empty)
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 || parts[^1].EndsWith("...", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var part in parts[..^1])
        {
            modifiers |= part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => ModControl,
                "ALT" => ModAlt,
                "SHIFT" => ModShift,
                "WIN" or "WINDOWS" => ModWin,
                _ => 0
            };
        }

        if (modifiers == 0)
        {
            return false;
        }

        if (!TryParseKey(parts[^1], out var key))
        {
            return false;
        }

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }

    private static bool TryParseKey(string keyText, out Key key)
    {
        keyText = keyText.Trim();
        if (keyText.Length == 1)
        {
            if (char.IsDigit(keyText[0]))
            {
                return Enum.TryParse($"D{keyText}", out key);
            }

            if (char.IsLetter(keyText[0]))
            {
                return Enum.TryParse(keyText.ToUpperInvariant(), out key);
            }
        }

        return keyText.ToUpperInvariant() switch
        {
            "PLUS" => Enum.TryParse("OemPlus", out key),
            "MINUS" => Enum.TryParse("OemMinus", out key),
            "COMMA" => Enum.TryParse("OemComma", out key),
            "PERIOD" => Enum.TryParse("OemPeriod", out key),
            "SPACE" => Enum.TryParse("Space", out key),
            "SEMICOLON" => Enum.TryParse("Oem1", out key),
            "SLASH" => Enum.TryParse("Oem2", out key),
            "TILDE" => Enum.TryParse("Oem3", out key),
            "BRACKETOPEN" => Enum.TryParse("Oem4", out key),
            "BACKSLASH" => Enum.TryParse("Oem5", out key),
            "BRACKETCLOSE" => Enum.TryParse("Oem6", out key),
            "QUOTE" => Enum.TryParse("Oem7", out key),
            "TAB" => Enum.TryParse("Tab", out key),
            "BACKSPACE" => Enum.TryParse("Back", out key),
            "DELETE" => Enum.TryParse("Delete", out key),
            "INSERT" => Enum.TryParse("Insert", out key),
            "HOME" => Enum.TryParse("Home", out key),
            "END" => Enum.TryParse("End", out key),
            "PAGEUP" => Enum.TryParse("PageUp", out key),
            "PAGEDOWN" => Enum.TryParse("PageDown", out key),
            "UP" => Enum.TryParse("Up", out key),
            "DOWN" => Enum.TryParse("Down", out key),
            "LEFT" => Enum.TryParse("Left", out key),
            "RIGHT" => Enum.TryParse("Right", out key),
            "MULTIPLY" => Enum.TryParse("Multiply", out key),
            "ADD" => Enum.TryParse("Add", out key),
            "SUBTRACT" => Enum.TryParse("Subtract", out key),
            "DECIMAL" => Enum.TryParse("Decimal", out key),
            "DIVIDE" => Enum.TryParse("Divide", out key),
            _ when keyText.StartsWith("NUMPAD", StringComparison.OrdinalIgnoreCase)
                => Enum.TryParse(keyText, ignoreCase: true, out key),
            _ when keyText.StartsWith('F') && int.TryParse(keyText[1..], out var fn) && fn is >= 1 and <= 24
                => Enum.TryParse($"F{fn}", out key),
            _ => Enum.TryParse<Key>(keyText, ignoreCase: true, out key)
        };
    }
}
