namespace LoxoneHueBridge.Core.Helpers;

public static class HexFormatHelper
{
    /// <summary>
    /// Converts a uint to hex string format: "12:34:56:78"
    /// </summary>
    public static string ToHexString(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    /// <summary>
    /// Converts hex string format "12:34:56:78" to uint
    /// </summary>
    public static uint FromHexString(string hexString)
    {
        if (string.IsNullOrEmpty(hexString))
            throw new ArgumentException("Hex string cannot be null or empty");

        var parts = hexString.Split(':');
        if (parts.Length != 4)
            throw new ArgumentException("Hex string must be in format 'XX:XX:XX:XX'");

        var bytes = parts.Select(part => Convert.ToByte(part, 16)).ToArray();
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        
        return BitConverter.ToUInt32(bytes, 0);
    }

    /// <summary>
    /// Validates if a string is in valid hex format "XX:XX:XX:XX"
    /// </summary>
    public static bool IsValidHexFormat(string hexString)
    {
        if (string.IsNullOrEmpty(hexString))
            return false;

        var parts = hexString.Split(':');
        if (parts.Length != 4)
            return false;

        return parts.All(part => 
            part.Length == 2 && 
            part.All(c => char.IsDigit(c) || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')));
    }
}