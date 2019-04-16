//#define HUNT_DEPTH_PIXEL_GRID
#define USE_CENTRE_DEPTH_IMAGE
using UnityEngine.XR.WSA;
using System;
using System.Linq;
using UnityEngine;
using System.Threading;
using System.Collections.Generic;
using System.Text;
using System.IO;

#if ENABLE_WINMD_SUPPORT
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Foundation;
using System.Threading.Tasks;
using Windows.Perception.Spatial;
using System.Runtime.InteropServices;
using Windows.Media.FaceAnalysis;
using Windows.Graphics.Imaging;
using uVector3 = UnityEngine.Vector3;
using wVector3 = System.Numerics.Vector3;
using wVector4 = System.Numerics.Vector4;
using wMatrix4x4 = System.Numerics.Matrix4x4;


//get access to depth data
[ComImport]
[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(out byte* buffer, out uint capacity);
}

#endif // ENABLE_WINMD_SUPPORT

public class DepthProvider : MonoBehaviour
{

    //define class to transmit sensordata via events
    public class SensorEventArgs : EventArgs
    {
        public List<float> depthInfos { get; set; }

    }

    //initialize eventhandler and event 
    public delegate void DepthProvidedEventHandler(object source, SensorEventArgs args);
    public static event DepthProvidedEventHandler DepthProvided;

    void Start()
    {
#if ENABLE_WINMD_SUPPORT

        // Not awaiting this...let it go.
        this.ProcessingLoopAsync();

#endif // ENABLE_WINMD_SUPPORT
    }

#if ENABLE_WINMD_SUPPORT
    /// <summary>
    /// This is just one big lump of code right now which should be factored out into some kind of
    /// 'frame reader' class which can then be subclassed for depth frame and video frame but
    /// it was handy to have it like this while I experimented with it - the intention was
    /// to tidy it up if I could get it doing more or less what I wanted :-)
    /// </summary>
    async Task ProcessingLoopAsync()
    {
        var depthMediaCapture = await this.GetMediaCaptureForDescriptionAsync(
            MediaFrameSourceKind.Depth, 448, 450, 15);

        var depthFrameReader = await depthMediaCapture.Item1.CreateFrameReaderAsync(depthMediaCapture.Item2);

        depthFrameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;

        MediaFrameReference lastDepthFrame = null;

        long depthFrameCount = 0;
        
        List<float> DepthData = new List<float>();

        // Expecting this to run at 1fps although the API (seems to) reports that it runs at 15fps
        TypedEventHandler<MediaFrameReader, MediaFrameArrivedEventArgs> depthFrameHandler =
            (sender, args) =>
            {
                using (var depthFrame = sender.TryAcquireLatestFrame())
                {
                    if ((depthFrame != null) && (depthFrame != lastDepthFrame))
                    {
                        lastDepthFrame = depthFrame;

                        Interlocked.Increment(ref depthFrameCount);

                        //write depthdata into list 'DepthData' 
                        DepthData = GetDepthDataFromBuffer(depthFrame, (float)depthFrame.VideoMediaFrame.DepthMediaFrame.DepthFormat.DepthScaleInMeters);
                        OnDepthProvided(DepthData);
                    }
                }
            };
     

        depthFrameReader.FrameArrived += depthFrameHandler;
       

        await depthFrameReader.StartAsync();
        

        // Wait forever then dispose...just doing this to keep track of what needs disposing.
        await Task.Delay(-1);

        depthFrameReader.FrameArrived -= depthFrameHandler;
      
        
        depthFrameReader.Dispose();

       
        depthMediaCapture.Item1.Dispose();

       
    }


    private static unsafe List<float> GetDepthDataFromBuffer(MediaFrameReference frame, float scaleInMeters)
    {
        List<float> depthArray = new List<float>();

        var bitmap = frame.VideoMediaFrame.SoftwareBitmap;

        using (var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read))
        using (var reference = buffer.CreateReference())
        {
            var description = buffer.GetPlaneDescription(0);

            byte* pBits2;
            uint size2;
            var byteAccess = reference as IMemoryBufferByteAccess;

            //get depth data from buffer
            byteAccess.GetBuffer(out pBits2, out size2);



            for (int i = 0; i < size2; i++)
            {
                depthArray.Add(*(UInt16*)(pBits2 + i) * scaleInMeters);

            }
            return depthArray;
        }
    }


    static bool IsValidDepthDistance(float depthDistance)
    {
        // If that depth value is > 4.0m then we discard it because it seems like 
        // 4.**m (4.09?) comes back from the sensor when it hasn't really got a value
        return ((depthDistance > 0.5f) && (depthDistance <= 4.0f));
    }
    // Used an explicit tuple here as I'm in C# 6.0
    async Task<Tuple<MediaCapture, MediaFrameSource>> GetMediaCaptureForDescriptionAsync(
        MediaFrameSourceKind sourceKind,
        int width,
        int height,
        int frameRate,
        string[] bitmapFormats = null)
    {
        MediaCapture mediaCapture = null;
        MediaFrameSource frameSource = null;

        var allSources = await MediaFrameSourceGroup.FindAllAsync();

        // Ignore frame rate here on the description as both depth streams seem to tell me they are
        // 30fps whereas I don't think they are (from the docs) so I leave that to query later on.
        // NB: LastOrDefault here is a NASTY, NASTY hack - just my way of getting hold of the 
        // *LAST* depth stream rather than the *FIRST* because I'm assuming that the *LAST*
        // one is the longer distance stream rather than the short distance stream.
        // I should fix this and find a better way of choosing the right depth stream rather
        // than relying on some ordering that's not likely to always work!
        var sourceInfo =
            allSources.SelectMany(group => group.SourceInfos)
            .LastOrDefault(
                si =>
                    (si.MediaStreamType == MediaStreamType.VideoRecord) &&
                    (si.SourceKind == sourceKind) &&
                    (si.VideoProfileMediaDescription.Any(
                        desc =>
                            desc.Width == width &&
                            desc.Height == height &&
                            desc.FrameRate == frameRate)));

        if (sourceInfo != null)
        {
            var sourceGroup = sourceInfo.SourceGroup;

            mediaCapture = new MediaCapture();

            await mediaCapture.InitializeAsync(
               new MediaCaptureInitializationSettings()
               {
               // I want software bitmaps
               MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                   SourceGroup = sourceGroup,
                   StreamingCaptureMode = StreamingCaptureMode.Video
               }
            );
            frameSource = mediaCapture.FrameSources[sourceInfo.Id];

            var selectedFormat = frameSource.SupportedFormats.First(
                format =>
                    format.VideoFormat.Width == width && format.VideoFormat.Height == height &&
                    format.FrameRate.Numerator / format.FrameRate.Denominator == frameRate &&
                    ((bitmapFormats == null) || (bitmapFormats.Contains(format.Subtype.ToLower()))));

            await frameSource.SetFormatAsync(selectedFormat);
        }
        return (Tuple.Create(mediaCapture, frameSource));
    }

    static wMatrix4x4 ByteArrayToMatrix(byte[] bits)
    {
        var matrix = wMatrix4x4.Identity;

        var handle = GCHandle.Alloc(bits, GCHandleType.Pinned);
        matrix = Marshal.PtrToStructure<wMatrix4x4>(handle.AddrOfPinnedObject());
        handle.Free();

        return (matrix);
    }
#if HUNT_DEPTH_PIXEL_GRID

    static readonly int DEPTH_SEARCH_GRID_SIZE = 32;

#endif // HUNT_DEPTH_PIXEL_GRID

    
    static readonly Guid MFSampleExtension_Spatial_CameraCoordinateSystem = new Guid("9D13C82F-2199-4E67-91CD-D1A4181F2534");
    static readonly Guid MFSampleExtension_Spatial_CameraProjectionTransform = new Guid("47F9FCB5-2A02-4F26-A477-792FDF95886A");
    static readonly Guid MFSampleExtension_Spatial_CameraViewTransform = new Guid("4E251FA4-830F-4770-859A-4B8D99AA809B");

#endif // ENABLE_WINMD_SUPPORT

    //event function
    protected virtual void OnDepthProvided(List<float> depthinfos)
    {
        if (DepthProvided != null)
        {
            DepthProvided(this, new SensorEventArgs() { depthInfos = depthinfos }); //trigger the event object instantiated
        }
    }
}