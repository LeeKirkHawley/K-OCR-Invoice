using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using K_OCR.Models;

namespace K_OCR.Services
{
    /// <summary>
    /// Shared document export helpers. Located in K-OCRLib so the same logic can be
    /// reused by the desktop app and by server/web hosts.
    /// </summary>
    public class DocumentExportService : IDocumentExportService
    {
        public DocumentExportService()
        {
        }

        public async Task ExportToDocxAsync(OCRFile ocrFile, string outputPath)
        {
            if (ocrFile == null)
                throw new ArgumentNullException(nameof(ocrFile));

            // Create DOCX using Open XML SDK
            using var wordDoc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body;

            if (body == null)
                return;

            // Combine and sort all blocks by position
            var allBlocks = new List<OcrBlock>();
            if (ocrFile.LineBlocks != null)
                allBlocks.AddRange(ocrFile.LineBlocks);
            if (ocrFile.TableBlocks != null)
                allBlocks.AddRange(ocrFile.TableBlocks ?? new List<OcrBlock>());

            var tables = ocrFile.TableBlocks ?? new List<OcrBlock>();
            var lines = ocrFile.LineBlocks ?? new List<OcrBlock>();
            var filteredLines = lines.Where(l => !tables.Any(t => Overlaps(l.BoundingBox, t.BoundingBox))).ToList();

            var finalBlocks = new List<OcrBlock>();
            finalBlocks.AddRange(filteredLines);
            finalBlocks.AddRange(tables);

            // Sort by Y position first, then X position
            foreach (var block in finalBlocks.OrderBy(b => b.BoundingBox.Y1).ThenBy(b => b.BoundingBox.X1))
            {
                if (block.Type == OcrBlockType.Text && !string.IsNullOrWhiteSpace(block.Text))
                {
                    // Add text paragraph
                    var paragraph = new Paragraph(
                        new Run(
                            new Text(block.Text)
                            {
                                Space = SpaceProcessingModeValues.Preserve
                            }
                        )
                    );
                    body.AppendChild(paragraph);
                }
                else if (block.Type == OcrBlockType.Table && block.RowData != null && block.RowData.Length > 0)
                {
                    var table = new Table();

                    // Add a simple border to the table
                    var tblProps = new TableProperties(
                        new TableBorders(
                            new TopBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                            new LeftBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                            new BottomBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                            new RightBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                            new InsideHorizontalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                            new InsideVerticalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 }
                        )
                    );
                    table.AppendChild(tblProps);

                    var row = new TableRow();
                    foreach (var cellText in block.RowData)
                    {
                        var cell = new TableCell(
                            new Paragraph(
                                new Run(
                                    new Text(cellText ?? string.Empty)
                                    {
                                        Space = SpaceProcessingModeValues.Preserve
                                    }
                                )
                            )
                        );
                        row.AppendChild(cell);
                    }
                    table.AppendChild(row);
                    body.AppendChild(table);
                }
            }

            mainPart.Document.Save();

            await Task.CompletedTask;
        }

        public Task ExportToPdfAsync(OCRFile ocrFile, string outputPath)
        {
            // PDF export not implemented in shared service yet
            return Task.CompletedTask;
        }

        public Task ExportToCsvAsync(List<OCRFile> files, string outputPath)
        {
            // CSV export not implemented in shared service yet
            return Task.CompletedTask;
        }

        private static bool Overlaps(Tesseract.Rect a, Tesseract.Rect b, int tol = 4)
        {
            var ax1 = a.X1 - tol;
            var ay1 = a.Y1 - tol;
            var ax2 = a.X2 + tol;
            var ay2 = a.Y2 + tol;
            var bx1 = b.X1 - tol;
            var by1 = b.Y1 - tol;
            var bx2 = b.X2 + tol;
            var by2 = b.Y2 + tol;
            return ax1 < bx2 && ax2 > bx1 && ay1 < by2 && ay2 > by1;
        }
    }
}
