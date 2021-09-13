using System;
using System.Collections.Concurrent;
using System.Drawing.Imaging;

using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;


namespace DesktopDuplication
{
    /// <summary>
    /// Provides access to frame-by-frame updates of a particular desktop (i.e. one monitor), with image and cursor information.
    /// </summary>
    public class DesktopDuplicator
    {
        private Device _mDevice;
        private Texture2DDescription _mTextureDesc;
        private OutputDescription _mOutputDesc;
        private OutputDuplication _outDuplication;
        private Texture2D _desktopImageTexture;

        private readonly ConcurrentQueue<ScreenShot> _rawBitmapQueue = new ConcurrentQueue<ScreenShot>();
        private readonly ConcurrentQueue<ScreenShot> _processedBitmapQueue = new ConcurrentQueue<ScreenShot>();

        public DesktopDuplicator(int whichGraphicsCardAdapter, int whichOutputDevice, System.Drawing.Rectangle targetWindowsRect, CancellationTokenSource source, string StoragePath)
        {
            SetupEnvironment(whichGraphicsCardAdapter, whichOutputDevice);
            // start queueing
            //QueueDesktopFrames(_rawBitmapQueue, source, _mDevice, _outDuplication, _desktopImageTexture, _mOutputDesc, _mTextureDesc);
            ProcessImages(_rawBitmapQueue, source, _processedBitmapQueue, targetWindowsRect, StoragePath);
        }

        private static void ProcessImages(ConcurrentQueue<ScreenShot> rawBitmapQueue, CancellationTokenSource source
            , ConcurrentQueue<ScreenShot> processedBitmapQueue, System.Drawing.Rectangle targetWindowsRect, string StoragePath)
        {
            Task.Factory.StartNew(() =>
            {
                do
                {
                    if (!rawBitmapQueue.TryDequeue(out var b)) continue;
                    var target = new System.Drawing.Rectangle(
                        targetWindowsRect.X,
                        targetWindowsRect.Y, 
                        targetWindowsRect.Width - targetWindowsRect.X, 
                        targetWindowsRect.Height - targetWindowsRect.Y);
                    using (var n = b.Screen.Clone(target, b.Screen.PixelFormat))
                    {
                        //processedBitmapQueue?.Enqueue(new ScreenShot { CaptureTime = b.CaptureTime, Screen = n });
                        string _pathName = StoragePath + $"{DateTime.Now.Ticks}_{Guid.NewGuid()}.bmp";
                        n.Save(_pathName);
                    }
                    b.Dispose();
                } while (!source.IsCancellationRequested);
            }, source.Token);
        }

        private void SetupEnvironment(int whichGraphicsCardAdapter, int whichOutputDevice)
        {
            Adapter1 adapter;
            try
            {
                adapter = new Factory1().GetAdapter1(whichGraphicsCardAdapter);
            }
            catch (SharpDXException)
            {
                throw new DesktopDuplicationException("Could not find the specified graphics card adapter.");
            }

            _mDevice = new Device(adapter);
            Output output;
            try
            {
                output = adapter.GetOutput(whichOutputDevice);
            }
            catch (SharpDXException)
            {
                throw new DesktopDuplicationException("Could not find the specified output device.");
            }

            var output1 = output.QueryInterface<Output1>();
            _mOutputDesc = output.Description;
            _mTextureDesc = new Texture2DDescription()
            {
                CpuAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None,
                Format = Format.B8G8R8A8_UNorm,
                Width = _mOutputDesc.DesktopBounds.Right,
                Height = _mOutputDesc.DesktopBounds.Bottom,
                OptionFlags = ResourceOptionFlags.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDescription = {Count = 1, Quality = 0},
                Usage = ResourceUsage.Staging
            };
            _desktopImageTexture = new Texture2D(_mDevice, _mTextureDesc);

            try
            {
                _outDuplication = output1.DuplicateOutput(_mDevice);
            }
            catch (SharpDXException ex)
            {
                if (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.NotCurrentlyAvailable.Result.Code)
                {
                    throw new DesktopDuplicationException(
                        "There is already the maximum number of applications using the Desktop Duplication API running, please close one of the applications and try again.");
                }
            }
        }

        public void Capture()
        {
            DesktopFrame frame;
            do
            {
                frame = GetLatestFrame(_mDevice, _outDuplication, _desktopImageTexture, _mOutputDesc, _mTextureDesc);
            } 
            while (frame?.DesktopImage == null);
            _rawBitmapQueue.Enqueue(new ScreenShot { CaptureTime = DateTime.Now, Screen = frame.DesktopImage });
        }


        /// <summary>
        /// ToDo: Continuous capturing not supported
        /// </summary>
        /// <param name="rawBitmapQueue"></param>
        /// <param name="source"></param>
        /// <param name="mDevice"></param>
        /// <param name="outDuplication"></param>
        /// <param name="desktopImageTexture"></param>
        /// <param name="mOutputDesc"></param>
        /// <param name="mTextureDesc"></param>
        private static void QueueDesktopFrames(ConcurrentQueue<ScreenShot> rawBitmapQueue, CancellationTokenSource source, Device mDevice, OutputDuplication outDuplication
            , Texture2D desktopImageTexture, OutputDescription mOutputDesc, Texture2DDescription mTextureDesc) =>
            Task.Factory.StartNew(() =>
            {
                do
                {
                    var b = GetLatestFrame(mDevice, outDuplication, desktopImageTexture, mOutputDesc, mTextureDesc);
                    if (b?.DesktopImage == null) continue;
                    rawBitmapQueue.Enqueue(new ScreenShot { CaptureTime = DateTime.Now, Screen = b.DesktopImage });
                } while (!source.IsCancellationRequested);
            }, source.Token);

        private static DesktopFrame GetLatestFrame(Device mDevice, OutputDuplication outDuplication, Texture2D desktopImageTexture, OutputDescription mOutputDesc, Texture2DDescription mTextureDesc)
        {
            var frame = new DesktopFrame();
            // Try to get the latest frame; this may timeout
            var retrievalTimedOut = RetrieveFrame(desktopImageTexture, mDevice, mTextureDesc, outDuplication);
            if (retrievalTimedOut)
                return null;
            try
            {
                ProcessFrame(frame, mDevice, desktopImageTexture, mOutputDesc); 
                ReleaseFrame(outDuplication);
            }
            catch
            {
                throw new DesktopDuplicationException("Couldn't release frame.");
            }
            return frame;
        }

        private static bool RetrieveFrame(Texture2D desktopImageTexture, Device mDevice, Texture2DDescription mTextureDesc, OutputDuplication outDuplication)
        {
            if (desktopImageTexture == null)
                desktopImageTexture = new Texture2D(mDevice, mTextureDesc);
            SharpDX.DXGI.Resource desktopResource = null;
            try
            {
                outDuplication.TryAcquireNextFrame(0, out _, out desktopResource);
            }
            catch (SharpDXException ex)
            {
                if (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code)
                {
                    return true;
                }
                if (ex.ResultCode.Failure)
                {
                    throw new DesktopDuplicationException("Failed to acquire next frame.");
                }
            }

            if (desktopResource == null) throw new DesktopDuplicationException("desktopResource is null");
            using (var tempTexture = desktopResource.QueryInterface<Texture2D>())
                mDevice.ImmediateContext.CopyResource(tempTexture, desktopImageTexture);
            desktopResource.Dispose();

            return false;
        }
      
        private static void ProcessFrame(DesktopFrame frame, Device mDevice, Texture2D desktopImageTexture, OutputDescription mOutputDesc)
        {
            // Get the desktop capture texture
            var mapSource = mDevice.ImmediateContext.MapSubresource(desktopImageTexture, 0, MapMode.Read, MapFlags.None);
            
            var finalImage = new Bitmap(mOutputDesc.DesktopBounds.Right, mOutputDesc.DesktopBounds.Bottom, PixelFormat.Format32bppRgb);
            var boundsRect = new System.Drawing.Rectangle(0, 0, mOutputDesc.DesktopBounds.Right, mOutputDesc.DesktopBounds.Bottom);
            // Copy pixels from screen capture Texture to GDI bitmap
            var mapDest = finalImage.LockBits(boundsRect, ImageLockMode.WriteOnly, finalImage.PixelFormat);
            var sourcePtr = mapSource.DataPointer;
            var destPtr = mapDest.Scan0;
            for (var y = 0; y < mOutputDesc.DesktopBounds.Bottom; y++)
            {
                // Copy a single line 
                Utilities.CopyMemory(destPtr, sourcePtr, mOutputDesc.DesktopBounds.Right * 4);

                // Advance pointers
                sourcePtr = IntPtr.Add(sourcePtr, mapSource.RowPitch);
                destPtr = IntPtr.Add(destPtr, mapDest.Stride);
            }

            // Release source and dest locks
            finalImage.UnlockBits(mapDest);
            mDevice.ImmediateContext.UnmapSubresource(desktopImageTexture, 0);
            frame.DesktopImage = finalImage;
        }

        private static void ReleaseFrame(OutputDuplication outDuplication)
        {
            try
            {
                outDuplication.ReleaseFrame();
            }
            catch (SharpDXException ex)
            {
                if (ex.ResultCode.Failure)
                {
                    throw new DesktopDuplicationException("Failed to release frame.");
                }
            }
        }
    }
}
