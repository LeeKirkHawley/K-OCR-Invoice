using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Tesseract;

namespace InvoiceParser
{
    public class InvoiceField
    {
        public string Text { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string Label { get; set; } = string.Empty; // Model class label
    }

    class Program
    {
        static void Main(string[] args)
        {
            string imagePath = "C:/OCR/Invoices/6271a424c9f168399794e4f3_Example-Invoice-Template-1-G.jpeg";
            string yoloModelPath = "C:/libraries/DocLayout-YOLO/doclayout_yolo_d4la_imgsz1600_docsynth_pretrain.onnx";

            var boxes = RunYolo(yoloModelPath, imagePath);
            var fields = RunOcr(imagePath, boxes);

            // Export CSV and rebuilt plain text
            var basePath = Path.Combine(Path.GetDirectoryName(imagePath) ?? ".", Path.GetFileNameWithoutExtension(imagePath));
            var csvPath = basePath + ".csv";
            var txtPath = basePath + ".txt";

            SaveAsCsv(fields, csvPath);
            SaveAsText(fields, txtPath);

            Console.WriteLine($"Done. {fields.Count} OCR regions processed.");
            Console.WriteLine($"Saved: {csvPath}");
            Console.WriteLine($"Saved: {txtPath}");
        }

        static List<(float X, float Y, float Width, float Height, string Label)> RunYolo(string modelPath, string imagePath)
        {
            const int inputSize = 1600;
            const float scoreThreshold = 0.25f;
            const float nmsIouThreshold = 0.45f;

            using var session = new InferenceSession(modelPath);

            var inputName = session.InputMetadata.Keys
                .First(k =>
                {
                    var md = session.InputMetadata[k];
                    return md.Dimensions?.Length == 4;
                });

            using var bmp = new Bitmap(imagePath);
            var (letterboxed, scale, padX, padY) = Letterbox(bmp, inputSize, inputSize, Color.Black);

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

            static (Bitmap img, float scale, float padX, float padY) Letterbox(Bitmap src, int dstW, int dstH, Color padColor)
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

            using var engine = new TesseractEngine(@"C:\Work\K-OCR\K-OCR\tessdata", "eng", EngineMode.Default);
            using var img = Pix.LoadFromFile(imagePath);

            foreach (var box in boxes)
            {
                var x = Math.Clamp((int)box.X, 0, img.Width - 1);
                var y = Math.Clamp((int)box.Y, 0, img.Height - 1);
                var w = Math.Clamp((int)box.Width, 1, img.Width - x);
                var h = Math.Clamp((int)box.Height, 1, img.Height - y);

                var rect = new Tesseract.Rect(x, y, w, h);

                using var page = engine.Process(img, rect);
                string text = page.GetText().Trim();

                fields.Add(new InvoiceField
                {
                    Text = text,
                    X = box.X,
                    Y = box.Y,
                    Width = box.Width,
                    Height = box.Height,
                    Label = box.Label
                });

                Console.WriteLine($"[{box.Label}] ({x},{y},{w},{h}) => {text}");
            }

            return fields;
        }

        // Exports (CSV/TXT)

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

                // Flatten line breaks so each CSV record stays on a single line
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
    }
}