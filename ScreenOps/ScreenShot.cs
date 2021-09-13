using System;
using System.Drawing;

namespace DesktopDuplication
{
    public class ScreenShot: IDisposable
    {
        public Bitmap Screen { get; set; }

        public DateTime CaptureTime { get; set; }

        public void Dispose()
        {
            Screen?.Dispose();
        }
    }
}