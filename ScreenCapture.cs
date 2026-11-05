using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RivalsCounter
{
    public static class ScreenCapture
    {
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int n);
        const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

        public static byte[] CaptureVirtualScreenPng()
        {
            int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (w <= 0 || h <= 0) { w = (int)SystemParameters.PrimaryScreenWidth; h = (int)SystemParameters.PrimaryScreenHeight; x = 0; y = 0; }
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        public static byte[] BitmapSourceToPng(BitmapSource src)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        public static BitmapSource PngToImage(byte[] png)
        {
            var bi = new BitmapImage();
            using var ms = new MemoryStream(png);
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
            return bi;
        }
    }
}
