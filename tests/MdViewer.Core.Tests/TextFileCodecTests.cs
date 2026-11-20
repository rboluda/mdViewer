using System.Text;
using MdViewer.Core.Documents;

namespace MdViewer.Core.Tests;

public class TextFileCodecTests
{
    public static TheoryData<byte[]> RoundTripSamples()
    {
        byte[] utf8Bom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("con BOM\r\n")];
        byte[] utf16 = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("utf16 é\r\n")];
        return new TheoryData<byte[]>(
            Encoding.UTF8.GetBytes("# Hola\r\n\r\nÁrbol, ñandú\r\n"),
            Encoding.UTF8.GetBytes("# Unix\nlínea 2\n"),
            utf8Bom,
            utf16,
            GetCp1252("acentos en ANSI: canción\r\n"),
            Encoding.UTF8.GetBytes("sin salto final"),
            Array.Empty<byte>());
    }

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    public void Decode_then_encode_returns_identical_bytes(byte[] original)
    {
        var content = TextFileCodec.Decode(original);

        var bytes = TextFileCodec.Encode(content.Text, content.Format);

        Assert.Equal(original, bytes);
    }

    [Fact]
    public void Decoded_text_uses_lf_only()
    {
        var content = TextFileCodec.Decode(Encoding.UTF8.GetBytes("a\r\nb\rc\n"));

        Assert.Equal("a\nb\nc\n", content.Text);
    }

    [Fact]
    public void Invalid_utf8_falls_back_to_windows_1252()
    {
        var content = TextFileCodec.Decode(GetCp1252("canción"));

        Assert.Equal("canción", content.Text);
        Assert.Equal(1252, content.Format.Encoding.CodePage);
    }

    [Theory]
    [InlineData("a\r\nb\r\n", LineEnding.CrLf)]
    [InlineData("a\nb\n", LineEnding.Lf)]
    [InlineData("a\rb\r", LineEnding.Cr)]
    [InlineData("sin saltos", LineEnding.CrLf)]
    public void Detects_predominant_line_ending(string text, LineEnding expected)
        => Assert.Equal(expected, TextFileCodec.DetectLineEnding(text));

    private static byte[] GetCp1252(string text)
    {
        _ = TextFileCodec.LegacyFallback; // registra el proveedor de páginas de códigos
        return Encoding.GetEncoding(1252).GetBytes(text);
    }
}
