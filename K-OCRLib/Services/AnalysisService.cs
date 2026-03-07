using Azure.AI.DocumentIntelligence;
using K_OCR.Models;
using Tesseract;

namespace K_OCR.Services
{
    public class AnalysisService : IAnalysisService
    {
        public void AnalyzePage(Tesseract.Page page, out List<OcrBlock> lineBlocks, out List<OcrBlock> tableBlocks)
        {
            lineBlocks = GetLineBlocks(page);
            tableBlocks = DetectTables(lineBlocks, page);
        }

        private static List<OcrBlock> GetLineBlocks(Tesseract.Page page)
        {
            var blocks = new List<OcrBlock>();

            using (var iter = page.GetIterator())
            {
                iter.Begin();
                do
                {
                    if (iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out var rect))
                    {
                        string text = iter.GetText(PageIteratorLevel.TextLine) ?? string.Empty;
                        text = text.Trim();
                        if (String.IsNullOrWhiteSpace(text))
                            continue;

                        float conf = iter.GetConfidence(PageIteratorLevel.TextLine);

                        OcrBlock block = new OcrBlock
                        {
                            Type = OcrBlockType.Text,
                            Text = text.Trim(),
                            Confidence = conf,
                            BoundingBox = rect
                        };

                        if (!String.IsNullOrWhiteSpace(block.Text))
                            blocks.Add(block);
                    }
                } while (iter.Next(PageIteratorLevel.TextLine));
            }

            return blocks;
        }

        public List<OcrBlock> DetectTables(List<OcrBlock> textLineBlocks, Tesseract.Page page)
        {
            var tableRows = new List<OcrBlock>();
            if (textLineBlocks == null || textLineBlocks.Count == 0)
                return tableRows;

            const int yTolerance = 8;
            const int colGapMin = 20;

            var groupedRows = textLineBlocks
                .OrderBy(b => b.BoundingBox.Y1)
                .GroupBy(b => b.BoundingBox.Y1 / yTolerance);

            foreach (var rowGroup in groupedRows)
            {
                var lines = rowGroup.OrderBy(b => b.BoundingBox.X1).ToList();
                var rowCells = new List<string>();

                foreach (var line in lines)
                {
                    var words = new List<(string text, int x1, int x2)>();

                    using (var iter = page.GetIterator())
                    {
                        iter.Begin();
                        do
                        {
                            if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var wRect))
                            {
                                bool overlapsY = Math.Abs(wRect.Y1 - line.BoundingBox.Y1) < yTolerance * 2;
                                if (overlapsY)
                                {
                                    var w = iter.GetText(PageIteratorLevel.Word);
                                    if (!string.IsNullOrWhiteSpace(w))
                                        words.Add((w.Trim(), wRect.X1, wRect.X2));
                                }
                            }
                        } while (iter.Next(PageIteratorLevel.Word));
                    }

                    if (words.Count == 0)
                        continue;

                    words.Sort((a, b) => a.x1.CompareTo(b.x1));
                    var clusters = new List<List<(string text, int x1, int x2)>>();
                    var current = new List<(string text, int x1, int x2)> { words[0] };

                    for (int i = 1; i < words.Count; i++)
                    {
                        var prev = words[i - 1];
                        var cur = words[i];

                        int gap = cur.x1 - prev.x2;
                        if (gap >= colGapMin)
                        {
                            clusters.Add(current);
                            current = new List<(string text, int x1, int x2)>();
                        }
                        current.Add(cur);
                    }
                    clusters.Add(current);

                    foreach (var cl in clusters)
                    {
                        var cellText = string.Join(" ", cl.Select(w => w.text));
                        if (!string.IsNullOrWhiteSpace(cellText))
                            rowCells.Add(cellText);
                    }
                }

                if (rowCells.Count >= 2)
                {
                    tableRows.Add(new OcrBlock
                    {
                        Type = OcrBlockType.Table,
                        RowData = rowCells.ToArray(),
                        BoundingBox = lines.First().BoundingBox
                    });
                }
            }

            return tableRows;
        }

        private static void ExtractFieldWithPolygon(AnalyzedDocument document, string fieldKey)
        {
            if (document.Fields?.TryGetValue(fieldKey, out Azure.AI.DocumentIntelligence.DocumentField field) == true)
            {
                string value = field.Content
                    ?? field.ValueString
                    ?? (field.ValueDate?.ToString() ?? null)
                    ?? (field.ValueCurrency != null ? field.ValueCurrency.Amount.ToString("C") : null)
                    ?? "N/A";
                Console.WriteLine($"{fieldKey}: {value} (Confidence: {field.Confidence:F4})");

                if (field.BoundingRegions != null && field.BoundingRegions.Count > 0)
                {
                    foreach (BoundingRegion region in field.BoundingRegions)
                    {
                        string polygonStr = string.Join(" → ", region.Polygon);
                        Console.WriteLine($"  Page {region.PageNumber} Polygon: {polygonStr}");
                    }
                }
                else
                {
                    Console.WriteLine("  No bounding region available.");
                }
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine($"{fieldKey}: Not found");
                Console.WriteLine();
            }
        }

    }
}
