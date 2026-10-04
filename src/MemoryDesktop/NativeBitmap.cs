using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace MemoryDesktop
{
    internal static class NativeBitmap
    {
        internal static Bitmap FromSource(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
            var bitmap = new Bitmap(source.PixelWidth, source.PixelHeight, PixelFormat.Format32bppPArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { converted.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Stride * bitmap.Height, data.Stride); }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
    }
}
