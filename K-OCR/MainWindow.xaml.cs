using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using K_OCR.Models;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using Tesseract;
//using OpenCvSharp;
//using System;
//using Point = OpenCvSharp.Point;
//using Size = OpenCvSharp.Size;

namespace K_OCR
{
    public partial class MainWindow : System.Windows.Window
    {
        private readonly List<OCRFile> filesToProcess = new List<OCRFile>();
        private int currentIndex = -1;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void OnOpenClick(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open Image",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All Files|*.*",
                Multiselect = true
            };

            if (dlg.ShowDialog(this) == true && dlg.FileNames?.Length > 0)
            {
                filesToProcess.Clear();
                foreach (string fileName in dlg.FileNames)
                {
                    filesToProcess.Add(new OCRFile { filePath = fileName });
                }

                currentIndex = filesToProcess.Count > 0 ? 0 : -1;

                await RunOcrAsync(filesToProcess);
                OnProcessingCompleted(filesToProcess);

                // Show first file without rebuilding document
                ShowFileAt(currentIndex);
            }
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, "K-OCR\nVersion 1.0\nPowered by Tesseract OCR", "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task RunOcrAsync(IEnumerable<OCRFile> items)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };

            await Parallel.ForEachAsync(items, options, async (ocrFile, ct) =>
            {
                try
                {
                    var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);

                    var img = Pix.LoadFromFile(ocrFile.filePath);
                    using (var page = engine.Process(img))
                    {
                        var text = page.GetText();
                        Debug.WriteLine("Mean confidence: {0}", page.GetMeanConfidence());
                        ocrFile.ocrText = text;

                        // Compute layout artifacts
                        var lineBlocks = GetLineBlocks(page);
                        var tableBlocks = DetectTables(lineBlocks, page);

                        await Dispatcher.InvokeAsync(() =>
                        {
                            // Build FlowDocument including tables
                            var flowDocument = BuildFlowDocument(lineBlocks, tableBlocks);
                            ocrFile.Document = flowDocument;

                            if (currentIndex >= 0 && currentIndex < filesToProcess.Count)
                            {
                                var current = filesToProcess[currentIndex];
                                if (ReferenceEquals(current, ocrFile))
                                {
                                    OCRdTextPanel.Document = ocrFile.Document;
                                }
                            }
                        });

                        Debug.WriteLine($"OCR'd {System.IO.Path.GetFileName(ocrFile.filePath)}");
                    }
                }
                catch (Exception ex)
                {
                    await Dispatcher.BeginInvoke(() =>
                        MessageBox.Show(this, $"OCR failed for {System.IO.Path.GetFileName(ocrFile.filePath)}: {ex.Message}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });
        }

        private static List<OcrBlock> GetLineBlocks(Page page)
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

        public List<OcrBlock> DetectTables(List<OcrBlock> textLineBlocks, Page page)
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

        public FlowDocument BuildFlowDocument(List<OcrBlock> lineBlocks, List<OcrBlock> tableBlocks)
        {
            var doc = new FlowDocument();

            var tables = tableBlocks ?? new List<OcrBlock>();
            var lines = lineBlocks ?? new List<OcrBlock>();

            // Exclude text lines that overlap table regions to avoid duplicates
            static bool Overlaps(Tesseract.Rect a, Tesseract.Rect b, int tol = 4)
            {
                var ax1 = a.X1 - tol; var ay1 = a.Y1 - tol; var ax2 = a.X2 + tol; var ay2 = a.Y2 + tol;
                var bx1 = b.X1 - tol; var by1 = b.Y1 - tol; var bx2 = b.X2 + tol; var by2 = b.Y2 + tol;
                return ax1 < bx2 && ax2 > bx1 && ay1 < by2 && ay2 > by1;
            }

            var filteredLines = lines.Where(l => !tables.Any(t => Overlaps(l.BoundingBox, t.BoundingBox))).ToList();

            var all = new List<OcrBlock>();
            all.AddRange(filteredLines);
            all.AddRange(tables);

            foreach (var block in all.OrderBy(b => b.BoundingBox.Y1).ThenBy(b => b.BoundingBox.X1))
            {
                if (block.Type == OcrBlockType.Text)
                {
                    doc.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(block.Text ?? string.Empty)));
                }
                else if (block.Type == OcrBlockType.Table && block.RowData != null && block.RowData.Length > 0)
                {
                    var table = new System.Windows.Documents.Table();

                    for (int c = 0; c < block.RowData.Length; c++)
                        table.Columns.Add(new TableColumn());

                    var trg = new TableRowGroup();
                    var row = new System.Windows.Documents.TableRow();

                    foreach (var cellText in block.RowData)
                        row.Cells.Add(new System.Windows.Documents.TableCell(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(cellText))));

                    trg.Rows.Add(row);
                    table.RowGroups.Add(trg);

                    doc.Blocks.Add(table);
                }
            }

            return doc;
        }

        private void OnProcessingCompleted(IReadOnlyCollection<OCRFile> completed)
        {
            Dispatcher.Invoke(() =>
            {
                var first = completed.FirstOrDefault();
                if (first != null && System.IO.File.Exists(first.filePath))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(first.filePath, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();

                    ImagePanel.Source = bmp;
                    if (first.Document != null)
                    {
                        OCRdTextPanel.Document = first.Document;
                        FileCaption.Text = first.filePath;
                    }
                }

                // Aggregated text remains optional; do not rebuild per navigation
                if (OCRdTextPanel != null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var file in completed)
                    {
                        sb.AppendLine($"{System.IO.Path.GetFileName(file.filePath)}:");
                        sb.AppendLine(file.ocrText);
                        sb.AppendLine();
                    }
                    // OCRdTextPanel.Text = sb.ToString();
                }
            });
        }

        // Navigation shows prebuilt document; no BuildFlowDocument calls here
        private void ShowFileAt(int index)
        {
            if (index < 0 || index >= filesToProcess.Count)
                return;

            var file = filesToProcess[index];
            if (System.IO.File.Exists(file.filePath))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(file.filePath, UriKind.Absolute);  // Fixed: Use 'file.filePath' instead of 'first.filePath'
                bmp.EndInit();
                bmp.Freeze();

                ImagePanel.Source = bmp;
            }

            OCRdTextPanel.Document = file.Document ?? new FlowDocument(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(file.ocrText ?? string.Empty)));
        }


        private void OnExportDocx(object sender, RoutedEventArgs e)
        {
            var doc = OCRdTextPanel?.Document;
            if (doc == null)
            {
                MessageBox.Show(this, "No document to export.", "Export DOCX", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ExportAsDocx(doc);
        }

        private void ExportAsDocx(FlowDocument flowDocument)
        {
            var doc = flowDocument;
            if (doc == null)
            {
                MessageBox.Show(this, "No document to export.", "Export DOCX", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save as Word (DOCX)",
                Filter = "Word Document|*.docx",
                AddExtension = true,
                DefaultExt = ".docx"
            };
            if (sfd.ShowDialog(this) == true)
            {
                try
                {
                    using var wordDoc = WordprocessingDocument.Create(sfd.FileName, WordprocessingDocumentType.Document);
                    var mainPart = wordDoc.AddMainDocumentPart();
                    mainPart.Document = new Document(new Body());
                    var body = mainPart.Document.Body;

                    foreach (var block in doc.Blocks)
                    {
                        if (block is System.Windows.Documents.Paragraph wpfParagraph)
                        {
                            var p = new DocumentFormat.OpenXml.Wordprocessing.Paragraph();
                            foreach (Inline inline in wpfParagraph.Inlines)
                            {
                                if (inline is System.Windows.Documents.Run wpfRun)
                                {
                                    var text = new DocumentFormat.OpenXml.Wordprocessing.Text(wpfRun.Text ?? string.Empty)
                                    {
                                        Space = SpaceProcessingModeValues.Preserve
                                    };
                                    var oxRun = new DocumentFormat.OpenXml.Wordprocessing.Run(text);
                                    p.AppendChild(oxRun);
                                }
                                else
                                {
                                    var raw = new TextRange(inline.ContentStart, inline.ContentEnd).Text;
                                    var text = new DocumentFormat.OpenXml.Wordprocessing.Text(raw ?? string.Empty)
                                    {
                                        Space = SpaceProcessingModeValues.Preserve
                                    };
                                    var oxRun = new DocumentFormat.OpenXml.Wordprocessing.Run(text);
                                    p.AppendChild(oxRun);
                                }
                            }
                            body?.AppendChild(p);
                        }
                        else if (block is System.Windows.Documents.Table wpfTable)
                        {
                            var oxTable = new DocumentFormat.OpenXml.Wordprocessing.Table();

                            // Basic table properties (optional)
                            var tblProps = new TableProperties(
                                new TableBorders(
                                    new TopBorder { Val = BorderValues.Single, Size = 4 },
                                    new LeftBorder { Val = BorderValues.Single, Size = 4 },
                                    new BottomBorder { Val = BorderValues.Single, Size = 4 },
                                    new RightBorder { Val = BorderValues.Single, Size = 4 },
                                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }
                                )
                            );
                            oxTable.AppendChild(tblProps);

                            foreach (var trg in wpfTable.RowGroups)
                            {
                                foreach (var row in trg.Rows)
                                {
                                    var oxRow = new DocumentFormat.OpenXml.Wordprocessing.TableRow();
                                    foreach (var cell in row.Cells)
                                    {
                                        var oxCell = new DocumentFormat.OpenXml.Wordprocessing.TableCell();

                                        foreach (var cb in cell.Blocks)
                                        {
                                            if (cb is System.Windows.Documents.Paragraph cellParagraph)
                                            {
                                                var p = new DocumentFormat.OpenXml.Wordprocessing.Paragraph();
                                                foreach (Inline inline in cellParagraph.Inlines)
                                                {
                                                    if (inline is System.Windows.Documents.Run wpfRun)
                                                    {
                                                        var text = new DocumentFormat.OpenXml.Wordprocessing.Text(wpfRun.Text ?? string.Empty)
                                                        {
                                                            Space = SpaceProcessingModeValues.Preserve
                                                        };
                                                        var oxRun = new DocumentFormat.OpenXml.Wordprocessing.Run(text);
                                                        p.AppendChild(oxRun);
                                                    }
                                                    else
                                                    {
                                                        var raw = new TextRange(inline.ContentStart, inline.ContentEnd).Text;
                                                        var text = new DocumentFormat.OpenXml.Wordprocessing.Text(raw ?? string.Empty)
                                                        {
                                                            Space = SpaceProcessingModeValues.Preserve
                                                        };
                                                        var oxRun = new DocumentFormat.OpenXml.Wordprocessing.Run(text);
                                                        p.AppendChild(oxRun);
                                                    }
                                                }
                                                oxCell.AppendChild(p);
                                            }
                                        }

                                        oxRow.AppendChild(oxCell);
                                    }
                                    oxTable.AppendChild(oxRow);
                                }
                            }

                            body?.AppendChild(oxTable);
                        }
                    }

                    mainPart.Document.Save();
                    MessageBox.Show(this, "DOCX saved.", "Export DOCX", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to save DOCX: {ex.Message}", "Export DOCX", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

    }
}