namespace PrintBridge.Printing;

/// <summary>
/// Streaming decoder for Apple Raster (URF / UNIRAST) documents sent by AirPrint
/// clients. Pages are decoded one at a time so large multi-page jobs never need
/// every page in memory at once.
/// </summary>
internal sealed class UrfReader
{
    internal readonly record struct PageHeader(int BitsPerPixel, int ColorSpace, int Width, int Height, int Dpi);

    private const int FileHeaderLength = 12;
    private const int PageHeaderLength = 32;
    private const int ColorSpaceCmyk = 6;

    private readonly byte[] _data;
    private readonly int _pageCount;
    private int _pos = FileHeaderLength;
    private int _pagesRead;

    internal UrfReader(byte[] data)
    {
        if (!IsUrf(data)) throw new ArgumentException("Not a URF document.", nameof(data));
        _data = data;
        _pageCount = ReadInt32BigEndian(data, 8);
    }

    internal static bool IsUrf(byte[] data) =>
        data.Length >= FileHeaderLength && data[0] == 'U' && data[1] == 'N' && data[2] == 'I' && data[3] == 'R'
        && data[4] == 'A' && data[5] == 'S' && data[6] == 'T';

    /// <summary>Header of the next undecoded page, or null when the document is exhausted.</summary>
    internal PageHeader? PeekHeader()
    {
        if ((_pageCount > 0 && _pagesRead >= _pageCount) || _pagesRead >= 500
            || _pos + PageHeaderLength > _data.Length)
            return null;

        var header = new PageHeader(_data[_pos], _data[_pos + 1],
            ReadInt32BigEndian(_data, _pos + 12), ReadInt32BigEndian(_data, _pos + 16), ReadInt32BigEndian(_data, _pos + 20));

        if (header.BitsPerPixel is not (1 or 8 or 24 or 32)
            || header.Width is <= 0 or > 20000 || header.Height is <= 0 or > 30000
            || (long)header.Width * header.Height > 80_000_000L)
            return null;

        return header;
    }

    /// <summary>
    /// Decodes the next page, delivering each row as 24-bit BGR (the GDI+ memory
    /// layout). Returns false when the page data is truncated.
    /// </summary>
    internal bool ReadPage(Action<int, byte[]> writeBgrRow)
    {
        if (PeekHeader() is not { } header) return false;
        _pos += PageHeaderLength;
        _pagesRead++;

        int bytesPerPixel = Math.Max(1, header.BitsPerPixel / 8);
        int bytesPerRow = header.BitsPerPixel == 1 ? (header.Width + 7) / 8 : header.Width * bytesPerPixel;
        // 0x80 blanks the rest of the line: white is all-zero bits for 1-bit and CMYK, all-one otherwise.
        byte blank = header.BitsPerPixel == 1 || header.ColorSpace == ColorSpaceCmyk ? (byte)0x00 : (byte)0xFF;
        var line = new byte[bytesPerRow];
        var bgr = new byte[header.Width * 3];

        for (int y = 0; y < header.Height;)
        {
            if (!ReadLine(line, bytesPerPixel, blank, out int repeat))
            {
                _pos = _data.Length;
                return false;
            }

            ToBgr(line, bgr, header);
            for (int r = Math.Min(repeat, header.Height - y); r > 0; r--)
                writeBgrRow(y++, bgr);
        }

        return true;
    }

    private bool ReadLine(byte[] line, int bytesPerPixel, byte blank, out int repeat)
    {
        // Each line starts with a repeat count (0 = once), followed by PackBits-style runs.
        repeat = 0;
        if (_pos >= _data.Length) return false;
        repeat = _data[_pos++] + 1;
        int rowPos = 0;

        while (rowPos < line.Length)
        {
            if (_pos >= _data.Length) return false;
            int code = _data[_pos++];

            if (code == 0x80)
            {
                Array.Fill(line, blank, rowPos, line.Length - rowPos);
                return true;
            }

            if (code < 0x80)
            {
                // Repeat the next pixel (code + 1) times.
                if (_pos + bytesPerPixel > _data.Length) return false;
                for (int i = 0; i <= code && rowPos < line.Length; i++)
                {
                    int copy = Math.Min(bytesPerPixel, line.Length - rowPos);
                    Array.Copy(_data, _pos, line, rowPos, copy);
                    rowPos += copy;
                }
                _pos += bytesPerPixel;
            }
            else
            {
                // Copy (257 - code) pixels verbatim.
                int wanted = Math.Min((257 - code) * bytesPerPixel, line.Length - rowPos);
                if (_pos + wanted > _data.Length) return false;
                Array.Copy(_data, _pos, line, rowPos, wanted);
                _pos += wanted;
                rowPos += wanted;
            }
        }

        return true;
    }

    private static void ToBgr(byte[] line, byte[] bgr, PageHeader header)
    {
        for (int x = 0; x < header.Width; x++)
        {
            int o = x * 3;
            switch (header.BitsPerPixel)
            {
                case 1:
                    bgr[o] = bgr[o + 1] = bgr[o + 2] = (line[x >> 3] & (0x80 >> (x & 7))) != 0 ? (byte)0 : (byte)255;
                    break;
                case 8:
                    bgr[o] = bgr[o + 1] = bgr[o + 2] = line[x];
                    break;
                case 24:
                    bgr[o] = line[x * 3 + 2];
                    bgr[o + 1] = line[x * 3 + 1];
                    bgr[o + 2] = line[x * 3];
                    break;
                default:
                    int s = x * 4;
                    if (header.ColorSpace == ColorSpaceCmyk)
                    {
                        int k = 255 - line[s + 3];
                        bgr[o] = (byte)((255 - line[s + 2]) * k / 255);
                        bgr[o + 1] = (byte)((255 - line[s + 1]) * k / 255);
                        bgr[o + 2] = (byte)((255 - line[s]) * k / 255);
                    }
                    else
                    {
                        bgr[o] = line[s + 2];
                        bgr[o + 1] = line[s + 1];
                        bgr[o + 2] = line[s];
                    }
                    break;
            }
        }
    }

    private static int ReadInt32BigEndian(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
