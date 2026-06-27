using System.Globalization;
using System.Text;

namespace SimCoach.Reference;

/// <summary>
/// Renders a static, self-contained HTML/SVG one-glance review page for a baked
/// <see cref="CornerGeometryDocument"/> (ADR-0014): the aggregate centerline path with an apex marker
/// per corner and a table of the baked values. This is the human gate read before committing
/// cornerGeometry.json — there is no full-auto accept.
/// </summary>
public static class CornerGeometryReviewPage
{
    /// <summary>Renders the review page as an HTML string.</summary>
    public static string Render(CornerGeometryDocument document, MedianCenterline centerline)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(centerline);

        IReadOnlyList<CenterlineBin> bins = centerline.Bins;
        float minX = 0f;
        float maxX = 1f;
        float minZ = 0f;
        float maxZ = 1f;
        if (bins.Count > 0)
        {
            minX = float.MaxValue;
            maxX = float.MinValue;
            minZ = float.MaxValue;
            maxZ = float.MinValue;
            foreach (CenterlineBin bin in bins)
            {
                minX = MathF.Min(minX, bin.X);
                maxX = MathF.Max(maxX, bin.X);
                minZ = MathF.Min(minZ, bin.Z);
                maxZ = MathF.Max(maxZ, bin.Z);
            }
        }

        const float size = 1000f;
        const float pad = 40f;
        float span = MathF.Max(MathF.Max(maxX - minX, maxZ - minZ), 1f);
        float scale = (size - (2f * pad)) / span;

        StringBuilder svg = new();
        svg.Append("<polyline fill=\"none\" stroke=\"#888\" stroke-width=\"2\" points=\"");
        foreach (CenterlineBin bin in bins)
        {
            svg.Append(Num(MapX(bin.X, minX, pad, scale)))
               .Append(',')
               .Append(Num(MapY(bin.Z, maxZ, pad, scale)))
               .Append(' ');
        }

        svg.Append("\"/>");
        foreach (CornerGeometryEntry corner in document.Corners)
        {
            if (bins.Count == 0)
            {
                break;
            }

            CenterlineBin apex = NearestBin(bins, corner.ApexPosition * document.LapLengthM);
            float cx = MapX(apex.X, minX, pad, scale);
            float cy = MapY(apex.Z, maxZ, pad, scale);
            svg.Append("<circle cx=\"").Append(Num(cx)).Append("\" cy=\"").Append(Num(cy)).Append("\" r=\"7\" fill=\"#d33\"/>");
            svg.Append("<text x=\"").Append(Num(cx + 9f)).Append("\" y=\"").Append(Num(cy)).Append("\" font-size=\"14\">").Append(Escape(corner.Id)).Append("</text>");
        }

        StringBuilder rows = new();
        foreach (CornerGeometryEntry corner in document.Corners)
        {
            rows.Append("<tr><td>").Append(Escape(corner.Id))
                .Append("</td><td>").Append(Num(corner.StartPosition))
                .Append("</td><td>").Append(Num(corner.ApexPosition))
                .Append("</td><td>").Append(Num(corner.EndPosition))
                .Append("</td><td>").Append(Num(corner.ApexRadiusM))
                .Append("</td><td>").Append(Num(corner.PeakLateralG))
                .Append("</td><td>").Append(Escape(corner.Trigger))
                .Append("</td></tr>");
        }

        StringBuilder html = new();
        html.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>cornerGeometry: ").Append(Escape(document.TrackId)).Append("</title></head><body>");
        html.Append("<h1>").Append(Escape(document.TrackId)).Append(" — ").Append(document.Corners.Count).Append(" corners</h1>");
        html.Append("<p>laps: ").Append(document.LapCount).Append(", lap length: ").Append(Num(document.LapLengthM)).Append(" m, source: ").Append(Escape(document.SourceRecording ?? "n/a")).Append("</p>");
        html.Append("<svg viewBox=\"0 0 ").Append(Num(size)).Append(' ').Append(Num(size)).Append("\" width=\"700\" height=\"700\">").Append(svg).Append("</svg>");
        html.Append("<table border=\"1\" cellpadding=\"4\"><tr><th>id</th><th>start</th><th>apex</th><th>end</th><th>R (m)</th><th>peak |G|</th><th>trigger</th></tr>").Append(rows).Append("</table>");
        html.Append("</body></html>");
        return html.ToString();
    }

    private static float MapX(float x, float minX, float pad, float scale) => pad + ((x - minX) * scale);

    private static float MapY(float z, float maxZ, float pad, float scale) => pad + ((maxZ - z) * scale);

    private static CenterlineBin NearestBin(IReadOnlyList<CenterlineBin> bins, float distanceM)
    {
        CenterlineBin best = bins[0];
        float bestDelta = float.MaxValue;
        foreach (CenterlineBin bin in bins)
        {
            float delta = MathF.Abs(bin.DistanceM - distanceM);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = bin;
            }
        }

        return best;
    }

    private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
