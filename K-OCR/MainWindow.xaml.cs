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

                        // Compute all layout artifacts during OCR step
                        var blocks = GetBlocks(page);
                        var lineBlocks = GetLineBlocks(page);
                        var tableBlocks = DetectTables(lineBlocks, page);

                        await Dispatcher.InvokeAsync(() =>
                        {
                            // Build and store the FlowDocument once
                            var flowDocument = BuildFlowDocument(blocks);
                            ocrFile.Document = flowDocument;

                            // Optionally show the document of the currently selected item if it's the one just processed
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

        private static List<OcrBlock> GetBlocks(Page page)
        {
            var blocks = new List<OcrBlock>();

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
                                Type = OcrBlockType.Text,
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
                bmp.UriSource = new Uri(file.filePath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                ImagePanel.Source = bmp;
            }

            OCRdTextPanel.Document = file.Document ?? new FlowDocument(new Paragraph(new Run(file.ocrText ?? string.Empty)));
        }
    }
}