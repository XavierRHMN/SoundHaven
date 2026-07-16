using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace SoundHaven.Helpers;

public static class DominantColorFinder
{
    private static readonly Color FallbackColor = Color.Parse("#546E7A");

    public static Color GetDominantColor(Bitmap bitmap, int maxDimension = 100)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);

        using SKBitmap original = ConvertToSkia(bitmap);
        using SKBitmap sampled = Downsample(original, maxDimension);
        return Analyze(sampled);
    }

    public static Color GetDominantColor(byte[] imageBytes, int maxDimension = 100)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);
        if (imageBytes.Length == 0)
        {
            return FallbackColor;
        }

        using SKBitmap original = SKBitmap.Decode(imageBytes)
            ?? throw new InvalidDataException("The artwork could not be decoded.");
        using SKBitmap sampled = Downsample(original, maxDimension);
        return Analyze(sampled);
    }

    private static SKBitmap ConvertToSkia(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return SKBitmap.Decode(stream)
            ?? throw new InvalidDataException("The artwork could not be decoded.");
    }

    private static SKBitmap Downsample(SKBitmap original, int maxDimension)
    {
        float scale = Math.Min(
            1f,
            (float)maxDimension / Math.Max(original.Width, original.Height));
        int width = Math.Max(1, (int)Math.Round(original.Width * scale));
        int height = Math.Max(1, (int)Math.Round(original.Height * scale));

        var resized = new SKBitmap(width, height);
        using var canvas = new SKCanvas(resized);
        canvas.DrawBitmap(
            original,
            SKRect.Create(original.Width, original.Height),
            SKRect.Create(width, height));
        return resized;
    }

    private static Color Analyze(SKBitmap bitmap)
    {
        var buckets = new Dictionary<int, ColorBucket>();
        double totalWeight = 0;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha < 128 || IsNeutralPadding(pixel))
                {
                    continue;
                }

                double weight = GetPixelWeight(x, y, bitmap.Width, bitmap.Height);
                int key = Quantize(pixel);
                if (!buckets.TryGetValue(key, out ColorBucket? bucket))
                {
                    bucket = new ColorBucket();
                    buckets.Add(key, bucket);
                }

                bucket.Add(pixel, weight);
                totalWeight += weight;
            }
        }

        if (buckets.Count == 0 || totalWeight <= 0)
        {
            return FallbackColor;
        }

        // Prefer lighter accents so theme text/icons stay readable on the dark UI.
        const double minReadableBrightness = 0.48;
        const double maxReadableBrightness = 0.88;

        var candidates = buckets.Values
            .Where(bucket => bucket.Weight / totalWeight >= 0.01)
            .Select(bucket =>
            {
                double brightness = bucket.AverageBrightness;
                // Reward mid-bright / light colours; soft-penalize near-black buckets.
                double brightnessScore = brightness < minReadableBrightness
                    ? brightness / minReadableBrightness * 0.35
                    : 0.35 + ((brightness - minReadableBrightness)
                        / (maxReadableBrightness - minReadableBrightness) * 0.65);
                brightnessScore = Math.Clamp(brightnessScore, 0, 1);

                return new
                {
                    Bucket = bucket,
                    Score = (bucket.Weight / totalWeight * 0.35)
                        + (bucket.AverageSaturation * 0.25)
                        + (brightnessScore * 0.40)
                };
            })
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();

        ColorBucket selected = candidates
            .FirstOrDefault(candidate =>
                candidate.Bucket.AverageBrightness is >= minReadableBrightness and <= maxReadableBrightness)
            ?.Bucket
            ?? candidates
                .OrderByDescending(candidate => candidate.Bucket.AverageBrightness)
                .First()
                .Bucket;

        byte red = (byte)Math.Clamp(Math.Round(selected.Red / selected.Weight), 0, 255);
        byte green = (byte)Math.Clamp(Math.Round(selected.Green / selected.Weight), 0, 255);
        byte blue = (byte)Math.Clamp(Math.Round(selected.Blue / selected.Weight), 0, 255);
        return EnsureReadableAccent(new Color(255, red, green, blue), minReadableBrightness);
    }

    /// <summary>
    /// Lifts a dark accent toward a readable brightness while keeping its hue.
    /// </summary>
    private static Color EnsureReadableAccent(Color color, double minBrightness)
    {
        double maxChannel = Math.Max(color.R, Math.Max(color.G, color.B)) / 255d;
        if (maxChannel >= minBrightness || maxChannel <= 0)
        {
            return color;
        }

        double scale = minBrightness / maxChannel;
        return new Color(
            255,
            (byte)Math.Clamp(Math.Round(color.R * scale), 0, 255),
            (byte)Math.Clamp(Math.Round(color.G * scale), 0, 255),
            (byte)Math.Clamp(Math.Round(color.B * scale), 0, 255));
    }

    // YouTube Music thumbs often use circular (-rj) art on white/black padding.
    private static bool IsNeutralPadding(SKColor color)
    {
        int maximum = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        int minimum = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        if (maximum - minimum < 28)
        {
            return true;
        }

        return maximum < 28 || minimum > 235;
    }

    private static int Quantize(SKColor color)
    {
        return ((color.Red >> 3) << 10)
            | ((color.Green >> 3) << 5)
            | (color.Blue >> 3);
    }

    private static double GetPixelWeight(int x, int y, int width, int height)
    {
        double centerX = (width - 1) / 2d;
        double centerY = (height - 1) / 2d;
        double distance = Math.Sqrt(
            Math.Pow(x - centerX, 2)
            + Math.Pow(y - centerY, 2));
        double maximum = Math.Sqrt(Math.Pow(centerX, 2) + Math.Pow(centerY, 2));
        // Stronger center bias helps circular YouTube Music artwork.
        return maximum <= 0 ? 1 : 1 - (distance / maximum * 0.65);
    }

    private sealed class ColorBucket
    {
        public double Weight { get; private set; }

        public double Red { get; private set; }

        public double Green { get; private set; }

        public double Blue { get; private set; }

        public double Saturation { get; private set; }

        public double Brightness { get; private set; }

        public double AverageSaturation => Saturation / Weight;

        public double AverageBrightness => Brightness / Weight;

        public void Add(SKColor color, double weight)
        {
            Weight += weight;
            Red += color.Red * weight;
            Green += color.Green * weight;
            Blue += color.Blue * weight;

            double maximum = Math.Max(color.Red, Math.Max(color.Green, color.Blue)) / 255d;
            double minimum = Math.Min(color.Red, Math.Min(color.Green, color.Blue)) / 255d;
            double saturation = maximum <= 0 ? 0 : (maximum - minimum) / maximum;
            Saturation += saturation * weight;
            Brightness += maximum * weight;
        }
    }
}
