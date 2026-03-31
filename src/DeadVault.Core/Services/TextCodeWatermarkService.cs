using System.Text;

namespace DeadVault.Core.Services;

/// <summary>
/// Lightweight proof-of-concept for text + code watermarking utilities.
/// Text: encodes bits using zero-width Unicode carriers after spaces.
/// Code: encodes bits using trailing whitespace (space=0, tab=1) at line ends.
/// </summary>
public static class TextCodeWatermarkService
{
    private const char TextBit0 = '\u200B'; // zero-width space
    private const char TextBit1 = '\u200C'; // zero-width non-joiner

    private static readonly byte[] TextMagic = Encoding.ASCII.GetBytes("DVWT1"); // DeadVault Watermark Text v1
    private static readonly byte[] CodeMagic = Encoding.ASCII.GetBytes("DVWC1"); // DeadVault Watermark Code v1

    public static string GenerateUid()
        => $"DM-{Guid.NewGuid():N}"[..11].ToUpperInvariant(); // DM- + 8 hex chars

    public static string EmbedText(string input, string uid)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        uid = (uid ?? string.Empty).Trim();
        if (uid.Length == 0)
            uid = GenerateUid();

        var payloadBytes = Encoding.UTF8.GetBytes(uid);
        var bytes = BuildFrame(TextMagic, payloadBytes);
        var bits = BytesToBits(bytes);

        var builder = new StringBuilder(input.Length + bits.Count);
        int bitIndex = 0;

        foreach (var ch in input)
        {
            builder.Append(ch);
            if (bitIndex >= bits.Count)
                continue;

            if (ch == ' ')
            {
                builder.Append(bits[bitIndex++] == 0 ? TextBit0 : TextBit1);
            }
        }

        // If not enough spaces, append the remaining watermark at the end (prefixed by a space).
        if (bitIndex < bits.Count)
        {
            builder.Append(' ');
            while (bitIndex < bits.Count)
                builder.Append(bits[bitIndex++] == 0 ? TextBit0 : TextBit1);
        }

        return builder.ToString();
    }

    public static bool TryExtractTextUid(string input, out string uid, out string error)
    {
        uid = string.Empty;
        error = string.Empty;

        var bits = ExtractTextBits(input);
        if (bits.Count < (TextMagic.Length + 2) * 8)
        {
            error = "No text watermark found.";
            return false;
        }

        if (!TryParseFrame(bits, TextMagic, out var payload, out error))
            return false;

        uid = Encoding.UTF8.GetString(payload);
        return true;
    }

    public static string EmbedCode(string code, string payload)
    {
        if (string.IsNullOrWhiteSpace(code))
            return code;

        payload = (payload ?? string.Empty).Trim();
        if (payload.Length == 0)
            payload = GenerateUid();

        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var bytes = BuildFrame(CodeMagic, payloadBytes);
        var bits = BytesToBits(bytes);

        var lines = code.Replace("\r\n", "\n").Split('\n').ToList();
        int bitIndex = 0;

        for (int i = 0; i < lines.Count && bitIndex < bits.Count; i++)
        {
            // Append a single trailing char. Many compilers ignore trailing whitespace.
            lines[i] += bits[bitIndex++] == 0 ? " " : "\t";
        }

        while (bitIndex < bits.Count)
        {
            lines.Add(bits[bitIndex++] == 0 ? " " : "\t");
        }

        return string.Join("\n", lines);
    }

    public static bool TryExtractCodePayload(string input, out string payload, out string error)
    {
        payload = string.Empty;
        error = string.Empty;

        var bits = ExtractCodeBits(input);
        if (bits.Count < (CodeMagic.Length + 2) * 8)
        {
            error = "No code watermark found.";
            return false;
        }

        if (!TryParseFrame(bits, CodeMagic, out var bytes, out error))
            return false;

        payload = Encoding.UTF8.GetString(bytes);
        return true;
    }

    private static byte[] BuildFrame(byte[] magic, byte[] payload)
    {
        if (payload.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(payload), "Payload too large.");

        var frame = new byte[magic.Length + 2 + payload.Length];
        Buffer.BlockCopy(magic, 0, frame, 0, magic.Length);

        // Big-endian length.
        frame[magic.Length] = (byte)((payload.Length >> 8) & 0xFF);
        frame[magic.Length + 1] = (byte)(payload.Length & 0xFF);

        Buffer.BlockCopy(payload, 0, frame, magic.Length + 2, payload.Length);
        return frame;
    }

    private static List<int> BytesToBits(byte[] bytes)
    {
        var bits = new List<int>(bytes.Length * 8);
        foreach (var b in bytes)
        {
            for (int i = 7; i >= 0; i--)
                bits.Add((b >> i) & 1);
        }
        return bits;
    }

    private static List<int> ExtractTextBits(string input)
    {
        var bits = new List<int>();
        foreach (var ch in input)
        {
            if (ch == TextBit0) bits.Add(0);
            else if (ch == TextBit1) bits.Add(1);
        }
        return bits;
    }

    private static List<int> ExtractCodeBits(string input)
    {
        var bits = new List<int>();
        var lines = input.Replace("\r\n", "\n").Split('\n');

        foreach (var line in lines)
        {
            if (line.Length == 0)
                continue;

            var last = line[^1];
            if (last == ' ') bits.Add(0);
            else if (last == '\t') bits.Add(1);
        }

        return bits;
    }

    private static bool TryParseFrame(List<int> bits, byte[] magic, out byte[] payload, out string error)
    {
        payload = Array.Empty<byte>();
        error = string.Empty;

        // Decode bytes.
        int byteCount = bits.Count / 8;
        if (byteCount < magic.Length + 2)
        {
            error = "Watermark is incomplete.";
            return false;
        }

        var bytes = new byte[byteCount];
        for (int i = 0; i < byteCount; i++)
        {
            byte b = 0;
            for (int j = 0; j < 8; j++)
                b = (byte)((b << 1) | bits[i * 8 + j]);
            bytes[i] = b;
        }

        // Find magic at offset 0 (simple PoC).
        for (int i = 0; i < magic.Length; i++)
        {
            if (bytes[i] != magic[i])
            {
                error = "Watermark magic not found.";
                return false;
            }
        }

        int len = (bytes[magic.Length] << 8) | bytes[magic.Length + 1];
        if (len < 0 || magic.Length + 2 + len > bytes.Length)
        {
            error = "Watermark length is invalid or incomplete.";
            return false;
        }

        payload = new byte[len];
        Buffer.BlockCopy(bytes, magic.Length + 2, payload, 0, len);
        return true;
    }
}

