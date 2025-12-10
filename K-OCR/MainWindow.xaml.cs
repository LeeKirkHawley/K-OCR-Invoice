using K_OCR.Models;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
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

                        GetBlocks(page);

                        ocrFile.ocrText = text;

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


        private static void GetBlocks(Page page)
        {
            //using (var iter = page.GetIterator())
            //{
            //    iter.Begin();

            //    do
            //    {
            //        do
            //        {
            //            do
            //            {
            //                do
            //                {
            //                    if (iter.IsAtBeginningOf(PageIteratorLevel.Block))
            //                    {
            //                        Console.WriteLine("<BLOCK>");
            //                    }

            //                    Console.Write(iter.GetText(PageIteratorLevel.Word));
            //                    Console.Write(" ");

            //                    if (iter.IsAtFinalOf(PageIteratorLevel.TextLine, PageIteratorLevel.Word))
            //                    {
            //                        Console.WriteLine();
            //                    }
            //                } while (iter.Next(PageIteratorLevel.TextLine, PageIteratorLevel.Word));

            //                if (iter.IsAtFinalOf(PageIteratorLevel.Para, PageIteratorLevel.TextLine))
            //                {
            //                    Console.WriteLine();
            //                }
            //            } while (iter.Next(PageIteratorLevel.Para, PageIteratorLevel.TextLine));
            //        } while (iter.Next(PageIteratorLevel.Block, PageIteratorLevel.Para));
            //    } while (iter.Next(PageIteratorLevel.Block));
            //}
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
                    OCRdTextPanel.Text = sb.ToString();
                }
            });
        }
    }
}