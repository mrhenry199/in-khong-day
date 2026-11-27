using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace PrintBridge.Printing;

public sealed class WindowsPrinterBridge
{
    public static IReadOnlyList<string> GetInstalledPrinters()
    {
        var list = new List<string>();
        try
        {
            foreach (string name in PrinterSettings.InstalledPrinters)
                list.Add(name);
        }
        catch
        {
        }

        return list;
    }

    /// <summary>
    /// True for queues that print to a physical device without user interaction
    /// (excludes Print to PDF, XPS, OneNote, Fax and similar virtual printers).
    /// </summary>
    public static bool IsShareableQueue(string printerName)
    {
        try { return HasUnattendedPort(printerName); }
        catch { return false; }
    }

    public PrintResult SendToSpooler(
        string printerName,
        byte[] data,
        string documentName = "PrintBridge Job",
        string mimeType = "application/pdf",
        PrintOptions? options = null, bool fromNetwork = false)
    {
        if (string.IsNullOrWhiteSpace(printerName) || data == null || data.Length == 0)
            return PrintResult.Fail("No printer or empty document was provided.");

        var extension = Path.GetExtension(documentName).ToLowerInvariant();
        var mime = (mimeType ?? string.Empty).ToLowerInvariant();

        if (string.IsNullOrEmpty(extension))
            extension = ExtensionFromMime(mime);

        try
        {
            if (fromNetwork && !HasUnattendedPort(printerName))
                return PrintResult.Fail("This queue needs an interactive output port or its port could not be verified. Select a physical printer.");

            if (IsPdf(data) || mime.Contains("pdf"))
            {
                return PrintPdfNative(printerName, data, documentName, options)
                    ? PrintResult.Ok()
                    : PrintResult.Fail("The PDF could not be rendered. Make sure the printer is online and its driver is working.");
            }

            if (UrfReader.IsUrf(data) || mime.Contains("urf"))
            {
                return PrintUrfNative(printerName, data, documentName, options)
                    ? PrintResult.Ok()
                    : PrintResult.Fail("The AirPrint raster document could not be decoded. Try printing the document as PDF instead.");
            }

            if (mime.Contains("pwg") || mime.Contains("raster"))
            {
                return PrintResult.Fail("PWG raster documents are not supported directly. Send the job as PDF or an image instead.");
            }

            if (IsImage(data, extension, mime))
            {
                return PrintImageNative(printerName, data, documentName, options)
                    ? PrintResult.Ok()
                    : PrintResult.Fail("The image could not be printed. The format may not be supported by the printer driver.");
            }

            var text = ExtractPrintableText(data, extension, mime);
            if (text != null)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return PrintResult.Fail("The document did not contain any printable text.");

                return PrintTextNative(printerName, text, documentName, options)
                    ? PrintResult.Ok()
                    : PrintResult.Fail("The text document could not be printed.");
            }

            if (IsRawPrinterLanguage(mime, extension))
            {
                return SendRawBytesToSpooler(printerName, data, documentName)
                    ? PrintResult.Ok()
                    : PrintResult.Fail("The printer rejected the raw print data. Check that the printer driver understands PCL/PostScript.");
            }

            if (DocumentTextExtractor.LooksLikeText(data))
            {
                return PrintTextNative(printerName, DocumentTextExtractor.DecodeText(data), documentName, options)
                    ? PrintResult.Ok()
                    : PrintResult.Fail("The text document could not be printed.");
            }

            return PrintResult.Fail(
                $"Unsupported document format '{mimeType}'{(!string.IsNullOrEmpty(extension) ? $" ({extension})" : "")}. " +
                "Supported formats: PDF, JPG, PNG, BMP, GIF, TIFF, TXT, CSV, HTML, RTF, DOCX, XLSX, PPTX, ODT, ODS and ODP.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Printing error for {printerName}: {ex}");
            return PrintResult.Fail($"Printing failed: {ex.Message}");
        }
    }

    public bool PrintTestPage(string printerName)
    {
        try
        {
            using var doc = new PrintDocument();
            doc.PrinterSettings.PrinterName = printerName;

            if (!doc.PrinterSettings.IsValid)
                return false;

            doc.DocumentName = "Easy Print Easy Scan Test Page";
            doc.PrintController = new StandardPrintController();

            doc.PrintPage += (s, e) =>
            {
                if (e.Graphics == null) return;

                using var titleFont = new Font("Segoe UI", 20, FontStyle.Bold);
                using var bodyFont = new Font("Segoe UI", 11, FontStyle.Regular);
                using var monoFont = new Font("Consolas", 10, FontStyle.Regular);
                using var brush = new SolidBrush(Color.FromArgb(30, 30, 30));
                using var accentBrush = new SolidBrush(Color.FromArgb(95, 107, 69));

                int x = 60, y = 60;

                e.Graphics.DrawString("Easy Print Easy Scan Test Page", titleFont, accentBrush, x, y);
                y += 50;

                e.Graphics.DrawString("Success! This printer is correctly shared over Wi-Fi.", bodyFont, brush, x, y);
                y += 35;

                var details = $"""
                    Queue Name:      {printerName}
                    Date & Time:     {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                    Computer:        {Environment.MachineName}
                    Protocols:       AirPrint (iOS / iPadOS) & Mopria / IPP Everywhere (Android)
                    Engine:          Windows GDI Direct Rendering
                    Status:          Active and Ready
                    """;

                e.Graphics.DrawString(details, monoFont, brush, x, y);
            };

            doc.Print();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to print test page: {ex.Message}");
            return false;
        }
    }

    private static bool PrintPdfNative(string printerName, byte[] data, string documentName, PrintOptions? options)
    {
        try
        {
            return Task.Run(async () =>
            {
                // Load from memory so no copy of the document is left in %TEMP%.
                using var source = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(source.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(data);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }

                var pdfDoc = await PdfDocument.LoadFromStreamAsync(source);
                uint pageCount = pdfDoc.PageCount;
                if (pageCount == 0) return false;

                using var printDoc = new PrintDocument();
                printDoc.PrinterSettings.PrinterName = printerName;
                if (!printDoc.PrinterSettings.IsValid) return false;

                ApplyPrintOptions(printDoc, options);
                printDoc.DocumentName = documentName;
                printDoc.PrintController = new StandardPrintController();

                SizeF PageSize(uint index)
                {
                    using var p = pdfDoc.GetPage(index);
                    return new SizeF((float)(p.Size.Width * 100 / 96), (float)(p.Size.Height * 100 / 96));
                }

                var pagesPerSheet = NormalizePagesPerSheet(options?.PagesPerSheet ?? 1);
                if (pagesPerSheet == 1 && options?.MediaWidthHundredthsMm == null)
                    TrySelectPaper(printDoc, PageSize(0));
                var (columns, rows) = ConfigureNUp(printDoc, pagesPerSheet);
                var dpi = options?.Quality == 5 ? 600 : 300;
                double displayScale;
                using (var first = pdfDoc.GetPage(0))
                    displayScale = MeasureRenderScale(first);
                uint currentPage = 0;

                if (pagesPerSheet == 1)
                {
                    // Turn the sheet per page, like a desktop PDF viewer does for mixed documents.
                    printDoc.QueryPageSettings += (s, e) =>
                    {
                        if (currentPage >= pageCount) return;
                        var size = PageSize(currentPage);
                        e.PageSettings.Landscape = size.Width > size.Height;
                    };
                }

                printDoc.PrintPage += (s, e) =>
                {
                    if (e.Graphics == null)
                    {
                        e.Cancel = true;
                        return;
                    }

                    for (int cell = 0; cell < pagesPerSheet && currentPage < pageCount; cell++, currentPage++)
                    {
                        using var page = pdfDoc.GetPage(currentPage);
                        var physical = new SizeF((float)(page.Size.Width * 100 / 96), (float)(page.Size.Height * 100 / 96));
                        RenderPdfPage(page, pagesPerSheet == 1 ? dpi : 200, displayScale, img =>
                        {
                            if (pagesPerSheet == 1) DrawPage(e, img, physical);
                            else DrawImageInCell(e, img, cell, columns, rows);
                        });
                    }

                    e.HasMorePages = currentPage < pageCount;
                };

                printDoc.Print();
                return true;
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Native Windows.Data.Pdf print failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Windows.Data.Pdf multiplies the requested size by the display scale (e.g. 1.8 at
    /// 180 %). Measure it once so print renders have the intended pixel size on every host.
    /// </summary>
    private static double MeasureRenderScale(PdfPage page)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = 100 }).AsTask().GetAwaiter().GetResult();
            stream.Seek(0);
            using var netStream = stream.AsStreamForRead();
            using var img = Image.FromStream(netStream);
            return Math.Clamp(img.Width / 100.0, 0.5, 4);
        }
        catch
        {
            return 1;
        }
    }

    private static void RenderPdfPage(PdfPage page, int dpi, double displayScale, Action<Image> draw)
    {
        // Render at print resolution; the default PDF render size is 96 DPI, which prints blurry.
        const double maxPixels = 36_000_000;
        double width = page.Size.Width * dpi / 96, height = page.Size.Height * dpi / 96;
        var shrink = Math.Min(1, Math.Sqrt(maxPixels / Math.Max(1, width * height)));
        var renderOptions = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, width * shrink / displayScale),
            DestinationHeight = (uint)Math.Max(1, height * shrink / displayScale)
        };

        using var stream = new InMemoryRandomAccessStream();
        page.RenderToStreamAsync(stream, renderOptions).AsTask().GetAwaiter().GetResult();
        stream.Seek(0);
        using var netStream = stream.AsStreamForRead();
        using var img = Image.FromStream(netStream);
        draw(img);
    }

    private static bool PrintImageNative(string printerName, byte[] data, string documentName, PrintOptions? options)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);

            using var printDoc = new PrintDocument();
            printDoc.PrinterSettings.PrinterName = printerName;
            if (!printDoc.PrinterSettings.IsValid) return false;

            ApplyPrintOptions(printDoc, options);
            printDoc.DocumentName = documentName;
            printDoc.PrintController = new StandardPrintController();

            FrameDimension? dimension = null;
            int frameCount = 1;

            if (img.FrameDimensionsList.Length > 0)
            {
                dimension = new FrameDimension(img.FrameDimensionsList[0]);
                try { frameCount = Math.Max(1, img.GetFrameCount(dimension)); }
                catch { frameCount = 1; }
            }

            if (frameCount == 1)
                ApplyExifOrientation(img);

            var pagesPerSheet = NormalizePagesPerSheet(options?.PagesPerSheet ?? 1);
            if (pagesPerSheet == 1 && options?.Landscape == null)
                printDoc.DefaultPageSettings.Landscape = img.Width > img.Height;
            var (columns, rows) = ConfigureNUp(printDoc, pagesPerSheet);
            int frameIndex = 0;

            printDoc.PrintPage += (s, e) =>
            {
                if (e.Graphics == null)
                {
                    e.Cancel = true;
                    return;
                }

                for (int cell = 0; cell < pagesPerSheet && frameIndex < frameCount; cell++, frameIndex++)
                {
                    if (dimension != null)
                    {
                        try { img.SelectActiveFrame(dimension, frameIndex); } catch { }
                    }

                    if (pagesPerSheet == 1) DrawPage(e, img, null);
                    else DrawImageInCell(e, img, cell, columns, rows);
                }

                e.HasMorePages = frameIndex < frameCount;
            };

            printDoc.Print();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Image print failed: {ex.Message}");
            return false;
        }
    }

    private static bool PrintUrfNative(string printerName, byte[] data, string documentName, PrintOptions? options)
    {
        try
        {
            if (!UrfReader.IsUrf(data)) return false;
            var reader = new UrfReader(data);
            if (reader.PeekHeader() is not { } first) return false;

            using var printDoc = new PrintDocument();
            printDoc.PrinterSettings.PrinterName = printerName;
            if (!printDoc.PrinterSettings.IsValid) return false;

            ApplyPrintOptions(printDoc, options);
            printDoc.DocumentName = documentName;
            printDoc.PrintController = new StandardPrintController();

            var pagesPerSheet = NormalizePagesPerSheet(options?.PagesPerSheet ?? 1);
            if (pagesPerSheet == 1 && options?.MediaWidthHundredthsMm == null && UrfPhysicalSize(first) is { } firstSize)
                TrySelectPaper(printDoc, firstSize);
            var (columns, rows) = ConfigureNUp(printDoc, pagesPerSheet);

            if (pagesPerSheet == 1)
            {
                printDoc.QueryPageSettings += (s, e) =>
                {
                    if (reader.PeekHeader() is { } next)
                        e.PageSettings.Landscape = next.Width > next.Height;
                };
            }

            printDoc.PrintPage += (s, e) =>
            {
                if (e.Graphics == null)
                {
                    e.Cancel = true;
                    return;
                }

                // Decode one page at a time; a 600 DPI A4 page alone is ~100 MB of pixels.
                for (int cell = 0; cell < pagesPerSheet && reader.PeekHeader() is { } header; cell++)
                {
                    using var page = DecodeUrfPage(reader, header);
                    if (pagesPerSheet == 1) DrawPage(e, page, UrfPhysicalSize(header));
                    else DrawImageInCell(e, page, cell, columns, rows);
                }

                e.HasMorePages = reader.PeekHeader() != null;
            };

            printDoc.Print();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"URF print failed: {ex.Message}");
            return false;
        }
    }

    private static SizeF? UrfPhysicalSize(UrfReader.PageHeader header) =>
        header.Dpi > 0 ? new SizeF(header.Width * 100f / header.Dpi, header.Height * 100f / header.Dpi) : null;

    private static Bitmap DecodeUrfPage(UrfReader reader, UrfReader.PageHeader header)
    {
        var bitmap = new Bitmap(header.Width, header.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap))
            g.Clear(Color.White);

        var data = bitmap.LockBits(new Rectangle(0, 0, header.Width, header.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            reader.ReadPage((y, bgr) => Marshal.Copy(bgr, 0, IntPtr.Add(data.Scan0, y * data.Stride), bgr.Length));
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static bool PrintTextNative(string printerName, string text, string documentName, PrintOptions? options)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            using var printDoc = new PrintDocument();
            printDoc.PrinterSettings.PrinterName = printerName;
            if (!printDoc.PrinterSettings.IsValid) return false;

            ApplyPrintOptions(printDoc, options);
            printDoc.DocumentName = documentName;
            printDoc.PrintController = new StandardPrintController();

            using var font = new Font("Segoe UI", 10.5f);
            using var brush = new SolidBrush(Color.Black);

            var lines = text.Replace("\0", string.Empty)
                .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var pagesPerSheet = NormalizePagesPerSheet(options?.PagesPerSheet ?? 1);
            var (columns, rows) = ConfigureNUp(printDoc, pagesPerSheet);
            int linesPerPage = 0;
            int lineIndex = 0;

            printDoc.PrintPage += (s, e) =>
            {
                if (e.Graphics == null)
                {
                    e.Cancel = true;
                    return;
                }

                var bounds = e.MarginBounds;
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    e.HasMorePages = false;
                    return;
                }

                float lineHeight = font.GetHeight(e.Graphics) + 2;

                if (pagesPerSheet == 1)
                {
                    float y = bounds.Top;

                    while (lineIndex < lines.Length && y + lineHeight <= bounds.Bottom)
                    {
                        e.Graphics.DrawString(lines[lineIndex], font, brush, bounds.Left, y);
                        y += lineHeight;
                        lineIndex++;
                    }
                }
                else
                {
                    float logicalWidth = Math.Min(bounds.Width, bounds.Height);
                    float logicalHeight = Math.Max(bounds.Width, bounds.Height);

                    if (linesPerPage <= 0)
                        linesPerPage = Math.Max(1, (int)Math.Floor(logicalHeight / lineHeight));

                    for (int cell = 0; cell < pagesPerSheet && lineIndex < lines.Length; cell++)
                    {
                        var cellBounds = GetCellBounds(bounds, cell, columns, rows);
                        float scale = Math.Min(cellBounds.Width / logicalWidth, cellBounds.Height / logicalHeight);
                        if (scale <= 0) continue;

                        float offsetX = cellBounds.Left + (cellBounds.Width - logicalWidth * scale) / 2;
                        float offsetY = cellBounds.Top + (cellBounds.Height - logicalHeight * scale) / 2;

                        var state = e.Graphics.Save();
                        try
                        {
                            e.Graphics.TranslateTransform(offsetX, offsetY);
                            e.Graphics.ScaleTransform(scale, scale);

                            float y = 0;
                            for (int i = 0; i < linesPerPage && lineIndex < lines.Length; i++)
                            {
                                e.Graphics.DrawString(lines[lineIndex], font, brush, 0, y);
                                y += lineHeight;
                                lineIndex++;
                            }
                        }
                        finally
                        {
                            e.Graphics.Restore(state);
                        }
                    }
                }

                e.HasMorePages = lineIndex < lines.Length;
            };

            printDoc.Print();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Text print failed: {ex.Message}");
            return false;
        }
    }

    private static int NormalizePagesPerSheet(int value) =>
        value is 2 or 4 or 6 or 9 or 16 ? value : 1;

    private static (int Columns, int Rows) GetNUpGrid(int pagesPerSheet) => pagesPerSheet switch
    {
        2 => (2, 1),
        4 => (2, 2),
        6 => (3, 2),
        9 => (3, 3),
        16 => (4, 4),
        _ => (1, 1)
    };

    private static (int Columns, int Rows) ConfigureNUp(PrintDocument printDoc, int pagesPerSheet)
    {
        var grid = GetNUpGrid(pagesPerSheet);
        if (pagesPerSheet > 1 && grid.Columns > grid.Rows)
            printDoc.DefaultPageSettings.Landscape = true;
        return grid;
    }

    private static RectangleF GetCellBounds(RectangleF bounds, int cellIndex, int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);

        float cellWidth = bounds.Width / columns;
        float cellHeight = bounds.Height / rows;
        int column = Math.Max(0, cellIndex % columns);
        int row = Math.Max(0, cellIndex / columns);

        return new RectangleF(
            bounds.Left + column * cellWidth,
            bounds.Top + row * cellHeight,
            cellWidth,
            cellHeight);
    }

    private static void DrawImageInCell(PrintPageEventArgs e, Image image, int cellIndex, int columns, int rows)
    {
        var bounds = e.MarginBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var cell = GetCellBounds(bounds, cellIndex, columns, rows);
        float padding = Math.Min(cell.Width, cell.Height) * 0.04f;
        if (padding > 1 && cell.Width > padding * 2 && cell.Height > padding * 2)
        {
            cell = new RectangleF(
                cell.Left + padding,
                cell.Top + padding,
                cell.Width - padding * 2,
                cell.Height - padding * 2);
        }

        float ratioX = cell.Width / image.Width;
        float ratioY = cell.Height / image.Height;
        float ratio = Math.Min(ratioX, ratioY);

        int width = Math.Max(1, (int)(image.Width * ratio));
        int height = Math.Max(1, (int)(image.Height * ratio));
        int x = (int)(cell.Left + (cell.Width - width) / 2);
        int y = (int)(cell.Top + (cell.Height - height) / 2);

        e.Graphics?.DrawImage(image, x, y, width, height);
    }

    /// <summary>
    /// Draws one document page on the sheet. When the page's physical size is known
    /// (PDF, URF) it is placed at actual size on the paper and only shrunk if it is
    /// larger than the sheet, matching how the host prints a PDF. Images without a
    /// meaningful size are fitted to the printable area.
    /// </summary>
    private static void DrawPage(PrintPageEventArgs e, Image image, SizeF? physicalSize)
    {
        var g = e.Graphics;
        if (g == null) return;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        RectangleF target;
        if (physicalSize is { Width: > 0, Height: > 0 } size)
        {
            // Graphics origin is the printable-area corner; the sheet starts at minus the hard margins.
            var sheet = new RectangleF(-e.PageSettings.HardMarginX, -e.PageSettings.HardMarginY,
                e.PageBounds.Width, e.PageBounds.Height);
            target = FitRect(sheet, size.Width, size.Height, enlarge: false);
        }
        else
        {
            target = FitRect(g.VisibleClipBounds, image.Width, image.Height, enlarge: true);
        }

        g.DrawImage(image, target);
    }

    private static RectangleF FitRect(RectangleF area, float width, float height, bool enlarge)
    {
        var scale = Math.Min(area.Width / width, area.Height / height);
        if (!enlarge) scale = Math.Min(scale, 1f);
        float w = width * scale, h = height * scale;
        return new RectangleF(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }

    /// <summary>
    /// Selects the printer's paper matching a size in hundredths of an inch (either
    /// orientation). Leaves the queue default untouched when the driver has no match.
    /// </summary>
    private static bool TrySelectPaper(PrintDocument printDoc, SizeF size)
    {
        const float tolerance = 8; // ~2 mm
        var sizes = printDoc.PrinterSettings.PaperSizes.Cast<PaperSize>().Where(p => p.Width > 0 && p.Height > 0).ToList();
        var match = sizes.FirstOrDefault(p => Math.Abs(p.Width - size.Width) <= tolerance && Math.Abs(p.Height - size.Height) <= tolerance)
            ?? sizes.FirstOrDefault(p => Math.Abs(p.Width - size.Height) <= tolerance && Math.Abs(p.Height - size.Width) <= tolerance);
        if (match == null) return false;
        printDoc.DefaultPageSettings.PaperSize = match;
        return true;
    }

    private static void ApplyExifOrientation(Image img)
    {
        const int orientationTag = 0x0112;
        try
        {
            if (!img.PropertyIdList.Contains(orientationTag)) return;
            var value = img.GetPropertyItem(orientationTag)?.Value;
            if (value is not { Length: >= 2 }) return;

            var flip = BitConverter.ToUInt16(value, 0) switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            };
            if (flip == RotateFlipType.RotateNoneFlipNone) return;
            img.RotateFlip(flip);
            img.RemovePropertyItem(orientationTag);
        }
        catch { }
    }

    private static void ApplyPrintOptions(PrintDocument printDoc, PrintOptions? options)
    {
        if (options == null) return;

        try
        {
            if (options.Copies > 1)
                printDoc.PrinterSettings.Copies = (short)Math.Clamp(options.Copies, 1, 99);
        }
        catch { }

        try
        {
            if (options.Duplex && printDoc.PrinterSettings.CanDuplex)
                printDoc.PrinterSettings.Duplex = options.DuplexShortEdge ? Duplex.Horizontal : Duplex.Vertical;
            else if (!options.Duplex)
                printDoc.PrinterSettings.Duplex = Duplex.Simplex;
        }
        catch { }

        try
        {
            var kind = options.Quality switch
            {
                3 => PrinterResolutionKind.Draft,
                5 => PrinterResolutionKind.High,
                _ => (PrinterResolutionKind?)null
            };
            if (kind != null && printDoc.PrinterSettings.PrinterResolutions.Cast<PrinterResolution>()
                    .FirstOrDefault(r => r.Kind == kind) is { } resolution)
                printDoc.DefaultPageSettings.PrinterResolution = resolution;
        }
        catch { }

        try
        {
            if (options.Color.HasValue)
                printDoc.DefaultPageSettings.Color = options.Color.Value;
        }
        catch { }

        try
        {
            // IPP sizes are hundredths of a millimetre; PaperSize uses hundredths of an inch.
            if (options.MediaWidthHundredthsMm is int mediaWidth
                && options.MediaHeightHundredthsMm is int mediaHeight)
                TrySelectPaper(printDoc, new SizeF(mediaWidth / 25.4f, mediaHeight / 25.4f));
        }
        catch { }

        try
        {
            if (options.Landscape is bool landscape)
                printDoc.DefaultPageSettings.Landscape = landscape;
        }
        catch { }
    }

    /// <summary>
    /// Returns the spooler queue status bits (PRINTER_INFO_6 / PRINTER_STATUS_*).
    /// Returns PRINTER_STATUS_NOT_AVAILABLE when the queue cannot be opened.
    /// </summary>
    public uint GetQueueStatusBits(string printerName)
    {
        if (!NativeMethods.OpenPrinterW(printerName, out var handle, IntPtr.Zero))
            return 0x1000;

        try
        {
            NativeMethods.GetPrinterW(handle, 6, IntPtr.Zero, 0, out var needed);
            if (needed <= 0) return 0;

            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                return NativeMethods.GetPrinterW(handle, 6, buffer, needed, out _)
                    ? (uint)Marshal.ReadInt32(buffer)
                    : 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            NativeMethods.ClosePrinter(handle);
        }
    }

    private static string? ExtractPrintableText(byte[] data, string extension, string mime)
    {
        if (extension is ".html" or ".htm" || mime.Contains("html"))
            return DocumentTextExtractor.TextFromHtml(DocumentTextExtractor.DecodeText(data));

        if (extension == ".rtf" || mime.Contains("rtf"))
            return DocumentTextExtractor.TextFromRtf(DocumentTextExtractor.DecodeText(data));

        if (DocumentTextExtractor.IsOfficeExtension(extension))
            return DocumentTextExtractor.ExtractOfficeText(data, extension);

        if (DocumentTextExtractor.IsTextFormat(extension, mime))
            return DocumentTextExtractor.DecodeText(data);

        return null;
    }

    private static string ExtensionFromMime(string mime) => mime switch
    {
        "application/pdf" => ".pdf",
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/bmp" => ".bmp",
        "image/gif" => ".gif",
        "image/tiff" => ".tif",
        "text/plain" => ".txt",
        "text/csv" => ".csv",
        "text/markdown" => ".md",
        "text/html" => ".html",
        "application/rtf" => ".rtf",
        "application/json" => ".json",
        "application/xml" or "text/xml" => ".xml",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
        "application/vnd.oasis.opendocument.text" => ".odt",
        "application/vnd.oasis.opendocument.spreadsheet" => ".ods",
        "application/vnd.oasis.opendocument.presentation" => ".odp",
        _ => string.Empty
    };

    private static bool IsPdf(byte[] data) =>
        data.Length >= 4 && data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F';

    private static bool IsImage(byte[] data, string extension, string mime)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return true;

        if (data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return true;

        if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D)
            return true;

        if (data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8')
            return true;

        if (data.Length >= 4
            && ((data[0] == 'I' && data[1] == 'I' && data[2] == 0x2A && data[3] == 0x00)
                || (data[0] == 'M' && data[1] == 'M' && data[2] == 0x00 && data[3] == 0x2A)))
            return true;

        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            && !mime.Contains("urf", StringComparison.OrdinalIgnoreCase)
            && !mime.Contains("pwg", StringComparison.OrdinalIgnoreCase)
            && !mime.Contains("svg", StringComparison.OrdinalIgnoreCase))
            return true;

        return extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff"
            or ".ico" or ".webp" or ".wmf" or ".emf";
    }

    private static bool IsRawPrinterLanguage(string mime, string extension) =>
        mime.Contains("postscript", StringComparison.OrdinalIgnoreCase)
        || mime.Contains("pcl", StringComparison.OrdinalIgnoreCase)
        || extension is ".ps" or ".pcl" or ".prn";

    private static bool SendRawBytesToSpooler(string printerName, byte[] data, string docName)
    {
        if (!NativeMethods.OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
            return false;

        try
        {
            var di = new NativeMethods.DOCINFOA
            {
                pDocName = docName,
                pDataType = "RAW"
            };

            if (!NativeMethods.StartDocPrinter(hPrinter, 1, di))
                return false;

            try
            {
                if (!NativeMethods.StartPagePrinter(hPrinter))
                    return false;

                try
                {
                    var pUnmanaged = Marshal.AllocCoTaskMem(data.Length);
                    try
                    {
                        Marshal.Copy(data, 0, pUnmanaged, data.Length);
                        return NativeMethods.WritePrinter(hPrinter, pUnmanaged, data.Length, out _);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pUnmanaged);
                    }
                }
                finally
                {
                    NativeMethods.EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                NativeMethods.EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            NativeMethods.ClosePrinter(hPrinter);
        }
    }

    private static bool HasUnattendedPort(string printerName)
    {
        if (!NativeMethods.OpenPrinterW(printerName, out var handle, IntPtr.Zero)) return false;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            NativeMethods.GetPrinterW(handle, 5, buffer, 0, out var bytes);
            if (bytes <= 0) return false;
            buffer = Marshal.AllocHGlobal(bytes);
            if (!NativeMethods.GetPrinterW(handle, 5, buffer, bytes, out _)) return false;
            // PRINTER_INFO_5 starts with pointers to the printer and port names.
            var ports = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, IntPtr.Size));
            return !string.IsNullOrWhiteSpace(ports) && ports.Split(',').All(port =>
                !new[] { "FILE:", "PORTPROMPT:", "SHRFAX:", "NUL:" }.Contains(port.Trim(), StringComparer.OrdinalIgnoreCase)
                && !port.Contains("onenote", StringComparison.OrdinalIgnoreCase)
                && !port.Contains("foxit", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            NativeMethods.ClosePrinter(handle);
        }
    }

    private static class NativeMethods
    {
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern bool OpenPrinterW(string printer, out IntPtr handle, IntPtr defaults);
        [DllImport("winspool.drv", ExactSpelling = true)]
        internal static extern bool GetPrinterW(IntPtr handle, int level, IntPtr buffer, int size, out int needed);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string? pDocName;
            [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)] public string? pDataType;
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true,
            CharSet = CharSet.Ansi, ExactSpelling = true)]
        public static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true,
            CharSet = CharSet.Ansi, ExactSpelling = true)]
        public static extern bool StartDocPrinter(IntPtr hPrinter, int level,
            [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true)]
        public static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true)]
        public static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true)]
        public static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true)]
        public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);
    }
}
