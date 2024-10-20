using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace SoundHaven.Helpers
{
    public class DominantColorFinder
    {
        public static Color GetDominantColor(byte[] imageData, int width, int height)
        {
            var colorCounts = new Dictionary<Color, double>();
            int totalPixels = 0;
            int skippedPixels = 0;

            unsafe
            {
                fixed (byte* pixelData = imageData)
                {
                    int stride = width * 4; // Assuming 4 bytes per pixel (BGRA)

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            totalPixels++;
                            int index = y * stride + x * 4;

                            // Read the pixel data according to RGBA format
                            byte a = pixelData[index];
                            byte r = pixelData[index + 1];
                            byte g = pixelData[index + 2];
                            byte b = pixelData[index + 3];

                            if (a < 128)
                            {
                                skippedPixels++;
                                continue;
                            }
                            
                            // Skip very dark pixels
                            if (r < 30 && g < 30 && b < 30)
                            {
                                skippedPixels++;
                                continue;
                            }

                            Color pixelColor = Color.FromArgb(a, r, g, b);
                            Color quantizedColor = QuantizeColor(pixelColor);

                            double weight = CalculateColorWeight(quantizedColor);

                            if (colorCounts.ContainsKey(quantizedColor))
                                colorCounts[quantizedColor] += weight;
                            else
                                colorCounts[quantizedColor] = weight;
                        }
                    }
                }
            }

            // Log diagnostic information
            Console.WriteLine($"Total pixels: {totalPixels}");
            Console.WriteLine($"Skipped pixels: {skippedPixels}");
            Console.WriteLine($"Unique colors: {colorCounts.Count}");

            // Get the top 5 colors
            var topColors = colorCounts.OrderByDescending(pair => pair.Value).Take(5).ToList();

            // Log top 5 colors
            for (int i = 0; i < topColors.Count; i++)
            {
                var color = topColors[i].Key;
                Console.WriteLine($"Top color {i + 1}: R={color.R}, G={color.G}, B={color.B}, Weight={topColors[i].Value}");
            }

            // Get the color with the highest weighted count
            var dominantColor = topColors.First().Key;

            // If no suitable color found, return a default color
            if (dominantColor == default(Color))
            {
                Console.WriteLine("No dominant color found, using fallback.");
                return Color.FromRgb(200, 200, 200); // Light gray as fallback
            }

            var adjustedColor = AdjustColorBrightness(dominantColor);
            Console.WriteLine($"Dominant color: R={dominantColor.R}, G={dominantColor.G}, B={dominantColor.B}");
            Console.WriteLine($"Adjusted color: R={adjustedColor.R}, G={adjustedColor.G}, B={adjustedColor.B}");

            return adjustedColor;
        }

        private static Color QuantizeColor(Color c)
        {
            // Finer quantization
            byte r = (byte)((c.R / 16) * 16);
            byte g = (byte)((c.G / 16) * 16);
            byte b = (byte)((c.B / 16) * 16);
            return Color.FromArgb(c.A, r, g, b);
        }

        private static double CalculateColorWeight(Color c)
        {
            // Calculate color weight based on saturation, value, and a bias towards warmer colors
            double max = Math.Max(c.R, Math.Max(c.G, c.B));
            double min = Math.Min(c.R, Math.Min(c.G, c.B));
            double saturation = (max == 0) ? 0 : (max - min) / max;
            double value = max / 255.0;

            // Add a slight bias towards warmer colors (reds and yellows)
            double warmBias = (c.R > c.B) ? 1.2 : 1.0;

            return saturation * value * warmBias;
        }

        private static Color AdjustColorBrightness(Color c)
        {
            // Ensure the color is vivid enough
            double brightness = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
            if (brightness < 0.3) // Increased the threshold
            {
                double factor = Math.Min(2, 0.6 / brightness); // Increased the target brightness
                return Color.FromRgb(
                    (byte)Math.Min(255, c.R * factor),
                    (byte)Math.Min(255, c.G * factor),
                    (byte)Math.Min(255, c.B * factor)
                );
            }
            return c;
        }
    }
}