using Avalonia.Media.Imaging;
using Avalonia.Media;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class DominantColorFinder
{
    public static Color GetDominantColor(Bitmap avaloniaBitmap)
    {
        using (SKBitmap skiaBitmap = ConvertAvaloniaToSkiaSharp(avaloniaBitmap))
        {
            SKColor dominantSkColor = AnalyzeDominantColor(skiaBitmap);
            return ConvertToAvaloniaColor(dominantSkColor);
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

    private static SKColor AnalyzeDominantColor(SKBitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        
        Dictionary<SKColor, int> colorCounts = new Dictionary<SKColor, int>();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                SKColor pixelColor = bitmap.GetPixel(x, y);
                SKColor simplifiedColor = SimplifyColor(pixelColor);

                if (colorCounts.ContainsKey(simplifiedColor))
                {
                    colorCounts[simplifiedColor]++;
                }
                else
                {
                    colorCounts[simplifiedColor] = 1;
                }
            }
        }

        SKColor dominantColor = colorCounts.OrderByDescending(pair => pair.Value).First().Key;

        if (IsTooLightOrDark(dominantColor))
        {
            dominantColor = colorCounts.Where(pair => !IsTooLightOrDark(pair.Key))
                                       .OrderByDescending(pair => pair.Value)
                                       .First().Key;
        }

        return dominantColor;
    }

    private static SKColor SimplifyColor(SKColor color)
    {
        int factor = 32;
        return new SKColor(
            (byte)(Math.Round((float)color.Red / factor) * factor),
            (byte)(Math.Round((float)color.Green / factor) * factor),
            (byte)(Math.Round((float)color.Blue / factor) * factor)
        );
    }

    private static bool IsTooLightOrDark(SKColor color)
    {
        float brightness = (color.Red * 0.299f + color.Green * 0.587f + color.Blue * 0.114f) / 255f;
        return brightness < 0.2f || brightness > 0.8f;
    }

    private static Color ConvertToAvaloniaColor(SKColor skColor)
    {
        return new Color(skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue);
    }
}