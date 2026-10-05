using System;
using System.Collections.Generic;
using System.Windows;

namespace MemoryDesktop
{
    internal sealed class PhotoPlacement
    {
        internal Rect Base, Reservation;
        internal Rect Frame(Size photo, Size viewport, double age, double phase)
        {
            var area = new Rect(Base.X * viewport.Width, Base.Y * viewport.Height, Base.Width * viewport.Width, Base.Height * viewport.Height);
            double scale = Math.Min(area.Width / photo.Width, area.Height / photo.Height);
            double width = photo.Width * scale, height = photo.Height * scale;
            double dx = Math.Sin(age / 36 + phase) * Math.Min(12, viewport.Width * .008);
            double dy = Math.Cos(age / 42 + phase) * Math.Min(9, viewport.Height * .008);
            return new Rect(area.X + (area.Width - width) / 2 + dx, area.Y + (area.Height - height) / 2 + dy, width, height);
        }
    }

    internal static class PhotoLayout
    {
        // Normalized gutters cover maximum drift plus pixel rounding at small
        // sizes. Reservations remain valid through resizing and every fade phase.
        internal const double Margin = .025;
        internal static bool TryPlace(Size photo, Size viewport, IList<Rect> occupied, Random random, out PhotoPlacement placement)
        {
            placement = null;
            if (viewport.Width < 1 || viewport.Height < 1 || photo.Width < 1 || photo.Height < 1) return false;
            double bestScore = Double.NegativeInfinity;
            for (int attempt = 0; attempt < 96; attempt++)
            {
                double maxWidth = viewport.Width * (.29 + random.NextDouble() * .15);
                double maxHeight = viewport.Height * (.44 + random.NextDouble() * .22);
                double scale = Math.Min(maxWidth / photo.Width, maxHeight / photo.Height);
                double longest = Math.Max(photo.Width, photo.Height) * scale;
                double readable = Math.Min(viewport.Width, viewport.Height) * .43;
                if (longest < readable) scale *= readable / longest;
                double width = photo.Width * scale / viewport.Width, height = photo.Height * scale / viewport.Height;
                if (width > .48 || height > .70) continue;
                double x = .055 + random.NextDouble() * (.89 - width), y = .055 + random.NextDouble() * (.89 - height);
                var basis = new Rect(x, y, width, height);
                var reserve = basis; reserve.Inflate(Margin, Margin);
                if (!new Rect(0, 0, 1, 1).Contains(reserve)) continue;
                bool collision = false;
                foreach (var other in occupied) if (reserve.IntersectsWith(other)) { collision = true; break; }
                if (collision) continue;
                // Soft balance with substantial jitter, never columns or a grid.
                double cx = x + width / 2, cy = y + height / 2;
                foreach (var other in occupied) { cx += other.X + other.Width / 2; cy += other.Y + other.Height / 2; }
                cx /= occupied.Count + 1; cy /= occupied.Count + 1;
                double score = random.NextDouble() * .40 - (occupied.Count == 0 ? 0 : Math.Abs(cx - .5) + Math.Abs(cy - .5)) * .22;
                if (score > bestScore) { bestScore = score; placement = new PhotoPlacement { Base = basis, Reservation = reserve }; }
            }
            return placement != null;
        }
    }
}
