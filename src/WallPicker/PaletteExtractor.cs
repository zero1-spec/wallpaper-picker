using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WallPicker;

public static class PaletteExtractor
{
    public static readonly string[] Labels =
    {
        "PRIMARY",
        "SECONDARY",
        "TERTIARY",
        "CONTAINER",
        "SURFACE"
    };

    private const int SampleGrid = 36;

    private struct Bucket
    {
        public int Count;
        public int SumR;
        public int SumG;
        public int SumB;
    }

    public static string[] Extract(BitmapSource source)
    {
        if (source is null || source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return DefaultPalette();
        }

        BitmapSource bgra = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Bgr32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int srcW = bgra.PixelWidth;
        int srcH = bgra.PixelHeight;
        int stride = srcW * 4;
        byte[] rowBuffer = new byte[stride];

        var buckets = new Dictionary<int, Bucket>();
        long totalR = 0;
        long totalG = 0;
        long totalB = 0;

        for (int y = 0; y < SampleGrid; y++)
        {
            int sy = (int)((long)y * srcH / SampleGrid);
            if (sy >= srcH) sy = srcH - 1;
            bgra.CopyPixels(new Int32Rect(0, sy, srcW, 1), rowBuffer, stride, 0);

            for (int x = 0; x < SampleGrid; x++)
            {
                int sx = (int)((long)x * srcW / SampleGrid);
                if (sx >= srcW) sx = srcW - 1;
                int offset = sx * 4;

                byte b = rowBuffer[offset];
                byte g = rowBuffer[offset + 1];
                byte r = rowBuffer[offset + 2];

                totalR += r;
                totalG += g;
                totalB += b;

                int key = ((r >> 5) << 6) | ((g >> 5) << 3) | (b >> 5);
                if (buckets.TryGetValue(key, out Bucket bucket))
                {
                    bucket.Count++;
                    bucket.SumR += r;
                    bucket.SumG += g;
                    bucket.SumB += b;
                    buckets[key] = bucket;
                }
                else
                {
                    buckets[key] = new Bucket { Count = 1, SumR = r, SumG = g, SumB = b };
                }
            }
        }

        if (buckets.Count == 0)
        {
            return DefaultPalette();
        }

        var cands = new List<(double W, double H, double L, double S)>(buckets.Count);
        foreach (Bucket bucket in buckets.Values)
        {
            double cnt = bucket.Count;
            (double h, double l, double s) = RgbToHls(
                bucket.SumR / cnt / 255.0,
                bucket.SumG / cnt / 255.0,
                bucket.SumB / cnt / 255.0);

            double w = cnt * (0.2 + s) * (l > 0.1 && l < 0.92 ? 1.0 : 0.35);
            cands.Add((w, h, l, s));
        }

        cands.Sort((a, b) =>
        {
            int cmp = b.W.CompareTo(a.W);
            if (cmp != 0) return cmp;
            cmp = b.H.CompareTo(a.H);
            if (cmp != 0) return cmp;
            cmp = b.L.CompareTo(a.L);
            if (cmp != 0) return cmp;
            return b.S.CompareTo(a.S);
        });

        double h1 = cands[0].H;
        double s1 = cands[0].S;
        double h3 = Mod1(h1 + 0.14);
        double s3 = s1;

        for (int i = 1; i < cands.Count; i++)
        {
            double h = cands[i].H;
            double s = cands[i].S;
            double dh = Math.Abs(h - h1);
            dh = Math.Min(dh, 1.0 - dh);
            if (dh > 0.10 && s > 0.2)
            {
                h3 = h;
                s3 = s;
                break;
            }
        }

        const double total = SampleGrid * SampleGrid;
        (double avgH, _, _) = RgbToHls(
            totalR / total / 255.0,
            totalG / total / 255.0,
            totalB / total / 255.0);

        double sat = Math.Min(Math.Max(s1, 0.08), 0.9);

        return new[]
        {
            HlsHex(h1, 0.78, Math.Min(0.95, sat * 1.25 + 0.05)),
            HlsHex(h1, 0.76, sat * 0.35),
            HlsHex(h3, 0.74, Math.Min(0.8, Math.Max(0.12, s3 * 1.1))),
            HlsHex(h1, 0.22, Math.Min(0.85, sat * 0.9)),
            HlsHex(avgH, 0.17, 0.10)
        };
    }

    public static bool IsLightHex(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 7 || hex[0] != '#')
        {
            return false;
        }

        int r = Convert.ToInt32(hex.Substring(1, 2), 16);
        int g = Convert.ToInt32(hex.Substring(3, 2), 16);
        int b = Convert.ToInt32(hex.Substring(5, 2), 16);
        double lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
        return lum > 0.55;
    }

    private static string[] DefaultPalette()
    {
        return new[] { "#5B8CFF", "#8FA8E0", "#A68BFF", "#1F2B47", "#27292E" };
    }

    private static string HlsHex(double h, double l, double s)
    {
        (double r, double g, double b) = HlsToRgb(
            Mod1(h),
            Math.Clamp(l, 0.0, 1.0),
            Math.Clamp(s, 0.0, 1.0));

        int ri = (int)(r * 255.0 + 0.5);
        int gi = (int)(g * 255.0 + 0.5);
        int bi = (int)(b * 255.0 + 0.5);
        return $"#{ri:X2}{gi:X2}{bi:X2}";
    }

    private static (double H, double L, double S) RgbToHls(double r, double g, double b)
    {
        double maxc = Math.Max(r, Math.Max(g, b));
        double minc = Math.Min(r, Math.Min(g, b));
        double sumc = maxc + minc;
        double rangec = maxc - minc;
        double l = sumc / 2.0;

        if (minc == maxc)
        {
            return (0.0, l, 0.0);
        }

        double s = l <= 0.5
            ? rangec / sumc
            : rangec / (2.0 - maxc - minc);

        double rc = (maxc - r) / rangec;
        double gc = (maxc - g) / rangec;
        double bc = (maxc - b) / rangec;

        double h;
        if (r == maxc)
        {
            h = bc - gc;
        }
        else if (g == maxc)
        {
            h = 2.0 + rc - bc;
        }
        else
        {
            h = 4.0 + gc - rc;
        }

        return (Mod1(h / 6.0), l, s);
    }

    private static (double R, double G, double B) HlsToRgb(double h, double l, double s)
    {
        if (s == 0.0)
        {
            return (l, l, l);
        }

        double m2 = l <= 0.5
            ? l * (1.0 + s)
            : l + s - l * s;
        double m1 = 2.0 * l - m2;

        const double oneThird = 1.0 / 3.0;
        return (
            HueToChannel(m1, m2, h + oneThird),
            HueToChannel(m1, m2, h),
            HueToChannel(m1, m2, h - oneThird));
    }

    private static double HueToChannel(double m1, double m2, double hue)
    {
        const double oneSixth = 1.0 / 6.0;
        const double twoThirds = 2.0 / 3.0;

        hue = Mod1(hue);
        if (hue < oneSixth)
        {
            return m1 + (m2 - m1) * hue * 6.0;
        }
        if (hue < 0.5)
        {
            return m2;
        }
        if (hue < twoThirds)
        {
            return m1 + (m2 - m1) * (twoThirds - hue) * 6.0;
        }
        return m1;
    }

    private static double Mod1(double x)
    {
        double r = x - Math.Floor(x);
        return r >= 1.0 ? 0.0 : r;
    }
}