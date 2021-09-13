using ALogger;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleMain
{
    public class Vars
    {
        public static NameValueCollection _config = new NameValueCollection();
        public static List<string> _windows = new List<string>();
        public static Logger _log;
        public static readonly string _logSeperator = "==================================================================";
        public static string _captureWindowsName;
        public static Rectangle _captureWindowsRect;
        public static IntPtr _captureWindowsHandle;
        // # of graphics card adapter
        public const int numAdapter = 0;
        // # of output device (i.e. monitor)
        public const int numOutput = 0;
        // path to save files to
        public static string StoragePath;
    }
}
