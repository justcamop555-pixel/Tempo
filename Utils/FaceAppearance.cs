using System;
using System.Drawing;

namespace AutoClicker.Utils
{
    /// <summary>
    /// What one face looks like, compactly: enough to tell whether a face that appears after a camera
    /// cut is someone already seen. Measured on the face analyzer's own 640-wide frame, so it costs a
    /// few thousand pixel reads per face.
    /// </summary>
    public sealed class FaceLook
    {
        /// <summary>The face box's brightness pattern, TemplateSize², zero-mean and unit length.</summary>
        public float[] Template;
        /// <summary>Skin in the middle of the face: chromaticity (r, g of r+g+b) and brightness 0–1.</summary>
        public float SkinR, SkinG, SkinL;
        /// <summary>Mean colour 0–1 of the band above the face (hair, or what is behind a short cut).</summary>
        public float TopR, TopG, TopB;
        /// <summary>Mean colour 0–1 of the strips beside the face (long hair falls there).</summary>
        public float SideR, SideG, SideB;
        /// <summary>Hue/saturation/value histogram of the head and what is around it, summing to 1.</summary>
        public float[] Hist;
        /// <summary>Where the face is and how big: centre and width as fractions of the frame.</summary>
        public float Cx, Cy, Width;
        /// <summary>How many measurements are averaged in.</summary>
        public int Samples;

        public FaceLook Clone()
        {
            var c = (FaceLook)MemberwiseClone();
            c.Template = (float[])Template?.Clone();
            c.Hist = (float[])Hist?.Clone();
            return c;
        }
    }

    /// <summary>
    /// Measures and compares <see cref="FaceLook"/>s. Every similarity is higher for more alike.
    ///
    /// Tuned on real footage (scratchpad/facereid): a public-domain NASA crew news conference in which
    /// all four astronauts wear the same red polo in front of the same backdrop, filmed in close-ups
    /// framed alike and in wide shots, with the audience in between — 609 cross-shot pairs of faces
    /// labelled by eye to tune on, and 354 from another stretch of the video to check against.
    /// How well each measure told the same person from different people (0.5 = chance, 1 = always):
    ///   head colour histogram 0.98 / 0.96 · skin colour 0.91 / 0.88 · colour above the face
    ///   0.90 / 0.83 · the face's brightness pattern 0.87 / 0.77 · place on screen 0.80 / 0.84.
    /// <see cref="MatchScore"/> uses the first two, which together passed NO pair of different people
    /// on either stretch. Place on screen is deliberately left out: it was perfect between shots from
    /// the same camera and wrong six times when a second camera put one astronaut where another sat.
    /// </summary>
    public static class FaceAppearance
    {
        public const int TemplateSize = 20;
        private const int HueBins = 8, SatBins = 3, GreyBins = 4;
        public const int HistBins = HueBins * SatBins + GreyBins;

        /// <summary>
        /// Faces at least this wide (a fraction of the frame; 40 px of the analyzer's 640) are compared
        /// as close-ups. A close-up and a wide-shot face of one person look different enough — the
        /// head's surroundings change entirely — that the two are only ever compared under a stricter rule.
        /// </summary>
        public const float BigFace = 40f / 640f;

        public static bool IsBig(FaceLook look) => look != null && look.Width >= BigFace;

        /// <summary>
        /// How surely <paramref name="probe"/> is the person <paramref name="known"/> describes: a score
        /// (higher is surer) when it passes the rule for their sizes, NaN when it does not.
        ///   close-up ↔ close-up, small ↔ small: head colours ≥ 0.93 alike and skin within 0.07 / 0.09;
        ///   close-up ↔ small: ≥ 0.95 and within 0.06 — measured, the two sizes overlap, so this passes
        ///   few true pairs (8 of 56) but no false one.
        /// </summary>
        public static double MatchScore(FaceLook known, FaceLook probe)
        {
            if (known == null || probe == null) { return double.NaN; }
            bool bigK = IsBig(known), bigP = IsBig(probe);
            double hist = HistSimilarity(known, probe), skin = SkinSimilarity(known, probe);
            bool pass = bigK && bigP ? hist >= 0.93 && skin >= -0.07
                      : !bigK && !bigP ? hist >= 0.93 && skin >= -0.09
                      : hist >= 0.95 && skin >= -0.06;
            return pass ? hist + skin : double.NaN;
        }

        /// <summary>
        /// Measures a face. <paramref name="bgr"/> is the frame's 24bpp rows (<paramref name="stride"/>
        /// bytes apart), <paramref name="gray"/> its luma, <paramref name="w"/>×<paramref name="h"/> its
        /// size, <paramref name="box"/> the detector's face box in the same pixels. Null when the box is
        /// mostly outside the frame.
        /// </summary>
        public static FaceLook Measure(byte[] bgr, int stride, byte[] gray, int w, int h, RectangleF box)
        {
            if (bgr == null || gray == null || w <= 0 || h <= 0 || box.Width < 4 || box.Height < 4) { return null; }
            var inside = RectangleF.Intersect(box, new RectangleF(0, 0, w, h));
            if (inside.Width * inside.Height < box.Width * box.Height * 0.6f) { return null; }

            var look = new FaceLook
            {
                Template = TemplateOf(gray, w, h, box),
                Hist = HistOf(bgr, stride, w, h, Grow(box, 0.3f, 0.45f, 0.3f, 0.2f)),
                Cx = (box.X + box.Width / 2) / w,
                Cy = (box.Y + box.Height / 2) / h,
                Width = box.Width / w,
                Samples = 1,
            };
            MeanColour(bgr, stride, w, h, Rect(box, 0.3f, 0.4f, 0.7f, 0.8f), out float r, out float g, out float b);
            float sum = r + g + b;
            look.SkinR = sum > 0 ? r / sum : 0.333f;
            look.SkinG = sum > 0 ? g / sum : 0.333f;
            look.SkinL = (r * 77 + g * 150 + b * 29) / 256f;
            MeanColour(bgr, stride, w, h, Rect(box, 0.2f, -0.35f, 0.8f, -0.02f), out look.TopR, out look.TopG, out look.TopB);
            MeanColour2(bgr, stride, w, h, Rect(box, -0.3f, 0.3f, -0.05f, 1.1f), Rect(box, 1.05f, 0.3f, 1.3f, 1.1f),
                        out look.SideR, out look.SideG, out look.SideB);
            return look;
        }

        /// <summary>Folds <paramref name="sample"/> into <paramref name="into"/> as a running average.</summary>
        public static void Accumulate(FaceLook into, FaceLook sample)
        {
            if (into == null || sample == null) { return; }
            // Up to 20 samples weigh equally; after that each new one keeps a 1/20 say, so the look
            // follows slow changes (lighting, a turned head) without forgetting the person.
            float k = 1f / Math.Min(20, into.Samples + 1);
            for (int i = 0; i < into.Template.Length; i++) { into.Template[i] += (sample.Template[i] - into.Template[i]) * k; }
            Normalise(into.Template);
            for (int i = 0; i < into.Hist.Length; i++) { into.Hist[i] += (sample.Hist[i] - into.Hist[i]) * k; }
            into.SkinR += (sample.SkinR - into.SkinR) * k;
            into.SkinG += (sample.SkinG - into.SkinG) * k;
            into.SkinL += (sample.SkinL - into.SkinL) * k;
            into.TopR += (sample.TopR - into.TopR) * k;
            into.TopG += (sample.TopG - into.TopG) * k;
            into.TopB += (sample.TopB - into.TopB) * k;
            into.SideR += (sample.SideR - into.SideR) * k;
            into.SideG += (sample.SideG - into.SideG) * k;
            into.SideB += (sample.SideB - into.SideB) * k;
            // Where it is: the latest, not an average — a face that moved is where it is now.
            into.Cx = sample.Cx; into.Cy = sample.Cy; into.Width = sample.Width;
            into.Samples++;
        }

        // ── Similarities (higher = more alike) ──────────────────────────────

        /// <summary>
        /// Normalised cross-correlation of the two brightness patterns, −1 to 1, the best over a shift of
        /// one cell each way — the detector's box wobbles by a few pixels from frame to frame.
        /// </summary>
        public static double TemplateSimilarity(FaceLook a, FaceLook b)
        {
            if (a?.Template == null || b?.Template == null) { return 0; }
            double best = -1;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    best = Math.Max(best, ShiftedNcc(a.Template, b.Template, dx, dy));
                }
            }
            return best;
        }

        /// <summary>Skin colour and brightness distance, negated (0 = identical).</summary>
        public static double SkinSimilarity(FaceLook a, FaceLook b)
        {
            double dr = a.SkinR - b.SkinR, dg = a.SkinG - b.SkinG, dl = a.SkinL - b.SkinL;
            return -Math.Sqrt(dr * dr * 25 + dg * dg * 25 + dl * dl);
        }

        public static double TopSimilarity(FaceLook a, FaceLook b)
        {
            double dr = a.TopR - b.TopR, dg = a.TopG - b.TopG, db = a.TopB - b.TopB;
            return -Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        public static double SideSimilarity(FaceLook a, FaceLook b)
        {
            double dr = a.SideR - b.SideR, dg = a.SideG - b.SideG, db = a.SideB - b.SideB;
            return -Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        /// <summary>
        /// Bhattacharyya coefficient of the two head histograms, 0 to 1. The head's SURROUNDINGS carry
        /// much of its power (hair, the set behind a seat): a histogram of the inside of the face box
        /// alone was measured too, and told people apart across sizes no better than chance (0.54 / 0.47).
        /// </summary>
        public static double HistSimilarity(FaceLook a, FaceLook b)
        {
            if (a?.Hist == null || b?.Hist == null) { return 0; }
            double s = 0;
            for (int i = 0; i < a.Hist.Length; i++) { s += Math.Sqrt(Math.Max(0, a.Hist[i]) * Math.Max(0, b.Hist[i])); }
            return s;
        }

        /// <summary>How far apart on screen, in frame widths, negated.</summary>
        public static double PlaceSimilarity(FaceLook a, FaceLook b)
        {
            double dx = a.Cx - b.Cx, dy = a.Cy - b.Cy;
            return -Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>How different in size (log ratio), negated.</summary>
        public static double SizeSimilarity(FaceLook a, FaceLook b)
        {
            if (a.Width <= 0 || b.Width <= 0) { return -10; }
            return -Math.Abs(Math.Log(a.Width / b.Width));
        }

        // ── Measuring ───────────────────────────────────────────────────────

        private static RectangleF Rect(RectangleF box, float x0, float y0, float x1, float y1)
        {
            return RectangleF.FromLTRB(box.X + box.Width * x0, box.Y + box.Height * y0,
                                       box.X + box.Width * x1, box.Y + box.Height * y1);
        }

        private static RectangleF Grow(RectangleF box, float left, float top, float right, float bottom)
        {
            return RectangleF.FromLTRB(box.X - box.Width * left, box.Y - box.Height * top,
                                       box.Right + box.Width * right, box.Bottom + box.Height * bottom);
        }

        private static float[] TemplateOf(byte[] gray, int w, int h, RectangleF box)
        {
            const int n = TemplateSize;
            var t = new float[n * n];
            double cw = box.Width / n, ch = box.Height / n;
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    // The mean over the cell (at least one pixel), clamped to the frame.
                    int x0 = (int)Math.Floor(box.X + i * cw), x1 = Math.Max(x0 + 1, (int)Math.Ceiling(box.X + (i + 1) * cw));
                    int y0 = (int)Math.Floor(box.Y + j * ch), y1 = Math.Max(y0 + 1, (int)Math.Ceiling(box.Y + (j + 1) * ch));
                    x0 = Clamp(x0, 0, w - 1); x1 = Clamp(x1, x0 + 1, w);
                    y0 = Clamp(y0, 0, h - 1); y1 = Clamp(y1, y0 + 1, h);
                    long sum = 0; int cnt = 0;
                    for (int y = y0; y < y1; y++)
                    {
                        int off = y * w;
                        for (int x = x0; x < x1; x++) { sum += gray[off + x]; cnt++; }
                    }
                    t[j * n + i] = cnt > 0 ? (float)sum / cnt : 0;
                }
            }
            double mean = 0;
            for (int i = 0; i < t.Length; i++) { mean += t[i]; }
            mean /= t.Length;
            for (int i = 0; i < t.Length; i++) { t[i] -= (float)mean; }
            Normalise(t);
            return t;
        }

        private static void Normalise(float[] v)
        {
            double ss = 0;
            for (int i = 0; i < v.Length; i++) { ss += v[i] * v[i]; }
            double norm = Math.Sqrt(ss);
            if (norm < 1e-6) { return; }
            for (int i = 0; i < v.Length; i++) { v[i] = (float)(v[i] / norm); }
        }

        private static double ShiftedNcc(float[] a, float[] b, int dx, int dy)
        {
            const int n = TemplateSize;
            double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
            int cnt = 0;
            for (int j = 1; j < n - 1; j++)
            {
                int jb = j + dy;
                if (jb < 0 || jb >= n) { continue; }
                for (int i = 1; i < n - 1; i++)
                {
                    int ib = i + dx;
                    if (ib < 0 || ib >= n) { continue; }
                    double va = a[j * n + i], vb = b[jb * n + ib];
                    sa += va; sb += vb; saa += va * va; sbb += vb * vb; sab += va * vb; cnt++;
                }
            }
            if (cnt < 4) { return -1; }
            double cov = sab - sa * sb / cnt, va2 = saa - sa * sa / cnt, vb2 = sbb - sb * sb / cnt;
            if (va2 <= 1e-12 || vb2 <= 1e-12) { return 0; }
            return cov / Math.Sqrt(va2 * vb2);
        }

        private static void MeanColour(byte[] bgr, int stride, int w, int h, RectangleF r, out float red, out float green, out float blue)
        {
            long sr = 0, sg = 0, sb = 0; int n = 0;
            Accumulate(bgr, stride, w, h, r, ref sr, ref sg, ref sb, ref n);
            red = n > 0 ? sr / (255f * n) : 0;
            green = n > 0 ? sg / (255f * n) : 0;
            blue = n > 0 ? sb / (255f * n) : 0;
        }

        private static void MeanColour2(byte[] bgr, int stride, int w, int h, RectangleF r1, RectangleF r2,
                                        out float red, out float green, out float blue)
        {
            long sr = 0, sg = 0, sb = 0; int n = 0;
            Accumulate(bgr, stride, w, h, r1, ref sr, ref sg, ref sb, ref n);
            Accumulate(bgr, stride, w, h, r2, ref sr, ref sg, ref sb, ref n);
            red = n > 0 ? sr / (255f * n) : 0;
            green = n > 0 ? sg / (255f * n) : 0;
            blue = n > 0 ? sb / (255f * n) : 0;
        }

        private static void Accumulate(byte[] bgr, int stride, int w, int h, RectangleF r,
                                       ref long sr, ref long sg, ref long sb, ref int n)
        {
            int x0 = Clamp((int)r.Left, 0, w), x1 = Clamp((int)Math.Ceiling(r.Right), 0, w);
            int y0 = Clamp((int)r.Top, 0, h), y1 = Clamp((int)Math.Ceiling(r.Bottom), 0, h);
            for (int y = y0; y < y1; y++)
            {
                int p = y * stride + x0 * 3;
                for (int x = x0; x < x1; x++, p += 3)
                {
                    sb += bgr[p]; sg += bgr[p + 1]; sr += bgr[p + 2]; n++;
                }
            }
        }

        private static float[] HistOf(byte[] bgr, int stride, int w, int h, RectangleF r)
        {
            var hist = new float[HistBins];
            int x0 = Clamp((int)r.Left, 0, w), x1 = Clamp((int)Math.Ceiling(r.Right), 0, w);
            int y0 = Clamp((int)r.Top, 0, h), y1 = Clamp((int)Math.Ceiling(r.Bottom), 0, h);
            int n = 0;
            for (int y = y0; y < y1; y++)
            {
                int p = y * stride + x0 * 3;
                for (int x = x0; x < x1; x++, p += 3)
                {
                    int b = bgr[p], g = bgr[p + 1], rr = bgr[p + 2];
                    int max = Math.Max(rr, Math.Max(g, b)), min = Math.Min(rr, Math.Min(g, b));
                    double v = max / 255.0, s = max == 0 ? 0 : (max - min) / (double)max;
                    int bin;
                    if (s < 0.2 || v < 0.12)
                    {
                        bin = HueBins * SatBins + Math.Min(GreyBins - 1, (int)(v * GreyBins));
                    }
                    else
                    {
                        double hue;
                        int d = max - min;
                        if (max == rr) { hue = ((g - b) / (double)d + 6) % 6; }
                        else if (max == g) { hue = (b - rr) / (double)d + 2; }
                        else { hue = (rr - g) / (double)d + 4; }
                        int hb = Math.Min(HueBins - 1, (int)(hue / 6.0 * HueBins));
                        int sbin = Math.Min(SatBins - 1, (int)((s - 0.2) / 0.8 * SatBins));
                        bin = hb * SatBins + sbin;
                    }
                    hist[bin]++;
                    n++;
                }
            }
            if (n > 0) { for (int i = 0; i < hist.Length; i++) { hist[i] /= n; } }
            return hist;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
