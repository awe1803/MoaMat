using System.Globalization;
using System.Text;
using MoaMat.Domain.Cylinders;
using MoaMat.Domain.Inventory;

namespace MoaMat.Infrastructure.Documents;

/// <summary>
/// Renders the "fiche de vie" of a cylinder as a small, dependency-free PDF
/// (A4, standard Helvetica, WinAnsi encoding).
/// </summary>
/// <remarks>
/// <para>The document is written by hand rather than through a PDF library: the
/// sheet is plain text (title, characteristics, timeline), a WebAssembly
/// download budget is precious, and no library needs to be trusted with the
/// club's data. Only the text primitives of PDF 1.4 are used.</para>
/// <para>Text is laid out with an average glyph width, not real font metrics: a
/// line may end a little short of the margin, never past the page edge.</para>
/// </remarks>
internal sealed class PdfLifeSheetRenderer : ILifeSheetRenderer
{
    private const double PageWidth = 595;
    private const double PageHeight = 842;
    private const double Margin = 50;
    private const double BottomLimit = 70;
    private const double AverageGlyphWidth = 0.52;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <inheritdoc />
    public byte[] Render(CylinderLifeSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var pages = new PageWriter();
        WriteHeader(pages, sheet);
        WriteCharacteristics(pages, sheet);
        WriteTimeline(pages, sheet);

        return Assemble(pages.Finish(sheet.GeneratedOn));
    }

    private static void WriteHeader(PageWriter pages, CylinderLifeSheet sheet)
    {
        var item = sheet.Item;
        pages.Line("Fiche de vie de bouteille", 10, bold: false, gapBefore: 0, indent: 0);
        pages.Line(item.ClubCode ?? $"Bouteille n° {item.Id}", 22, bold: true, gapBefore: 4, indent: 0);

        var subtitle = string.Join(
            " · ",
            new[]
            {
                item.SerialNumber is null ? null : $"N° de série {item.SerialNumber}",
                string.Join(' ', new[] { item.Brand, item.Model }.Where(part => !string.IsNullOrWhiteSpace(part))),
                item.LocationPath,
            }.Where(part => !string.IsNullOrWhiteSpace(part)));

        if (subtitle.Length > 0)
        {
            pages.Wrapped(subtitle, 10, bold: false, gapBefore: 4, indent: 0);
        }
    }

    private static void WriteCharacteristics(PageWriter pages, CylinderLifeSheet sheet)
    {
        var item = sheet.Item;
        var details = sheet.Details;

        pages.Line("Caractéristiques", 13, bold: true, gapBefore: 18, indent: 0);

        (string Label, string? Value)[] rows =
        [
            ("Statut", item.StatusLabel ?? item.StatusCode),
            ("Validité", DescribeValidity(item, sheet.GeneratedOn)),
            ("Marque", item.Brand),
            ("Modèle", item.Model),
            ("Volume", details?.VolumeLitres is { } volume ? $"{Number(volume)} L" : null),
            ("Pression de service", details?.ServicePressureBar is { } bar ? $"{bar} bar" : null),
            ("Tare", details?.TareKg is { } tare ? $"{Number(tare)} kg" : null),
            ("Type de gaz", details?.UsageLabel),
            ("Matière", details?.MaterialLabel),
            ("N° peint", details?.PaintedNumber),
            ("ID Access", details?.AccessId?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Filetage", details?.Thread),
            ("Double sortie", details?.HasDoubleOutlet is { } dbl ? (dbl ? "Oui" : "Non") : null),
            ("Destination", item.Destination),
            ("Dernier contrôle optique", details?.LastOpticalControlOn is { } optical ? Date(optical) : null),
            ("Dernier contrôle hydraulique", details?.LastHydraulicControlOn is { } hydraulic ? Date(hydraulic) : null),
            ("Remarque", item.Remark),
        ];

        foreach (var (label, value) in rows)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            pages.LabelledLine(label, value);
        }
    }

    private static void WriteTimeline(PageWriter pages, CylinderLifeSheet sheet)
    {
        pages.Line("Chronologie", 13, bold: true, gapBefore: 20, indent: 0);

        if (sheet.Events.Count == 0)
        {
            pages.Line("Aucun événement enregistré.", 10, bold: false, gapBefore: 8, indent: 0);
            return;
        }

        foreach (var entry in sheet.Events)
        {
            var when = entry.OccurredOn is { } date ? Date(date) : "date inconnue";
            pages.Line($"{when} — {entry.Title}", 10.5, bold: true, gapBefore: 10, indent: 0);

            var detail = string.Join(
                " · ",
                new[]
                {
                    entry.Provider,
                    entry.CertificateNumber is null ? null : $"certificat {entry.CertificateNumber}",
                    entry.CostEur is { } cost ? Euro(cost) : null,
                    entry.NextDueOn is { } next ? $"échéance suivante {Date(next)}" : null,
                    entry.Remark,
                }.Where(part => !string.IsNullOrWhiteSpace(part)));

            if (detail.Length > 0)
            {
                pages.Wrapped(detail, 9.5, bold: false, gapBefore: 2, indent: 14);
            }
        }
    }

    private static string DescribeValidity(InventoryItem item, DateOnly today)
    {
        var status = item.DueStatusOn(today) switch
        {
            DueStatus.Overdue => "Hors validité",
            DueStatus.DueSoon => "Échéance proche",
            DueStatus.Valid => "Valide",
            _ => "Sans échéance",
        };

        return item.DueOn is { } due ? $"{status} (échéance {Date(due)})" : status;
    }

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", Invariant);

    private static string Number(decimal value) =>
        value.ToString("0.##", Invariant).Replace('.', ',');

    private static string Euro(decimal value) =>
        value.ToString("0.00", Invariant).Replace('.', ',') + " €";

    /// <summary>Lays the pages out as a PDF 1.4 file.</summary>
    private static byte[] Assemble(List<string> pageContents)
    {
        // Objects: 1 catalog, 2 page tree, 3 regular font, 4 bold font, then a
        // (content stream, page) pair per page. One char = one byte (Latin-1),
        // so a StringBuilder length is a byte offset.
        var file = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        void WriteObject(string body)
        {
            offsets.Add(file.Length);
            file.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }

        var pageObjectNumbers = Enumerable.Range(0, pageContents.Count).Select(index => 6 + (index * 2)).ToArray();

        WriteObject("<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(
            $"<< /Type /Pages /Count {pageContents.Count} /Kids [{string.Join(' ', pageObjectNumbers.Select(number => $"{number} 0 R"))}] >>");
        WriteObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        WriteObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        for (var index = 0; index < pageContents.Count; index++)
        {
            var content = pageContents[index];
            WriteObject($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
            WriteObject(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth.ToString(Invariant)} {PageHeight.ToString(Invariant)}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {5 + (index * 2)} 0 R >>");
        }

        var xrefOffset = file.Length;
        file.Append("xref\n0 ").Append(offsets.Count + 1).Append('\n');
        file.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            file.Append(offset.ToString("D10", Invariant)).Append(" 00000 n \n");
        }

        file.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\n");
        file.Append("startxref\n").Append(xrefOffset).Append("\n%%EOF\n");

        return Encoding.Latin1.GetBytes(file.ToString());
    }

    /// <summary>Accumulates positioned text, opening a new page when the current one is full.</summary>
    private sealed class PageWriter
    {
        private readonly List<StringBuilder> _pages = [];
        private StringBuilder _current = new();
        private double _y;

        public PageWriter() => NewPage();

        public void Line(string text, double size, bool bold, double gapBefore, double indent)
        {
            Advance(size, gapBefore);
            Emit(Margin + indent, text, size, bold);
        }

        public void LabelledLine(string label, string value)
        {
            const double size = 10;
            const double valueColumn = 230;

            Advance(size, 5);
            Emit(Margin, label, size, bold: true);

            var lines = Wrap(value, size, PageWidth - Margin - valueColumn);
            Emit(valueColumn, lines[0], size, bold: false);

            foreach (var extra in lines.Skip(1))
            {
                Advance(size, 2);
                Emit(valueColumn, extra, size, bold: false);
            }
        }

        public void Wrapped(string text, double size, bool bold, double gapBefore, double indent)
        {
            var first = true;
            foreach (var line in Wrap(text, size, PageWidth - (2 * Margin) - indent))
            {
                Advance(size, first ? gapBefore : 2);
                Emit(Margin + indent, line, size, bold);
                first = false;
            }
        }

        public List<string> Finish(DateOnly generatedOn)
        {
            var total = _pages.Count;
            var contents = new List<string>(total);

            for (var index = 0; index < total; index++)
            {
                var page = _pages[index];
                var footer = $"MoaMat — fiche générée le {generatedOn.ToString("dd/MM/yyyy", Invariant)} — page {index + 1}/{total}";
                page.Append(TextOperator(Margin, 40, footer, 8, bold: false));
                contents.Add(page.ToString());
            }

            return contents;
        }

        private void NewPage()
        {
            _current = new StringBuilder();
            _pages.Add(_current);
            _y = PageHeight - Margin;
        }

        private void Advance(double size, double gapBefore)
        {
            var needed = gapBefore + (size * 1.25);
            if (_y - needed < BottomLimit)
            {
                NewPage();
                _y -= size;
                return;
            }

            _y -= needed;
        }

        private void Emit(double x, string text, double size, bool bold) =>
            _current.Append(TextOperator(x, _y, text, size, bold));

        private static string TextOperator(double x, double y, string text, double size, bool bold) =>
            string.Create(
                Invariant,
                $"BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {x:0.##} {y:0.##} Td ({Escape(text)}) Tj ET\n");

        private static List<string> Wrap(string text, double size, double width)
        {
            var maxChars = Math.Max(10, (int)(width / (size * AverageGlyphWidth)));
            var lines = new List<string>();
            var line = new StringBuilder();

            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var piece = word;

                // A single word longer than the line (a long certificate
                // number, say) is cut rather than allowed to run off the page.
                while (piece.Length > maxChars)
                {
                    if (line.Length > 0)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                    }

                    lines.Add(piece[..maxChars]);
                    piece = piece[maxChars..];
                }

                if (line.Length > 0 && line.Length + 1 + piece.Length > maxChars)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }

                if (line.Length > 0)
                {
                    line.Append(' ');
                }

                line.Append(piece);
            }

            if (line.Length > 0 || lines.Count == 0)
            {
                lines.Add(line.ToString());
            }

            return lines;
        }

        /// <summary>
        /// Maps text to WinAnsi bytes (one char per byte) and escapes the PDF
        /// string delimiters. Latin-1 characters are identical in WinAnsi; the
        /// few extra ones the club's texts use are mapped explicitly, anything
        /// else becomes <c>?</c> rather than a corrupted glyph.
        /// </summary>
        private static string Escape(string text)
        {
            var builder = new StringBuilder(text.Length);

            foreach (var character in text)
            {
                var mapped = character switch
                {
                    '€' => '\u0080',
                    '…' => '\u0085',
                    '‘' => '\u0091',
                    '’' => '\u0092',
                    '“' => '\u0093',
                    '”' => '\u0094',
                    '•' => '\u0095',
                    '–' => '\u0096',
                    '—' => '\u0097',
                    'œ' => '\u009c',
                    'Œ' => '\u008c',
                    '₂' => '2',
                    '\r' or '\n' or '\t' => ' ',
                    _ when character is >= ' ' and <= '~' or >= ' ' and <= 'ÿ' => character,
                    _ => '?',
                };

                if (mapped is '\\' or '(' or ')')
                {
                    builder.Append('\\');
                }

                builder.Append(mapped);
            }

            return builder.ToString();
        }
    }
}
