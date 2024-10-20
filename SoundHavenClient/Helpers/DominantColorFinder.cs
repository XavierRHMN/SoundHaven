using Avalonia.Media.Imaging;
using Avalonia.Media;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ColorMine.ColorSpaces;

namespace SoundHaven.Helpers
{
    public static class DominantColorFinder
    {
        public static Color GetDominantColor(Bitmap avaloniaBitmap)
        {
            using (SKBitmap skiaBitmap = ConvertAvaloniaToSkiaSharp(avaloniaBitmap))
            {
                Rgb dominantColor = AnalyzeDominantColor(skiaBitmap);
                return ConvertToAvaloniaColor(dominantColor);
            }
        }

        private static SKBitmap ConvertAvaloniaToSkiaSharp(Bitmap avaloniaBitmap)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                avaloniaBitmap.Save(memoryStream);
                memoryStream.Position = 0;
                return SKBitmap.Decode(memoryStream);
            }
        }

        private static Rgb AnalyzeDominantColor(SKBitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            Dictionary<Lab, ColorInfo> colorInfos = new Dictionary<Lab, ColorInfo>();
            double totalWeight = 0;

            for(int x = 0;x < width;x++)
            {
                for(int y = 0;y < height;y++)
                {
                    SKColor pixelColor = bitmap.GetPixel(x, y);
                    if (pixelColor.Alpha < 128) continue; // Skip fully transparent pixels

                    Rgb rgb = new Rgb { R = pixelColor.Red, G = pixelColor.Green, B = pixelColor.Blue };
                    Lab lab = rgb.To<Lab>();

                    double weight = GetPixelWeight(x, y, width, height);
                    totalWeight += weight;

                    double vibrancy = CalculateVibrancy(rgb);

                    if (colorInfos.ContainsKey(lab))
                    {
                        colorInfos[lab].Weight += weight;
                    }
                    else
                    {
                        colorInfos[lab] = new ColorInfo { Weight = weight, Vibrancy = vibrancy };
                    }
                }
            }

            // Filter out colors that occupy less than 5% of the image
            var significantColors = colorInfos.Where(pair => pair.Value.Weight / totalWeight > 0.05).ToList();

            if (!significantColors.Any())
            {
                significantColors = colorInfos.ToList(); // If no significant colors, use all colors
            }

            // Combine dominance and vibrancy scores
            var scoredColors = significantColors.Select(pair => new
            {
                Color = pair.Key,
                Score = (pair.Value.Weight / totalWeight) * 0.6 + pair.Value.Vibrancy * 0.4
            }).OrderByDescending(c => c.Score);

            Lab selectedLab = scoredColors.First().Color;

            // If the selected color is too light or dark, choose the next best option
            if (IsTooLightOrDark(selectedLab))
            {
                selectedLab = scoredColors.Where(c => !IsTooLightOrDark(c.Color))
                    .FirstOrDefault().Color;
            }

            return selectedLab.To<Rgb>();
        }

        private static double GetPixelWeight(int x, int y, int width, int height)
        {
            // Give more weight to pixels near the center of the image
            double distanceFromCenter = Math.Sqrt(Math.Pow(x - width / 2.0, 2) + Math.Pow(y - height / 2.0, 2));
            double maxDistance = Math.Sqrt(Math.Pow(width / 2.0, 2) + Math.Pow(height / 2.0, 2));
            return 1 - (distanceFromCenter / maxDistance);
        }

        private static bool IsTooLightOrDark(Lab color)
        {
            // Use the L value from Lab color space for a more accurate brightness assessment
            return color.L < 20 || color.L > 80;
        }

        private static double CalculateVibrancy(Rgb rgb)
        {
            // Convert RGB to HSV
            var hsv = rgb.To<Hsv>();

            // Calculate vibrancy based on saturation and value (brightness)
            double saturationWeight = 0.7;
            double valueWeight = 0.3;

            return (hsv.S / 100.0) * saturationWeight + (hsv.V / 100.0) * valueWeight;
        }

        private static Color ConvertToAvaloniaColor(Rgb rgb)
        {
            return new Color(255, (byte)rgb.R, (byte)rgb.G, (byte)rgb.B);
        }


        private class ColorInfo
        {
            public double Weight { get; set; }
            public double Vibrancy { get; set; }
        }
    }
}