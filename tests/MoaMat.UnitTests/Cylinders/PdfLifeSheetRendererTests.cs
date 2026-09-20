using System.Text;
using MoaMat.Domain.Campaigns;
using MoaMat.Domain.Cylinders;
using MoaMat.Domain.Inventory;
using MoaMat.Infrastructure.Documents;

namespace MoaMat.UnitTests.Cylinders;

/// <summary>
/// The life sheet is a hand-written PDF. These tests pin the structure a PDF
/// reader relies on (header, cross-reference offsets, trailer) and that the
/// timeline content reaches the page.
/// </summary>
public sealed class PdfLifeSheetRendererTests
{
    private static readonly DateOnly Today = new(2026, 9, 20);

    [Fact]
    public void The_output_is_a_well_formed_pdf_whose_xref_offsets_point_at_the_objects()
    {
        var text = Encoding.Latin1.GetString(Render(Sheet(events: [])));

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);

        var xrefStart = int.Parse(
            text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + "startxref\n".Length)..]
                .Split('\n')[0],
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("xref", text[xrefStart..], StringComparison.Ordinal);

        var entries = text[xrefStart..].Split('\n').Where(line => line.EndsWith(" 00000 n ", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(entries);

        for (var index = 0; index < entries.Length; index++)
        {
            var offset = int.Parse(entries[index][..10], System.Globalization.CultureInfo.InvariantCulture);
            Assert.StartsWith($"{index + 1} 0 obj", text[offset..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_club_code_and_the_timeline_are_written_with_accents_and_the_euro_sign_in_winansi()
    {
        var events = new[]
        {
            new CylinderEvent
            {
                Id = 1,
                Type = CylinderEventType.OpticalControl,
                Outcome = CampaignLineOutcome.Passed,
                OccurredOn = new DateOnly(2025, 9, 9),
                Provider = "Apragaz",
                CertificateNumber = "Lb.134008",
                CostEur = 21.32m,
                Remark = "Échéance à surveiller (filet)",
            },
        };

        var text = Encoding.Latin1.GetString(Render(Sheet(events)));

        Assert.Contains("(BOUT-F-52)", text, StringComparison.Ordinal);
        Assert.Contains("09/09/2025 \u0097 Contrôle optique \u0097 conforme", text, StringComparison.Ordinal);
        Assert.Contains("21,32 \u0080", text, StringComparison.Ordinal);
        Assert.Contains("Échéance à surveiller \\(filet\\)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_timeline_spills_onto_further_pages_each_numbered()
    {
        var events = Enumerable.Range(1, 80).Select(index => new CylinderEvent
        {
            Id = index,
            Type = CylinderEventType.HydraulicControl,
            OccurredOn = new DateOnly(2000, 1, 1).AddYears(index % 25),
            Remark = "Remarque",
        }).ToArray();

        var text = Encoding.Latin1.GetString(Render(Sheet(events)));

        var pageCount = System.Text.RegularExpressions.Regex.Count(text, "/Type /Page /Parent");
        Assert.True(pageCount > 1);
        Assert.Contains($"page 1/{pageCount}", text, StringComparison.Ordinal);
        Assert.Contains($"page {pageCount}/{pageCount}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bottle_without_history_says_so()
    {
        var text = Encoding.Latin1.GetString(Render(Sheet(events: [])));

        Assert.Contains("Aucun événement enregistré.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Characters_outside_winansi_become_a_question_mark_not_a_corrupted_glyph()
    {
        var events = new[]
        {
            new CylinderEvent { Id = 1, Type = CylinderEventType.Incident, OccurredOn = Today, Remark = "robinet 中" },
        };

        var text = Encoding.Latin1.GetString(Render(Sheet(events)));

        Assert.Contains("robinet ?", text, StringComparison.Ordinal);
    }

    [Fact]
    public void C1_control_characters_are_never_written_through()
    {
        var events = new[]
        {
            new CylinderEvent { Id = 1, Type = CylinderEventType.Incident, OccurredOn = Today, Remark = "a\u0085b\u009fc" },
        };

        var text = Encoding.Latin1.GetString(Render(Sheet(events)));

        Assert.Contains("a?b?c", text, StringComparison.Ordinal);
    }

    private static byte[] Render(CylinderLifeSheet sheet) => new PdfLifeSheetRenderer().Render(sheet);

    private static CylinderLifeSheet Sheet(IReadOnlyList<CylinderEvent> events) => new(
        new InventoryItem
        {
            Id = 52,
            ClubCode = "BOUT-F-52",
            FamilyCode = "bouteille",
            StatusCode = "en_stock",
            StatusLabel = "En stock",
            SerialNumber = "X26628",
            Brand = "FABER",
            DueOn = new DateOnly(2026, 1, 1),
        },
        new CylinderDetails { ItemId = 52, VolumeLitres = 12, ServicePressureBar = 232, UsageCode = "plongee" },
        events,
        Today);
}
