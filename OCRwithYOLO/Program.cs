using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Tesseract;
using DrawingColor = System.Drawing.Color;
using System.Text.RegularExpressions;

namespace InvoiceParser
{
    public class InvoiceField
    {
        public string Text { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string Label { get; set; } = string.Empty;
        public bool IsTableCell { get; set; } = false;
        public int TableRow { get; set; } = -1;
        public int TableCol { get; set; } = -1;
    }

    class Program
    {
        static void Main(string[] args)
        {
            string imagePath = "C:/OCR/Invoices/Test1.jpg";
            string yoloModelPath = "C:/libraries/DocLayout-YOLO/doclayout_yolo_d4la_imgsz1600_docsynth_pretrain.onnx";

            var boxes = RunYolo(yoloModelPath, imagePath);
            SaveImageWithBoxes(imagePath, boxes);

            var fields = RunOcr(imagePath, boxes);
            fields = DeduplicateOcr(fields);

            var tableFields = DetectTables(fields);

            var finalBoxes = fields.Select(f => (f.X, f.Y, f.Width, f.Height, f.Label)).ToList();
            SaveImageWithBoxes(imagePath, finalBoxes, "_AfterDedupe.jpg");

            var basePath = Path.Combine(Path.GetDirectoryName(imagePath) ?? ".", Path.GetFileNameWithoutExtension(imagePath));
            var csvPath = basePath + ".csv";
            var txtPath = basePath + ".txt";
            var docxPath = basePath + ".docx";

            SaveAsCsv(fields, csvPath);
            SaveAsText(fields, txtPath);
            SaveAsDocx(fields, docxPath, imagePath);

            Console.WriteLine($"Done. {fields.Count} OCR regions processed ({tableFields.Count} table cells).");
            Console.WriteLine($"Saved: {csvPath}");
            Console.WriteLine($"Saved: {txtPath}");
            Console.WriteLine($"Saved: {docxPath}");
        }

        static List<(float X, float Y, float Width, float Height, string Label)> RunYolo(string modelPath, string imagePath)
        {
            const int inputSize = 1600;
            const float scoreThreshold = 0.4f;
            const float nmsIouThreshold = 0.45f;

            using var session = new InferenceSession(modelPath);

            var inputName = session.InputMetadata.Keys
                .First(k =>
                {
                    var md = session.InputMetadata[k];
                    return md.Dimensions?.Length == 4;
                });

            using var bmp = new Bitmap(imagePath);
            var (letterboxed, scale, padX, padY) = Letterbox(bmp, inputSize, inputSize, DrawingColor.Black);

            var inputTensor = new DenseTensor<float>(new[] { 1, 3, inputSize, inputSize });
            for (int y = 0; y < inputSize; y++)
            {
                for (int x = 0; x < inputSize; x++)
                {
                    var c = letterboxed.GetPixel(x, y);
                    inputTensor[0, 0, y, x] = c.R / 255f;
                    inputTensor[0, 1, y, x] = c.G / 255f;
                    inputTensor[0, 2, y, x] = c.B / 255f;
                }
            }

            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };
            using var results = session.Run(inputs);

            var output = results.Select(r => r.AsTensor<float>()).First(t => t != null);

            var dimsArr = output.Dimensions.ToArray();
            int dimCount = dimsArr.Length;
            int numBoxes = dimCount == 3 ? dimsArr[1] : dimsArr[0];
            int numAttrs = dimsArr[dimCount - 1];

            var candidates = new List<(RectangleF boxImgSpace, float score, int cls)>();
            for (int i = 0; i < numBoxes; i++)
            {
                float Read(int a) => dimCount == 3 ? output[0, i, a] : output[i, a];

                float cx = Read(0);
                float cy = Read(1);
                float w = Read(2);
                float h = Read(3);

                float obj = Sigmoid(Read(4));

                int bestClass = -1;
                float bestClassProb = 0f;
                for (int c = 5; c < numAttrs; c++)
                {
                    float p = Sigmoid(Read(c));
                    if (p > bestClassProb)
                    {
                        bestClassProb = p;
                        bestClass = c - 5;
                    }
                }

                float score = obj * bestClassProb;
                if (score < scoreThreshold)
                    continue;

                float x1 = cx - w / 2f;
                float y1 = cy - h / 2f;
                float x2 = cx + w / 2f;
                float y2 = cy + h / 2f;

                float imgX1 = (x1 - padX) / scale;
                float imgY1 = (y1 - padY) / scale;
                float imgX2 = (x2 - padX) / scale;
                float imgY2 = (y2 - padY) / scale;

                imgX1 = MathF.Max(0, MathF.Min(bmp.Width - 1, imgX1));
                imgY1 = MathF.Max(0, MathF.Min(bmp.Height - 1, imgY1));
                imgX2 = MathF.Max(0, MathF.Min(bmp.Width - 1, imgX2));
                imgY2 = MathF.Max(0, MathF.Min(bmp.Height - 1, imgY2));

                var rect = RectangleF.FromLTRB(imgX1, imgY1, imgX2, imgY2);
                if (rect.Width <= 1 || rect.Height <= 1)
                    continue;

                candidates.Add((rect, score, bestClass));
            }

            var kept = Nms(candidates, nmsIouThreshold);

            var boxes = kept.Select(k => ((float)k.boxImgSpace.X, (float)k.boxImgSpace.Y, (float)k.boxImgSpace.Width, (float)k.boxImgSpace.Height, "CandidateField"))
                            .ToList();

            Console.WriteLine($"YOLO: {boxes.Count} boxes after NMS");
            return boxes;

            static (Bitmap img, float scale, float padX, float padY) Letterbox(Bitmap src, int dstW, int dstH, DrawingColor padColor)
            {
                float scale = Math.Min((float)dstW / src.Width, (float)dstH / src.Height);
                int newW = (int)Math.Round(src.Width * scale);
                int newH = (int)Math.Round(src.Height * scale);
                int padX = (dstW - newW) / 2;
                int padY = (dstH - newH) / 2;

                var canvas = new Bitmap(dstW, dstH, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(canvas))
                {
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(padColor);
                    g.DrawImage(src, new Rectangle(padX, padY, newW, newH));
                }
                return (canvas, scale, padX, padY);
            }

            static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

            static List<(RectangleF boxImgSpace, float score, int cls)> Nms(List<(RectangleF boxImgSpace, float score, int cls)> dets, float iouThresh)
            {
                var result = new List<(RectangleF, float, int)>();
                foreach (var clsGroup in dets.GroupBy(d => d.cls))
                {
                    var list = clsGroup.OrderByDescending(d => d.score).ToList();
                    var picked = new bool[list.Count];

                    for (int i = 0; i < list.Count; i++)
                    {
                        if (picked[i]) continue;
                        result.Add(list[i]);
                        for (int j = i + 1; j < list.Count; j++)
                        {
                            if (picked[j]) continue;
                            if (IoU(list[i].boxImgSpace, list[j].boxImgSpace) > iouThresh)
                                picked[j] = true;
                        }
                    }
                }
                return result;
            }

            static float IoU(RectangleF a, RectangleF b)
            {
                float x1 = MathF.Max(a.Left, b.Left);
                float y1 = MathF.Max(a.Top, b.Top);
                float x2 = MathF.Min(a.Right, b.Right);
                float y2 = MathF.Min(a.Bottom, b.Bottom);
                float inter = MathF.Max(0, x2 - x1) * MathF.Max(0, y2 - y1);
                float union = a.Width * a.Height + b.Width * b.Height - inter;
                return union <= 0 ? 0 : inter / union;
            }
        }

        static List<InvoiceField> RunOcr(string imagePath, List<(float X, float Y, float Width, float Height, string Label)> boxes)
        {
            var fields = new List<InvoiceField>();

            using var engine = new TesseractEngine(@"C:\Work\K-OCR\K-OCR\tessdata", "eng", EngineMode.LstmOnly);
            using var img = Pix.LoadFromFile(imagePath);

            engine.SetVariable("user_defined_dpi", "300");
            engine.SetVariable("preserve_interword_spaces", "1");

            foreach (var box in boxes)
            {
                // Skip very small or narrow boxes (likely noise)
                const float minWidth = 100f;  // Minimum width in pixels
                const float minHeight = 50f;  // Minimum height in pixels
                if (box.Width < minWidth || box.Height < minHeight)
                {
                    Console.WriteLine($"[{box.Label}] ({box.X},{box.Y},{box.Width},{box.Height}) SKIPPED (too small)");
                    continue;
                }

                const float pad = 0.05f;
                int x = Math.Max(0, (int)Math.Floor(box.X - box.Width * pad));
                int y = Math.Max(0, (int)Math.Floor(box.Y - box.Height * pad));
                int w = (int)Math.Ceiling(box.Width * (1 + 2 * pad));
                int h = (int)Math.Ceiling(box.Height * (1 + 2 * pad));

                if (x + w > img.Width) w = img.Width - x;
                if (y + h > img.Height) h = img.Height - y;
                if (w <= 1 || h <= 1) continue;

                var rect = new Tesseract.Rect(x, y, w, h);
                var psm = GetPsmForBox(w, h);

                using var page = engine.Process(img, rect, psm);
                string text = page.GetText().Trim();
                float conf = page.GetMeanConfidence();

                // Skip empty, short, or low-confidence OCR results
                if (string.IsNullOrWhiteSpace(text) || text.Length < 10 || conf < .60f)  // Raised length to 10
                {
                    Console.WriteLine($"[{box.Label}] ({x},{y},{w},{h}) SKIPPED (short/low conf: len={text.Length}, conf={conf:F1})");
                    continue;
                }

                fields.Add(new InvoiceField
                {
                    Text = text,
                    X = box.X,
                    Y = box.Y,
                    Width = box.Width,
                    Height = box.Height,
                    Label = box.Label
                });

                Console.WriteLine($"[{box.Label}] ({x},{y},{w},{h}) PSM={psm} Conf={conf:F1} => {text}");
            }

            return fields;

            static PageSegMode GetPsmForBox(int w, int h)
            {
                float aspect = w / Math.Max(1f, (float)h);
                if (h < 28 && w < 140) return PageSegMode.SingleWord;
                if (aspect > 6f && h < 60) return PageSegMode.SingleLine;
                return PageSegMode.Auto;
            }
        }

        static List<InvoiceField> DeduplicateOcr(List<InvoiceField> fields, float iouThreshold = 0.4f, float containmentThreshold = 0.75f)
        {
            var items = fields
                .Where(f => !string.IsNullOrWhiteSpace(f.Text))
                .Select(f => new
                {
                    Field = f,
                    NormText = NormalizeForCompare(f.Text),
                    Area = Math.Max(1f, f.Width) * Math.Max(1f, f.Height)
                })
                .OrderByDescending(i => i.Area)  // Prefer larger boxes first
                .ThenByDescending(i => i.NormText.Length)
                .ToList();

            var kept = new List<InvoiceField>();

            foreach (var cur in items)
            {
                bool isDup = false;

                foreach (var keptField in kept)
                {
                    float iou = IoU(cur.Field, keptField);
                    float contain = Containment(cur.Field, keptField);

                    // High containment: check substring + fuzzy similarity
                    if (contain >= containmentThreshold)
                    {
                        var keptNorm = NormalizeForCompare(keptField.Text);
                        if (IsSubstringOrPrefix(cur.NormText, keptNorm) ||
                            LevenshteinSimilarity(cur.NormText, keptNorm) > 0.8f)
                        {
                            Console.WriteLine($"DISCARDED (high containment): '{cur.NormText}' overlaps '{keptNorm}'");
                            isDup = true;
                            break;
                        }
                    }

                    // Moderate overlap: check IoU + similarity
                    if (iou >= iouThreshold)
                    {
                        var keptNorm = NormalizeForCompare(keptField.Text);
                        if (JaccardSimilarity(cur.NormText, keptNorm) > 0.6f ||
                            LevenshteinSimilarity(cur.NormText, keptNorm) > 0.75f)
                        {
                            Console.WriteLine($"DISCARDED (IoU overlap): '{cur.NormText}' overlaps '{keptNorm}'");
                            isDup = true;
                            break;
                        }
                    }
                }

                if (!isDup)
                {
                    kept.Add(cur.Field);
                    Console.WriteLine($"KEPT: '{cur.NormText}'");
                }
            }

            return kept;

            static string NormalizeForCompare(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                s = s.ToLowerInvariant();
                s = Regex.Replace(s, @"[^a-z0-9\s]", "");  // Remove punctuation, keep letters/numbers/spaces
                s = Regex.Replace(s, @"\s{2,}", " ").Trim();
                return s;
            }

            static bool IsSubstringOrPrefix(string a, string b)
            {
                if (a.Length == 0 || b.Length == 0) return false;
                if (a == b) return true;
                return a.Contains(b) || b.Contains(a) || a.StartsWith(b) || b.StartsWith(a);
            }

            static float JaccardSimilarity(string a, string b)
            {
                var aw = new HashSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                var bw = new HashSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                if (aw.Count == 0 || bw.Count == 0) return 0f;

                int inter = aw.Intersect(bw).Count();
                int uni = aw.Union(bw).Count();
                return uni == 0 ? 0f : (float)inter / uni;
            }

            static float LevenshteinSimilarity(string a, string b)
            {
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0f;
                int dist = LevenshteinDistance(a, b);
                int maxLen = Math.Max(a.Length, b.Length);
                return maxLen == 0 ? 1f : 1f - ((float)dist / maxLen);
            }

            static int LevenshteinDistance(string a, string b)
            {
                int[,] d = new int[a.Length + 1, b.Length + 1];
                for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
                for (int j = 0; j <= b.Length; j++) d[0, j] = j;

                for (int i = 1; i <= a.Length; i++)
                {
                    for (int j = 1; j <= b.Length; j++)
                    {
                        int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                        d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    }
                }
                return d[a.Length, b.Length];
            }

            static float IoU(InvoiceField a, InvoiceField b)
            {
                float ax1 = a.X, ay1 = a.Y, ax2 = a.X + a.Width, ay2 = a.Y + a.Height;
                float bx1 = b.X, by1 = b.Y, bx2 = b.X + b.Width, by2 = b.Y + b.Height;
                float x1 = MathF.Max(ax1, bx1);
                float y1 = MathF.Max(ay1, by1);
                float x2 = MathF.Min(ax2, bx2);
                float y2 = MathF.Min(ay2, by2);
                float inter = MathF.Max(0, x2 - x1) * MathF.Max(0, y2 - y1);
                float areaA = (ax2 - ax1) * (ay2 - ay1);
                float areaB = (bx2 - bx1) * (by2 - by1);
                float uni = areaA + areaB - inter;
                return uni <= 0 ? 0 : inter / uni;
            }

            static float Containment(InvoiceField a, InvoiceField b)
            {
                float ax1 = a.X, ay1 = a.Y, ax2 = a.X + a.Width, ay2 = a.Y + a.Height;
                float bx1 = b.X, by1 = b.Y, bx2 = b.X + b.Width, by2 = b.Y + b.Height;
                float x1 = MathF.Max(ax1, bx1);
                float y1 = MathF.Max(ay1, by1);
                float x2 = MathF.Min(ax2, bx2);
                float y2 = MathF.Min(ay2, by2);
                float inter = MathF.Max(0, x2 - x1) * MathF.Max(0, y2 - y1);
                float areaA = MathF.Max(1f, (ax2 - ax1) * (ay2 - ay1));
                float areaB = MathF.Max(1f, (bx2 - bx1) * (by2 - by1));
                float smaller = MathF.Min(areaA, areaB);
                return inter / smaller;
            }
        }

        static List<InvoiceField> DetectTables(List<InvoiceField> fields)
        {
            var tableCells = new List<InvoiceField>();
            const int yTolerance = 8;
            const int colGapMin = 20;

            var rows = fields
                .OrderBy(f => f.Y)
                .GroupBy(f => (int)(f.Y / yTolerance))
                .Where(g => g.Count() >= 2)
                .ToList();

            foreach (var rowGroup in rows)
            {
                var rowFields = rowGroup.OrderBy(f => f.X).ToList();

                bool hasGaps = false;
                for (int i = 1; i < rowFields.Count; i++)
                {
                    float gap = rowFields[i].X - (rowFields[i - 1].X + rowFields[i - 1].Width);
                    if (gap >= colGapMin)
                    {
                        hasGaps = true;
                        break;
                    }
                }

                if (!hasGaps) continue;

                for (int col = 0; col < rowFields.Count; col++)
                {
                    var cell = rowFields[col];
                    cell.IsTableCell = true;
                    cell.TableRow = rowGroup.Key;
                    cell.TableCol = col;
                    tableCells.Add(cell);
                }
            }

            return tableCells;
        }

        static void SaveAsCsv(IEnumerable<InvoiceField> fields, string path)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Label,X,Y,Width,Height,Text");

            foreach (var f in fields.OrderBy(f => f.Y).ThenBy(f => f.X))
            {
                var cells = new[]
                {
                    CsvEscape(f.Label),
                    f.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    f.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    f.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    f.Height.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    CsvEscape(f.Text)
                };
                sb.AppendLine(string.Join(",", cells));
            }

            File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);

            static string CsvEscape(string value)
            {
                if (string.IsNullOrEmpty(value)) return string.Empty;

                var s = value.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

                if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                {
                    return "\"" + s.Replace("\"", "\"\"") + "\"";
                }
                return s;
            }
        }

        static void SaveAsText(IEnumerable<InvoiceField> fields, string path, float yTolerance = 10f)
        {
            var rows = fields
                .Where(f => !string.IsNullOrWhiteSpace(f.Text))
                .GroupBy(f => (int)MathF.Round(f.Y / yTolerance))
                .OrderBy(g => g.Key)
                .Select(g => g.OrderBy(f => f.X).Select(f => f.Text.Trim()))
                .ToList();

            var sb = new System.Text.StringBuilder();
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(" ", row).Trim());
            }

            File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
        }

        static void SaveAsDocx(IEnumerable<InvoiceField> fields, string path, string imagePath, float minFontPt = 8f, float maxFontPt = 16f, float fontHeightScale = 0.42f, string fontName = "Arial Narrow")
        {
            var items = fields
                .Where(f => !string.IsNullOrWhiteSpace(f.Text))
                .Select(f => new
                {
                    X = f.X,
                    Y = f.Y,
                    W = Math.Max(1f, f.Width),
                    H = Math.Max(1f, f.Height),
                    Text = f.Text.Replace("\r\n", "\n").Replace("\r", "\n")
                })
                .ToList();

            using var img = new Bitmap(imagePath);
            int imgW = img.Width;
            int imgH = img.Height;

            const int TwipsPerInch = 1440;
            const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

            int pageWidthTw = (int)Math.Round(8.5 * TwipsPerInch);
            int marginLeftTw = (int)Math.Round(0.5 * TwipsPerInch);
            int marginRightTw = (int)Math.Round(0.5 * TwipsPerInch);
            int marginTopTw = (int)Math.Round(0.5 * TwipsPerInch);
            int marginBottomTw = (int)Math.Round(0.5 * TwipsPerInch);

            int contentWidthTw = pageWidthTw - marginLeftTw - marginRightTw;
            float scale = imgW > 0 ? (float)contentWidthTw / imgW : 1f;
            int contentHeightTw = (int)Math.Round(imgH * scale);
            int pageHeightTw = contentHeightTw + marginTopTw + marginBottomTw;

            using var wordDoc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body ?? mainPart.Document.AppendChild(new Body());

            var sectPr = new SectionProperties(
                new PageSize { Width = (uint)pageWidthTw, Height = (uint)pageHeightTw },
                new PageMargin
                {
                    Top = marginTopTw,
                    Bottom = marginBottomTw,
                    Left = (uint)marginLeftTw,
                    Right = (uint)marginRightTw
                }
            );
            body.AppendChild(sectPr);

            foreach (var it in items.OrderBy(i => i.Y).ThenBy(i => i.X))
            {
                int xTw = marginLeftTw + (int)Math.Round(it.X * scale);
                int yTw = marginTopTw + (int)Math.Round(it.Y * scale);
                int wTw = (int)Math.Round(it.W * scale);
                int hTw = (int)Math.Round(it.H * scale);

                var p = new Paragraph();
                var pPr = new ParagraphProperties();

                var frame = new FrameProperties();
                frame.SetAttribute(new OpenXmlAttribute("w", "w", Wns, Math.Max(1, wTw).ToString()));
                frame.SetAttribute(new OpenXmlAttribute("w", "h", Wns, Math.Max(1, hTw).ToString()));
                frame.SetAttribute(new OpenXmlAttribute("w", "x", Wns, xTw.ToString()));
                frame.SetAttribute(new OpenXmlAttribute("w", "y", Wns, yTw.ToString()));
                frame.SetAttribute(new OpenXmlAttribute("w", "wrap", Wns, "none"));
                frame.SetAttribute(new OpenXmlAttribute("w", "hAnchor", Wns, "page"));
                frame.SetAttribute(new OpenXmlAttribute("w", "vAnchor", Wns, "page"));
                pPr.Append(frame);
                p.Append(pPr);

                float hPt = hTw / 20f;
                float fontPt = Math.Clamp(hPt * fontHeightScale, minFontPt, maxFontPt);

                var r = new Run();

                var rPr = new RunProperties(
                    new RunFonts
                    {
                        Ascii = fontName,
                        HighAnsi = fontName,
                        EastAsia = fontName,
                        ComplexScript = fontName
                    },
                    new FontSize { Val = ((int)(fontPt)).ToString() }
                );
                r.Append(rPr);

                var parts = it.Text.Split('\n');
                for (int i = 0; i < parts.Length; i++)
                {
                    r.Append(new Text(parts[i]) { Space = SpaceProcessingModeValues.Preserve });
                    if (i < parts.Length - 1)
                        r.Append(new Break());
                }

                p.Append(r);
                body.Append(p);
            }

            mainPart.Document.Save();
        }

        static void SaveImageWithBoxes(string imagePath, List<(float X, float Y, float Width, float Height, string Label)> boxes, string suffix = "_AfterYOLO.jpg")
        {
            using var img = new Bitmap(imagePath);
            using var g = Graphics.FromImage(img);
            var pen = new Pen(DrawingColor.Red, 2);

            foreach (var box in boxes)
            {
                var rect = new Rectangle((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height);
                g.DrawRectangle(pen, rect);
            }

            var dir = Path.GetDirectoryName(imagePath) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(imagePath);
            var outPath = Path.Combine(dir, $"{baseName}{suffix}");

            img.Save(outPath, System.Drawing.Imaging.ImageFormat.Jpeg);
            Console.WriteLine($"Saved debug image: {outPath}");
        }
    }
}