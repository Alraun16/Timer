using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;

namespace Timer
{
    internal static class SvgIconRenderer
    {
        private const string FallbackColor = "#C7C5C2";

        public static BitmapSource Render(string path, int pixelSize)
        {
            var document = LoadDocument(path);
            var root = document.Root ?? throw new InvalidOperationException("SVG root is missing.");
            Rect viewBox = ReadViewBox(root);

            var visual = new DrawingVisual();
            using (DrawingContext context = visual.RenderOpen())
            {
                double scale = Math.Min(pixelSize / viewBox.Width, pixelSize / viewBox.Height);
                double x = (pixelSize - viewBox.Width * scale) / 2;
                double y = (pixelSize - viewBox.Height * scale) / 2;

                context.PushTransform(new TranslateTransform(x, y));
                context.PushTransform(new ScaleTransform(scale, scale));
                context.PushTransform(new TranslateTransform(-viewBox.X, -viewBox.Y));
                DrawElement(root, context, PaintState.Default);
                context.Pop();
                context.Pop();
                context.Pop();
            }

            var bitmap = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static XDocument LoadDocument(string path)
        {
            string svg = File.ReadAllText(path);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null
            };

            using var reader = XmlReader.Create(new StringReader(svg), settings);
            return XDocument.Load(reader);
        }

        private static void DrawElement(XElement element, DrawingContext context, PaintState inherited)
        {
            PaintState state = PaintState.FromElement(element, inherited);
            Transform? transform = ReadTransform((string?)element.Attribute("transform"));
            if (transform != null)
                context.PushTransform(transform);

            Geometry? geometry = CreateGeometry(element);
            if (geometry != null)
            {
                context.DrawGeometry(state.Fill, state.CreatePen(), geometry);
            }

            foreach (XElement child in element.Elements())
            {
                DrawElement(child, context, state);
            }

            if (transform != null)
                context.Pop();
        }

        private static Geometry? CreateGeometry(XElement element)
        {
            string name = element.Name.LocalName;
            return name switch
            {
                "path" => CreatePathGeometry(element),
                "circle" => new EllipseGeometry(
                    new Point(ReadDouble(element, "cx"), ReadDouble(element, "cy")),
                    ReadDouble(element, "r"),
                    ReadDouble(element, "r")),
                "ellipse" => new EllipseGeometry(
                    new Point(ReadDouble(element, "cx"), ReadDouble(element, "cy")),
                    ReadDouble(element, "rx"),
                    ReadDouble(element, "ry")),
                "rect" => CreateRectangleGeometry(element),
                "line" => new LineGeometry(
                    new Point(ReadDouble(element, "x1"), ReadDouble(element, "y1")),
                    new Point(ReadDouble(element, "x2"), ReadDouble(element, "y2"))),
                _ => null
            };
        }

        private static Geometry? CreatePathGeometry(XElement element)
        {
            string? data = (string?)element.Attribute("d");
            if (string.IsNullOrWhiteSpace(data))
                return null;

            return Geometry.Parse(data);
        }

        private static RectangleGeometry CreateRectangleGeometry(XElement element)
        {
            double x = ReadDouble(element, "x");
            double y = ReadDouble(element, "y");
            double width = ReadDouble(element, "width");
            double height = ReadDouble(element, "height");
            double rx = ReadDouble(element, "rx");
            double ry = ReadDouble(element, "ry");
            if (ry == 0) ry = rx;

            return new RectangleGeometry(new Rect(x, y, width, height), rx, ry);
        }

        private static Transform? ReadTransform(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var group = new TransformGroup();
            foreach (Match match in Regex.Matches(value, @"(?<name>\w+)\((?<args>[^)]*)\)"))
            {
                string name = match.Groups["name"].Value;
                double[] args = SplitNumbers(match.Groups["args"].Value);

                Transform? transform = name switch
                {
                    "translate" when args.Length >= 1 => new TranslateTransform(args[0], args.Length >= 2 ? args[1] : 0),
                    "scale" when args.Length >= 1 => new ScaleTransform(args[0], args.Length >= 2 ? args[1] : args[0]),
                    "rotate" when args.Length >= 1 => args.Length >= 3
                        ? new RotateTransform(args[0], args[1], args[2])
                        : new RotateTransform(args[0]),
                    "matrix" when args.Length >= 6 => new MatrixTransform(args[0], args[1], args[2], args[3], args[4], args[5]),
                    _ => null
                };

                if (transform != null)
                    group.Children.Add(transform);
            }

            if (group.Children.Count == 0)
                return null;

            group.Freeze();
            return group;
        }

        private static Rect ReadViewBox(XElement root)
        {
            string? viewBox = (string?)root.Attribute("viewBox");
            if (!string.IsNullOrWhiteSpace(viewBox))
            {
                double[] values = SplitNumbers(viewBox);
                if (values.Length >= 4 && values[2] > 0 && values[3] > 0)
                    return new Rect(values[0], values[1], values[2], values[3]);
            }

            double width = ReadLength((string?)root.Attribute("width"));
            double height = ReadLength((string?)root.Attribute("height"));
            return new Rect(0, 0, width > 0 ? width : 24, height > 0 ? height : 24);
        }

        private static double ReadDouble(XElement element, string attributeName)
            => ReadLength((string?)element.Attribute(attributeName));

        private static double ReadLength(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            string normalized = value.Trim().Replace("px", "", StringComparison.OrdinalIgnoreCase);
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
                ? result
                : 0;
        }

        private static double[] SplitNumbers(string value)
            => Regex.Split(value.Trim(), @"[\s,]+")
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
                .ToArray();

        private readonly record struct PaintState(
            Brush? Fill,
            Brush? Stroke,
            double StrokeWidth,
            PenLineCap StrokeLineCap,
            PenLineJoin StrokeLineJoin)
        {
            public static PaintState Default { get; } = new(CreateBrush(FallbackColor), null, 1, PenLineCap.Flat, PenLineJoin.Miter);

            public static PaintState FromElement(XElement element, PaintState inherited)
            {
                Brush? fill = ReadPaint(element, "fill", inherited.Fill);
                Brush? stroke = ReadPaint(element, "stroke", inherited.Stroke);
                double strokeWidth = ReadLength(ReadPresentationValue(element, "stroke-width"));
                if (strokeWidth == 0) strokeWidth = inherited.StrokeWidth;

                return new PaintState(
                    fill,
                    stroke,
                    strokeWidth,
                    ReadLineCap(ReadPresentationValue(element, "stroke-linecap"), inherited.StrokeLineCap),
                    ReadLineJoin(ReadPresentationValue(element, "stroke-linejoin"), inherited.StrokeLineJoin));
            }

            public Pen? CreatePen()
            {
                if (Stroke == null)
                    return null;

                var pen = new Pen(Stroke, StrokeWidth)
                {
                    StartLineCap = StrokeLineCap,
                    EndLineCap = StrokeLineCap,
                    LineJoin = StrokeLineJoin
                };
                pen.Freeze();
                return pen;
            }

            private static Brush? ReadPaint(XElement element, string attributeName, Brush? inherited)
            {
                string? value = ReadPresentationValue(element, attributeName);
                if (value == null)
                    return inherited;

                value = value.Trim();
                if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
                    return CreateBrush(ReadColor(element) ?? FallbackColor);

                return CreateBrush(value);
            }

            private static string? ReadPresentationValue(XElement element, string name)
            {
                string? value = (string?)element.Attribute(name);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                string? style = (string?)element.Attribute("style");
                if (string.IsNullOrWhiteSpace(style))
                    return null;

                foreach (string declaration in style.Split(';'))
                {
                    int separatorIndex = declaration.IndexOf(':');
                    if (separatorIndex <= 0)
                        continue;

                    string declarationName = declaration[..separatorIndex].Trim();
                    if (!declarationName.Equals(name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return declaration[(separatorIndex + 1)..].Trim();
                }

                return null;
            }

            private static string? ReadColor(XElement element)
            {
                XElement? current = element;
                while (current != null)
                {
                    string? color = (string?)current.Attribute("color");
                    if (!string.IsNullOrWhiteSpace(color))
                        return color;

                    current = current.Parent;
                }

                return null;
            }

            private static Brush CreateBrush(string color)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                brush.Freeze();
                return brush;
            }

            private static PenLineCap ReadLineCap(string? value, PenLineCap fallback)
                => value switch
                {
                    "round" => PenLineCap.Round,
                    "square" => PenLineCap.Square,
                    _ => fallback
                };

            private static PenLineJoin ReadLineJoin(string? value, PenLineJoin fallback)
                => value switch
                {
                    "round" => PenLineJoin.Round,
                    "bevel" => PenLineJoin.Bevel,
                    _ => fallback
                };
        }
    }
}
