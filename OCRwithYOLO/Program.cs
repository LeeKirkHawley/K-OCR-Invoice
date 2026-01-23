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
            //string imagePath = "C:/OCR/Invoices/Simple-invoice-e1718189030194.jpg";
            string imagePath = "C:/OCR/Invoices/Sample-Invoice-printable.png";
            //string yoloModelPath = "C:/libraries/DocLayout-YOLO/doclayout_yolo_d4la_imgsz1600_docsynth_pretrain.onnx";
            string yoloModelPath = "C:/libraries/DocLayout-YOLO/doclayout_yolo_docstructbench_imgsz1024.onnx";

            var boxes = RunYolo(yoloModelPath, imagePath);
            SaveImageWithBoxes(imagePath, boxes);

            var fields = RunOcr(imagePath, boxes);
            fields = DeduplicateOcr(fields);

            var tableFields = DetectTables(fields, imagePath);

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

            using var bmpSrc = new Bitmap(imagePath);
            using var bmp = EnsureNonIndexed(bmpSrc, PixelFormat.Format24bppRgb);
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
                const float minWidth = 100f;
                const float minHeight = 50f;
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

                if (string.IsNullOrWhiteSpace(text) || text.Length < 10 || conf < .60f)
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
                .OrderByDescending(i => i.Area)
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
                s = Regex.Replace(s, @"[^a-z0-9\s]", "");
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

        static List<InvoiceField> DetectTables(List<InvoiceField> fields, string imagePath)
        {
            // Tunables
            const float yRowTolerance = 12f;
            const float xCenterTolerance = 20f;
            const float minCellWidth = 32f;
            const float minCellHeight = 12f;
            const int minColumns = 2;
            const int minAlignedRows = 2;

            var tableCells = new List<InvoiceField>();
            var items = fields
                .Where(f => !string.IsNullOrWhiteSpace(f.Text))
                .Where(f => f.Width >= minCellWidth && f.Height >= minCellHeight)
                .OrderBy(f => f.Y).ThenBy(f => f.X)
                .ToList();

            if (items.Count == 0)
            {
                Console.WriteLine("DetectTables: no usable items.");
                return tableCells;
            }

            // 1) Find header anchors
            static bool Has(string s, params string[] keys)
            {
                var t = s.ToLowerInvariant();
                foreach (var k in keys)
                {
                    if (t.Contains(k.ToLowerInvariant()))
                        return true;
                }
                return false;
            }

            var headerCandidates = items
                .Where(f => Has(f.Text, "description", "qty", "qty/ hr", "quantity", "unit price", "price", "total", "DESCRIPTION"))
                .ToList();

            if (headerCandidates.Count == 0)
            {
                Console.WriteLine("DetectTables: no header candidates in YOLO OCR fields; trying full-page OCR words.");

                // Pull fine-grained words and retry header search
                var wordFields = RunOcrFullPageWords(imagePath);

                // If you have `imagePath` available, prefer passing it directly:
                // var wordFields = RunOcrFullPageWords(imagePath);

                // Use words, but filter tiny artifacts
                var words = wordFields
                    .Where(f => f.Width >= minCellWidth / 2 && f.Height >= minCellHeight / 2)
                    .OrderBy(f => f.Y).ThenBy(f => f.X)
                    .ToList();

                var headerWords = words
                    .Where(f => Has(f.Text, "description", "qty", "quantity", "unit price", "price", "total", "qty/hr", "unit price" ))
                    .ToList();

                if (headerWords.Count == 0)
                {
                    Console.WriteLine("DetectTables: still no headers after full-page OCR; abort.");
                    return tableCells;
                }

                // Replace `items` with words so downstream logic can infer rows/columns
                items = words;
                headerCandidates = headerWords;
            }

            // 2) Choose the top-most header line (largest Y that still looks like a header row)
            var headerY = headerCandidates.Min(h => h.Y);
            var headerLine = items.Where(f => Math.Abs(f.Y - headerY) <= yRowTolerance).ToList();
            if (headerLine.Count < minColumns)
            {
                Console.WriteLine($"DetectTables: header row too sparse (count={headerLine.Count}).");
                return tableCells;
            }

            float headerBottom = headerLine.Max(f => f.Y + f.Height);

            // 3) Find bottom anchor (subtotal/total/tax area)
            var footerCandidates = items
                .Where(f => f.Y > headerBottom + 10f)
                .Where(f => Has(f.Text, "subtotal", "total", "tax"))
                .ToList();

            float tableBottom = footerCandidates.Count > 0
                ? footerCandidates.Min(f => f.Y) // first totals line
                : items.Max(f => f.Y + f.Height); // fallback: to the end

            // 4) Crop to table region
            var inRegion = items
                .Where(f => f.Y >= headerBottom && (f.Y + f.Height) <= tableBottom + 4f)
                .ToList();

            if (inRegion.Count < minColumns)
            {
                Console.WriteLine($"DetectTables: region too small (count={inRegion.Count}).");
                return tableCells;
            }

            // 5) Build rows by Y proximity/overlap
            var rows = new List<List<InvoiceField>>();
            foreach (var f in inRegion)
            {
                var target = rows.FirstOrDefault(r =>
                {
                    float ry1 = r.Min(x => x.Y);
                    float ry2 = r.Max(x => x.Y + x.Height);
                    bool yClose = (f.Y >= ry1 - yRowTolerance && f.Y <= ry2 + yRowTolerance)
                                  || OverlapY(f, r);
                    return yClose;
                });

                if (target == null) rows.Add(new List<InvoiceField> { f });
                else target.Add(f);
            }

            rows = rows
                .Select(r => r.OrderBy(x => x.X).ToList())
                .Where(r => r.Count >= minColumns)
                .OrderBy(r => r.Min(x => x.Y))
                .ToList();

            if (rows.Count < minAlignedRows)
            {
                Console.WriteLine($"DetectTables: not enough rows (rows={rows.Count}).");
                return tableCells;
            }

            // 6) Global column centers from all rows
            var perRowCenters = rows
                .Select(r => r.Select(c => c.X + c.Width / 2f).OrderBy(x => x).ToList())
                .ToList();

            var globalCenters = ClusterColumnCenters(perRowCenters, xCenterTolerance);
            if (globalCenters.Count < minColumns)
            {
                Console.WriteLine($"DetectTables: clustered columns too few (cols={globalCenters.Count}).");
                return tableCells;
            }

            // 7) Assign TableRow/TableCol
            for (int ri = 0; ri < rows.Count; ri++)
            {
                var row = rows[ri];
                int aligned = row.Count(c => Math.Abs((c.X + c.Width / 2f) - Nearest(globalCenters, c.X + c.Width / 2f)) <= xCenterTolerance);
                if (aligned < minColumns) continue;

                foreach (var cell in row)
                {
                    float cx = cell.X + cell.Width / 2f;
                    int col = IndexOfClosest(globalCenters, cx);
                    if (col < 0) continue;

                    cell.IsTableCell = true;
                    cell.TableRow = ri;
                    cell.TableCol = col;
                    tableCells.Add(cell);
                }
            }

            Console.WriteLine($"DetectTables: table cells={tableCells.Count}, rows={rows.Count}, cols={globalCenters.Count}");
            if (tableCells.Count == 0)
            {
                Console.WriteLine("DetectTables: header-based detection yielded 0 cells; falling back to geometric word clustering.");
                var wordFields = RunOcrFullPageWords(imagePath);
                var fallback = DetectTableFromWords(wordFields);
                Console.WriteLine($"DetectTables/Fallback: cells={fallback.Count}");
                return fallback;
            }
            return tableCells;

            Console.WriteLine($"DetectTables: table cells={tableCells.Count}, rows={rows.Count}, cols={globalCenters.Count}");
            return tableCells;

            // helpers
            static bool OverlapY(InvoiceField f, List<InvoiceField> row)
            {
                float y1 = f.Y, y2 = f.Y + f.Height;
                float ry1 = row.Min(x => x.Y);
                float ry2 = row.Max(x => x.Y + x.Height);
                return Math.Max(0, Math.Min(y2, ry2) - Math.Max(y1, ry1)) > 0;
            }

            static List<float> ClusterColumnCenters(List<List<float>> perRowCenters, float tol)
            {
                var all = perRowCenters.SelectMany(x => x).OrderBy(x => x).ToList();
                if (all.Count == 0) return new List<float>();

                var clusters = new List<List<float>> { new List<float> { all[0] } };
                foreach (var v in all.Skip(1))
                {
                    var last = clusters[^1];
                    var mean = last.Average();
                    if (Math.Abs(v - mean) <= tol) last.Add(v);
                    else clusters.Add(new List<float> { v });
                }
                return clusters.Select(c => c.Average()).ToList();
            }

            static float Nearest(List<float> centers, float v)
            {
                float best = float.MaxValue;
                foreach (var c in centers) best = Math.Min(best, Math.Abs(c - v));
                return best;
            }

            static int IndexOfClosest(List<float> centers, float v)
            {
                int idx = -1;
                float best = float.MaxValue;
                for (int i = 0; i < centers.Count; i++)
                {
                    float d = Math.Abs(centers[i] - v);
                    if (d < best) { best = d; idx = i; }
                }
                return idx;
            }
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

            using var imgSrc = new Bitmap(imagePath);
            using var img = EnsureNonIndexed(imgSrc, PixelFormat.Format24bppRgb);
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
            using var src = new Bitmap(imagePath);
            using var img = EnsureNonIndexed(src, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(img);
            using var pen = new Pen(DrawingColor.Red, 2);

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

        static Bitmap EnsureNonIndexed(Bitmap src, PixelFormat target = PixelFormat.Format24bppRgb)
        {
            if ((src.PixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
            {
                var dest = new Bitmap(src.Width, src.Height, target);
                using var g = Graphics.FromImage(dest);
                g.DrawImage(src, new Rectangle(0, 0, dest.Width, dest.Height));
                return dest;
            }
            return (Bitmap)src.Clone();
        }

        static List<InvoiceField> RunOcrFullPageWords(string imagePath)
        {
            var fields = new List<InvoiceField>();

            using var engine = new TesseractEngine(@"C:\Work\K-OCR\K-OCR\tessdata", "eng", EngineMode.LstmOnly);
            using var img = Pix.LoadFromFile(imagePath);

            engine.SetVariable("user_defined_dpi", "300");
            engine.SetVariable("preserve_interword_spaces", "1");

            using var page = engine.Process(img, PageSegMode.Auto);
            using var iter = page.GetIterator();
            iter.Begin();

            // Collect words (and short phrases)
            do
            {
                if (!iter.IsAtBeginningOf(PageIteratorLevel.Word))
                    continue;

                if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
                {
                    string text = iter.GetText(PageIteratorLevel.Word) ?? string.Empty;
                    text = text.Trim();
                    float conf = iter.GetConfidence(PageIteratorLevel.Word);

                    if (string.IsNullOrWhiteSpace(text)) continue;

                    // Keep even short headers like "QTY", "TOTAL" with slightly lower conf
                    if (text.Length >= 2 || conf >= 0.55f)
                    {
                        fields.Add(new InvoiceField
                        {
                            Text = text,
                            X = rect.X1,
                            Y = rect.Y1,
                            Width = rect.Width,
                            Height = rect.Height,
                            Label = "Word"
                        });
                    }
                }
            }
            while (iter.Next(PageIteratorLevel.Word));

            Console.WriteLine($"RunOcrFullPageWords: collected {fields.Count} word boxes.");
            return fields;
        }

        static List<InvoiceField> DetectTableFromWords(List<InvoiceField> wordItems)
        {
            // Tunables
            const float yRowTolerance = 12f;
            const float xCenterTolerance = 18f;
            const int minColumns = 3;         // invoices usually have >=3 columns
            const int minRows = 3;            // at least 3 data rows
            const float minWordW = 12f;       // filter noise
            const float minWordH = 10f;

            var tableCells = new List<InvoiceField>();

            var items = wordItems
                .Where(f => !string.IsNullOrWhiteSpace(f.Text))
                .Where(f => f.Width >= minWordW && f.Height >= minWordH)
                .OrderBy(f => f.Y).ThenBy(f => f.X)
                .ToList();

            if (items.Count == 0) return tableCells;

            // 1) Build rows by Y proximity and vertical overlap
            var rows = new List<List<InvoiceField>>();
            foreach (var f in items)
            {
                var target = rows.FirstOrDefault(r =>
                {
                    float ry1 = r.Min(x => x.Y);
                    float ry2 = r.Max(x => x.Y + x.Height);
                    bool yClose = (f.Y >= ry1 - yRowTolerance && f.Y <= ry2 + yRowTolerance)
                                  || OverlapY(f, r);
                    return yClose;
                });
                if (target == null) rows.Add(new List<InvoiceField> { f });
                else target.Add(f);
            }

            rows = rows
                .Select(r => r.OrderBy(x => x.X).ToList())
                .Where(r => r.Count >= 2) // need at least 2 items to be a row candidate
                .OrderBy(r => r.Min(x => x.Y))
                .ToList();

            if (rows.Count < minRows) return tableCells;

            // 2) Compute per-row centers and cluster to global columns
            var perRowCenters = rows
                .Select(r => r.Select(c => c.X + c.Width / 2f).OrderBy(x => x).ToList())
                .ToList();

            var globalCenters = ClusterColumnCenters(perRowCenters, xCenterTolerance);
            if (globalCenters.Count < minColumns)
            {
                // Try stricter clustering if too many near-duplicate centers bloated clusters
                globalCenters = ClusterColumnCenters(perRowCenters, xCenterTolerance * 0.8f);
            }
            if (globalCenters.Count < minColumns) return tableCells;

            // 3) Bias columns that look numeric (prices/qty)
            static bool LooksNumeric(string s)
            {
                var t = s.Trim();
                if (t.Length == 0) return false;
                // simple number/amount pattern
                return System.Text.RegularExpressions.Regex.IsMatch(t, @"^[\$]?\d{1,3}(?:[,]\d{3})*(?:\.\d{1,2})?$")
                    || System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+$");
            }

            var numericHits = new int[globalCenters.Count];
            foreach (var r in rows)
            {
                foreach (var c in r)
                {
                    float cx = c.X + c.Width / 2f;
                    int colIdx = IndexOfClosest(globalCenters, cx);
                    if (colIdx >= 0 && LooksNumeric(c.Text)) numericHits[colIdx]++;
                }
            }

            // Keep dominant columns if too many columns detected
            if (globalCenters.Count > 6)
            {
                var ranked = Enumerable.Range(0, globalCenters.Count)
                    .OrderByDescending(i => numericHits[i])
                    .ThenBy(i => globalCenters[i])
                    .Take(6)
                    .ToArray();

                globalCenters = ranked.Select(i => globalCenters[i]).ToList();
            }

            // 4) Assign cells to nearest global column; keep rows that align with columns
            var alignedRows = new List<(int idx, List<InvoiceField> row)>();
            for (int ri = 0; ri < rows.Count; ri++)
            {
                var row = rows[ri];
                int alignedCount = row.Count(c => Math.Abs((c.X + c.Width / 2f) - Nearest(globalCenters, c.X + c.Width / 2f)) <= xCenterTolerance);
                if (alignedCount >= Math.Min(minColumns, globalCenters.Count - 1))
                    alignedRows.Add((ri, row));
            }

            if (alignedRows.Count < minRows) return tableCells;

            foreach (var (ri, row) in alignedRows)
            {
                foreach (var cell in row)
                {
                    int col = IndexOfClosest(globalCenters, cell.X + cell.Width / 2f);
                    if (col < 0) continue;

                    cell.IsTableCell = true;
                    cell.TableRow = ri;
                    cell.TableCol = col;
                    tableCells.Add(cell);
                }
            }

            return tableCells;

            // helpers
            static bool OverlapY(InvoiceField f, List<InvoiceField> row)
            {
                float y1 = f.Y, y2 = f.Y + f.Height;
                float ry1 = row.Min(x => x.Y);
                float ry2 = row.Max(x => x.Y + x.Height);
                return Math.Max(0, Math.Min(y2, ry2) - Math.Max(y1, ry1)) > 0;
            }

            static List<float> ClusterColumnCenters(List<List<float>> perRowCenters, float tol)
            {
                var all = perRowCenters.SelectMany(x => x).OrderBy(x => x).ToList();
                if (all.Count == 0) return new List<float>();
                var clusters = new List<List<float>> { new List<float> { all[0] } };
                foreach (var v in all.Skip(1))
                {
                    var cur = clusters[^1];
                    var mean = cur.Average();
                    if (Math.Abs(v - mean) <= tol) cur.Add(v);
                    else clusters.Add(new List<float> { v });
                }
                return clusters.Select(c => c.Average()).ToList();
            }

            static float Nearest(List<float> centers, float v)
            {
                float best = float.MaxValue;
                foreach (var c in centers) best = Math.Min(best, Math.Abs(c - v));
                return best;
            }

            static int IndexOfClosest(List<float> centers, float v)
            {
                int idx = -1;
                float best = float.MaxValue;
                for (int i = 0; i < centers.Count; i++)
                {
                    float d = Math.Abs(centers[i] - v);
                    if (d < best) { best = d; idx = i; }
                }
                return idx;
            }
        }

    }
}