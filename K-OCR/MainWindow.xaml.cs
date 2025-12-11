using K_OCR.Models;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using Tesseract;

namespace K_OCR
{
    public partial class MainWindow : Window
    {
        private readonly List<OCRFile> filesToProcess = new List<OCRFile>();

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

                await RunOcrAsync(filesToProcess);
                OnProcessingCompleted(filesToProcess);
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
                        Console.WriteLine("Mean confidence: {0}", page.GetMeanConfidence());
                        Console.WriteLine("Text (GetText): \r\n{0}", text);
                        Console.WriteLine("Text (iterator):");

                        ocrFile.ocrText = text;

                        if (true)
                        {
                            List<OcrBlock> blocks = new List<OcrBlock>();
                            blocks = GetBlocks(page);

                            await Dispatcher.InvokeAsync(() =>
                            {
                                List<OcrBlock> lineBlocks = GetLineBlocks(page);
                                FlowDocument flowDocument = BuildFlowDocument(blocks);
                                List<OcrBlock> tableBlocks = DetectTables(lineBlocks, page);
                                OCRdTextPanel.Document = flowDocument;
                            });
                        }

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


        private static List<OcrBlock> GetBlocks(Page page)
        {
            List<OcrBlock> blocks = new List<OcrBlock>();

            using (var iter = page.GetIterator())
            {
                iter.Begin();

                do
                {
                    if (iter.IsAtBeginningOf(PageIteratorLevel.Block))
                    {
                        string text = iter.GetText(PageIteratorLevel.Block);
                        float conf = iter.GetConfidence(PageIteratorLevel.Block);

                        if (iter.TryGetBoundingBox(PageIteratorLevel.Block, out var rect))
                        {
                            blocks.Add(new OcrBlock
                            {
                                Type = OcrBlockType.Text, // default, you can reclassify later
                                Text = text,
                                Confidence = conf,
                                BoundingBox = rect
                            });
                        }
                    }
                } while (iter.Next(PageIteratorLevel.Block));
            }

            return blocks;
        }

        private static List<OcrBlock> GetLineBlocks(Page page)
        {
            var blocks = new List<OcrBlock>();

            using (var iter = page.GetIterator())
            {
                iter.Begin();
                // Walk lines and collect text + bounding box
                do
                {
                    if (iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out var rect))
                    {
                        var text = iter.GetText(PageIteratorLevel.TextLine) ?? string.Empty;
                        var conf = iter.GetConfidence(PageIteratorLevel.TextLine);

                        blocks.Add(new OcrBlock
                        {
                            Type = OcrBlockType.Text,
                            Text = text.Trim(),
                            Confidence = conf,
                            BoundingBox = rect
                        });
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

            // Tolerances tuned for typical scanned docs; adjust as needed
            const int yTolerance = 8;     // lines within ~8 px considered same row band
            const int colGapMin = 20;     // min gap between words to consider a column split

            // Group lines into row bands by Y1 (top) with tolerance
            var groupedRows = textLineBlocks
                .OrderBy(b => b.BoundingBox.Y1)
                .GroupBy(b => b.BoundingBox.Y1 / yTolerance);

            foreach (var rowGroup in groupedRows)
            {
                var lines = rowGroup.OrderBy(b => b.BoundingBox.X1).ToList();

                // Split each line into "cells" by large gaps in words
                var rowCells = new List<string>();

                foreach (var line in lines)
                {
                    // Tokenize line by words with positions
                    var words = new List<(string text, int x1, int x2)>();

                    // Re-iterate at word level within the page to get positions for this line’s span.
                    // If you cannot filter to the exact line, approximate by taking words whose Y overlaps the line’s Y-band.
                    using (var iter = page.GetIterator())
                    {
                        iter.Begin();
                        do
                        {
                            if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var wRect))
                            {
                                // Overlap check: word belongs to this row band
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

                    // Sort words by X, then split into clusters where gaps exceed threshold
                    words.Sort((a, b) => a.x1.CompareTo(b.x1));
                    var clusters = new List<List<(string text, int x1, int x2)>>();
                    var current = new List<(string text, int x1, int x2)>();
                    current.Add(words[0]);

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

                    // Each cluster becomes a "cell" for this row
                    foreach (var cl in clusters)
                    {
                        var cellText = string.Join(" ", cl.Select(w => w.text));
                        if (!string.IsNullOrWhiteSpace(cellText))
                            rowCells.Add(cellText);
                    }
                }

                // If the row has 2+ cells, consider it a table row
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

        public FlowDocument BuildFlowDocument(List<OcrBlock> blocks)
        {
            FlowDocument doc = new FlowDocument();

            foreach (var block in blocks.OrderBy(b => b.BoundingBox.Y1))
            {
                if (block.Type == OcrBlockType.Text)
                {
                    doc.Blocks.Add(new Paragraph(new Run(block.Text)));
                }
                else if (block.Type == OcrBlockType.Table && block.RowData != null)
                {
                    Table table = new Table();
                    for (int c = 0; c < block.RowData.Length; c++)
                        table.Columns.Add(new TableColumn());

                    TableRowGroup trg = new TableRowGroup();
                    TableRow row = new TableRow();
                    foreach (var cellText in block.RowData)
                        row.Cells.Add(new TableCell(new Paragraph(new Run(cellText))));
                    trg.Rows.Add(row);
                    table.RowGroups.Add(trg);

                    doc.Blocks.Add(table);
                }
            }

            return doc;
        }


        // Your completion hook: update UI, raise an event, or call into another service
        private void OnProcessingCompleted(IReadOnlyCollection<OCRFile> completed)
        {
            // Or simple UI notification
            //MessageBox.Show(this, $"Completed OCR for {completed.Count} file(s).", "Done",
            //    MessageBoxButton.OK, MessageBoxImage.Information);

            Dispatcher.Invoke(() =>
            {
                // Show the first image on the left panel
                var first = completed.FirstOrDefault();
                if (first != null && System.IO.File.Exists(first.filePath))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(first.filePath, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze(); // for cross-thread safety

                    ImagePanel.Source = bmp;
                }

                // Update OCR text on the right panel (keep existing behavior)
                if (OCRdTextPanel != null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var file in completed)
                    {
                        sb.AppendLine($"{System.IO.Path.GetFileName(file.filePath)}:");
                        sb.AppendLine(file.ocrText);
                        sb.AppendLine();
                    }
                    //OCRdTextPanel.Text = sb.ToString();
                }
            });
        }
    }
}