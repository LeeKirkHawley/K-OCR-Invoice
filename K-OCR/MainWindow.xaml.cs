using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using K_OCR.Models;
using K_OCR.PipeLineSteps;
using K_OCR.Services;
using Microsoft.Extensions.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using static K_OCR.PipeLineSteps.PipeLineTextConfig;


namespace K_OCR
{
    public partial class MainWindow : System.Windows.Window
    {
        private readonly List<OCRFile> filesToProcess = new List<OCRFile>();
        private int currentIndex = -1;
        private readonly IConfiguration _config;
        private readonly IFileService _fileService;
        private readonly IInvoiceService _invoiceService;
        private readonly IAzureService _azureService;
        private readonly IAnalysisService _analysisService;
        private readonly IOCRService _ocrService;

        public MainWindow(IFileService fileService, IInvoiceService invoiceService, IAzureService azureService, 
            IAnalysisService analysisService, IOCRService ocrService)
        {
            InitializeComponent();
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            _config = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
            _fileService = fileService;
            _invoiceService = invoiceService;
            _azureService = azureService;
            _analysisService = analysisService;
            _ocrService = ocrService;
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
                    PipeLineSteps.PipelineTextConfig config = PipelineConfigLoader.Load("PipelineSteps/DefaultPipeline.json");
                    var executor = new PipelineExecutor();
                    var context = new PipelineContext
                    {
                        InputPath = fileName
                    };

                    var result = await executor.RunAsync(config, context);
                    //await _invoiceService.RunAzureInvoiceParse(fileName);
                }
            }
        }

        //private async void OnParseInvoiceClick(object sender, RoutedEventArgs e)
        //{
        //    string filePath = "C:/OCR/Invoices/invoice-template-us-mono-black-750px.png";

        //    await _invoiceService.RunAzureInvoiceParse(filePath);
        //}

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, "K-OCR\nVersion 1.0\nPowered by Tesseract OCR", "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }


        // New method to draw OCR overlay with absolute positioning
        private void DrawOCROverlay(OCRFile ocrFile)
        {
            // Clear previous overlays
            OCRdTextPanel.Children.Clear();

            if (ocrFile.LineBlocks == null || ocrFile.LineBlocks.Count == 0)
                return;

            // Load and display the background image
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(ocrFile.filePath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            //OCRImagePanel.Source = bmp;

            // Set Canvas size to match image dimensions
            OCRdTextPanel.Width = bmp.PixelWidth;
            OCRdTextPanel.Height = bmp.PixelHeight;

            var tables = ocrFile.TableBlocks ?? new List<OcrBlock>();
            var lines = ocrFile.LineBlocks ?? new List<OcrBlock>();

            // Filter out text lines that overlap with tables
            static bool Overlaps(Tesseract.Rect a, Tesseract.Rect b, int tol = 4)
            {
                var ax1 = a.X1 - tol; var ay1 = a.Y1 - tol; var ax2 = a.X2 + tol; var ay2 = a.Y2 + tol;
                var bx1 = b.X1 - tol; var by1 = b.Y1 - tol; var bx2 = b.X2 + tol; var by2 = b.Y2 + tol;
                return ax1 < bx2 && ax2 > bx1 && ay1 < by2 && ay2 > by1;
            }

            var filteredLines = lines.Where(l => !tables.Any(t => Overlaps(l.BoundingBox, t.BoundingBox))).ToList();

            // Draw text blocks with absolute positioning
            //foreach (var block in filteredLines)
            foreach (var block in lines)
            {
                if (block.Type == OcrBlockType.Text && !string.IsNullOrWhiteSpace(block.Text))
                {
                    var textBlock = new TextBlock
                    {
                        Text = block.Text,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 255, 0, 0)), // Semi-transparent red
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 255, 255, 255)), // Semi-transparent white
                        Padding = new Thickness(2)
                    };

                    // Position using bounding box
                    Canvas.SetLeft(textBlock, block.BoundingBox.X1);
                    Canvas.SetTop(textBlock, block.BoundingBox.Y1);

                    OCRdTextPanel.Children.Add(textBlock);
                }
            }

            // Draw table regions with rectangles
            foreach (var table in tables)
            {
                var rect = new Rectangle
                {
                    Width = table.BoundingBox.Width,
                    Height = table.BoundingBox.Height,
                    Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 255, 0)), // Semi-transparent yellow
                    Stroke = Brushes.Black,
                    StrokeThickness = 2
                };

                Canvas.SetLeft(rect, table.BoundingBox.X1);
                Canvas.SetTop(rect, table.BoundingBox.Y1);
                OCRdTextPanel.Children.Add(rect);
            }
        }

        // Keep BuildFlowDocument for DOCX export
        public FlowDocument BuildFlowDocument(List<OcrBlock> lineBlocks, List<OcrBlock> tableBlocks)
        {
            var doc = new FlowDocument();

            var tables = tableBlocks ?? new List<OcrBlock>();
            var lines = lineBlocks ?? new List<OcrBlock>();

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
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(first.filePath, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();

                    ImagePanel.Source = bmp;
                    FileCaption.Text = first.filePath;

                    DrawOCROverlay(first);
                }
            });
        }

        private void ShowFileAt(int index)
        {
            if (index < 0 || index >= filesToProcess.Count)
                return;

            var file = filesToProcess[index];
            if (System.IO.File.Exists(file.filePath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(file.filePath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                ImagePanel.Source = bmp;
                DrawOCROverlay(file);
            }
        }

        private void OnExportDocx(object sender, RoutedEventArgs e)
        {
            var doc = filesToProcess[currentIndex]?.Document;
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