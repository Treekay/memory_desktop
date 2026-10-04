using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MemoryDesktop
{
    // Retained premultiplied DIBs use Windows' optimized constant-alpha blend.
    // GDI+ color matrices over full desktop images are prohibitively slow here.
    internal sealed class NativeImage : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
        { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int XResolution, YResolution; public uint UsedColors, ImportantColors; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend
        { public byte Operation, Flags, Alpha, Format; }
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
        [DllImport("msimg32.dll")] private static extern bool AlphaBlend(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, int sourceWidth, int sourceHeight, Blend blend);
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] private static extern void CopyMemory(IntPtr target, IntPtr source, UIntPtr count);
        private IntPtr dc, bitmap, previous;
        private readonly int width, height;
        internal NativeImage(int imageWidth, int imageHeight)
        {
            width = imageWidth; height = imageHeight;
            dc = CreateCompatibleDC(IntPtr.Zero);
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32, ImageSize = (uint)(width * height * 4) };
            IntPtr bits; bitmap = CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0);
            pixelBuffer = bits;
            if (dc == IntPtr.Zero || bitmap == IntPtr.Zero) { Dispose(); throw new InvalidOperationException("无法分配桌面图像。"); }
            previous = SelectObject(dc, bitmap);
        }
        private IntPtr pixelBuffer;
        internal NativeImage(Bitmap image) : this(image.Width, image.Height)
        {
            var data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { for (int row = 0; row < height; row++) CopyMemory(IntPtr.Add(pixelBuffer, row * width * 4), IntPtr.Add(data.Scan0, row * data.Stride), new UIntPtr((uint)(width * 4))); }
            finally { image.UnlockBits(data); }
        }
        internal void Compose(NativeImage basis, NativeImage glow, int x, int y, byte alpha)
        {
            BitBlt(dc, 0, 0, width, height, basis.dc, 0, 0, 0x00CC0020);
            AlphaBlend(dc, x, y, glow.width, glow.height, glow.dc, 0, 0, glow.width, glow.height, new Blend { Alpha = alpha, Format = 1 });
        }
        internal void Draw(Graphics graphics, int x, int y, double opacity, bool opaque)
        {
            IntPtr target = graphics.GetHdc();
            try
            {
                if (opaque) BitBlt(target, x, y, width, height, dc, 0, 0, 0x00CC0020);
                else AlphaBlend(target, x, y, width, height, dc, 0, 0, width, height, new Blend { Alpha = (byte)Math.Round(Math.Max(0, Math.Min(1, opacity)) * 255), Format = 1 });
            }
            finally { graphics.ReleaseHdc(target); }
        }
        public void Dispose()
        {
            if (dc != IntPtr.Zero && previous != IntPtr.Zero) SelectObject(dc, previous);
            if (bitmap != IntPtr.Zero) { DeleteObject(bitmap); bitmap = IntPtr.Zero; }
            if (dc != IntPtr.Zero) { DeleteDC(dc); dc = IntPtr.Zero; }
        }
    }
}
