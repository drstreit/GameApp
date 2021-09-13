using ALogger;
using DesktopDuplication;
using GlobalHotKeys;
using GlobalHotKeys.Native.Types;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleMain
{
    class Program
    {
        public static DesktopDuplication.DesktopDuplicator duplicator;
        static void HotKeyPressed(HotKey hotKey) => ThreadPool.QueueUserWorkItem(CaptureScreen, duplicator);

        #region PInvoke
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, out Rectangle lpRect);
        #endregion

        /// <summary>
        /// Console to define a screen area to be captured and processed
        /// </summary>
        static void Main()
        {
            var source = new CancellationTokenSource();
            Vars._log = new Logger(source);
            var hotKeyManager = new HotKeyManager();

            try
            {
                #region Setup parameters and initialize classes
                // prepare console
                ConsoleKeyInfo cki;
                // set variables
                var t = Task<bool>.Factory.StartNew(SetVars_GetConfig, source.Token);
                if (!t.Result)
                    throw new ArgumentException("Unable to set AppConfig variables");
                Vars._log.AddMessage(new LogMessage(Levels.Log, "Finished loading parameter..."));
                ListWindows();
                DoWindowSelectionProcess();
                SetGlobalKey(hotKeyManager, out IDisposable subscription, out IRegistration shift1);
                #endregion
                // initialize screenshot creation
                duplicator = new DesktopDuplicator(Vars.numAdapter, Vars.numOutput, Vars._captureWindowsRect, source, Vars.StoragePath);
                // wait for input
                Vars._log.AddMessage(new LogMessage(Levels.Log, "Press 'ESC' to stop program"));
                do
                {
                    cki = Console.ReadKey(true);
                    Vars._log.AddMessage(new LogMessage(Levels.Error,
                        $"No function assigned to '{cki.Modifiers}'+'{cki.Key}'"));
                } while (cki.Key != ConsoleKey.Escape);
                Vars._log.Stop();
            }
            catch (Exception ex)
            {
                Vars._log?.AddMessage(new LogMessage(Levels.Error, $"Error in Program.cs - Main: {ex.Message}"));
            }
            finally
            {
                source.Cancel();
                hotKeyManager.Dispose();
            }
            Console.ReadKey();
        }

        /// <summary>
        /// Set a global key to fire event "CaptureScreen"
        /// </summary>
        /// <param name="hotKeyManager"></param>
        /// <param name="subscription"></param>
        /// <param name="shift1"></param>
        private static void SetGlobalKey(HotKeyManager hotKeyManager, out IDisposable subscription, out IRegistration shift1)
        {
            subscription = hotKeyManager.HotKeyPressed.Subscribe(HotKeyPressed);
            Vars._log.AddMessage(new LogMessage(Levels.Log, $"Hotkey set to '{VirtualKeyCode.VK_TAB}' with modifier '{Modifiers.Control}'"));
            shift1 = hotKeyManager.Register(VirtualKeyCode.VK_TAB, Modifiers.Control);
        }


        private static void CaptureScreen(object state)
        {
            DesktopDuplicator d = (DesktopDuplicator)state; 
            d.Capture();
        }

        #region SetEnvironment

        /// <summary>
        /// Read App.config and set environment variables
        /// </summary>
        /// <returns></returns>
        private static bool SetVars_GetConfig()
        {
            // get config file and list
            try
            {
                Vars._config = ConfigurationManager.AppSettings;
                Vars._log.AddMessage(new LogMessage(Levels.Log, "Found config file: "));
                foreach (var key in Vars._config.AllKeys)
                    Vars._log.AddMessage(new LogMessage(Levels.Log, $"\t{key}: {Vars._config[key]}"));
                Vars._log.AddMessage(new LogMessage(Levels.Log, Environment.NewLine));
            }
            catch (Exception ex)
            {
                Vars._log.AddMessage(new LogMessage(Levels.Error, $"Error loading AppConfig: {ex.Message}"));
                return false;
            }
            // set variables
            try
            {
                Vars.StoragePath = Vars._config[nameof(Vars.StoragePath)];
                /**
                _hidCorporateName = _config[nameof(_hidCorporateName)];
                _gameActionsPath = _config[nameof(_gameActionsPath)];

                if ((!uint.TryParse(_config[nameof(_hidProductId)], out _hidProductId)) ||
                    (!uint.TryParse(_config[nameof(_hidVendorId)], out _hidVendorId)))
                    throw new ArgumentException("Error converting _hidProductId and _hidVendorId to uint");
                **/
            }
            catch (Exception ex)
            {
                Vars._log.AddMessage(new LogMessage(Levels.Error, $"Error setting variables from AppConfig: {ex.Message}"));
                return false;
            }
            return true;
        }
        #endregion

        #region Define Capture Area

        /// <summary>
        /// Select a windows that covers the area to store a screenshot from for further processing. The screenshot can be captured continuously or via globval hotkey.
        /// </summary>
        private static void DoWindowSelectionProcess()
        {
            while (true) // Loop indefinitely
            {
                Vars._log.AddMessage(new LogMessage(Levels.Log,
                    $"Please select the corresponding window number to define the capture area - area will be captured per hotkey (next step)!"));
                var r = Console.ReadLine();

                if (string.Equals(r, null, StringComparison.Ordinal) || r.Equals(string.Empty))
                    continue;

                if (int.TryParse(r, out var windowNo) && windowNo <= Vars._windows.Count)
                {
                    Vars._captureWindowsName = Vars._windows[windowNo - 1];
                    // get coordinates and handle of selectd windows
                    foreach (var process in Process.GetProcesses())
                    {
                        if (string.IsNullOrEmpty(process.MainWindowTitle)) continue;
                        if (process.MainWindowTitle.Equals(Vars._captureWindowsName))
                        {
                            Vars._captureWindowsHandle = process.MainWindowHandle;
                            // get windows position
                            SetProcessDPIAware();
                            GetWindowRect(Vars._captureWindowsHandle, out Vars._captureWindowsRect);
                            break;
                        }
                    }
                    Console.Clear();
                    Vars._log.AddMessage(new LogMessage(Levels.Success,
                        $"'{Vars._captureWindowsName}' has been chosen as screen area"));
                    Vars._log.AddMessage(new LogMessage(Levels.Success,
                        $"'Coordinates to capture are upperLeft: {Vars._captureWindowsRect.X}/{Vars._captureWindowsRect.Y} to bottomRight: {Vars._captureWindowsRect.Size.Width}/{Vars._captureWindowsRect.Size.Height}"));
                    break;
                }

                Vars._log.AddMessage(new LogMessage(Levels.Error,
                    $"'{r}' is not a valid window number"));
            }
        }
        /// <summary>
        /// List all current windows to help selecting the capture area
        /// </summary>
        private static void ListWindows()
        {
            Vars._log.AddMessage(new LogMessage(Levels.Success, Vars._logSeperator));
            foreach (var process in Process.GetProcesses())
            {
                if (string.IsNullOrEmpty(process.MainWindowTitle)) continue;
                Vars._windows.Add(process.MainWindowTitle);
                Vars._log.AddMessage(new LogMessage(Levels.Log, $"Press '{Vars._windows.Count}' for setting '{process.ProcessName}' in '{process.MainWindowTitle}'"));
            }
            Vars._log.AddMessage(new LogMessage(Levels.Success, Vars._logSeperator));
            Vars._log.Flush();
        }

        #endregion
    }
}