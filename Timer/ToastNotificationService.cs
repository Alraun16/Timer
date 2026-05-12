using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Timer
{
    internal static class ToastNotificationService
    {
        private const string AppUserModelId = "Raun.Timer";
        private const string ProtocolName = "timer";
        private const string RepeatUri = "timer://repeat";
        private static readonly Guid ToastActivatorClsid = new("9F510A7F-3553-4D2C-8E13-73B71D77E72F");

        public static void Register()
        {
            try
            {
                string exePath = GetExePath();
                Log($"Register start. Exe={exePath}");
                SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
                RegisterProtocol(exePath);
                CreateStartMenuShortcut(exePath);
                Log("Register complete.");
            }
            catch (Exception ex)
            {
                Log($"Register failed: {ex}");
                // Notifications are best-effort; the timer itself should keep working.
            }
        }

        public static void ShowCompleted(TimeSpan duration, string taskDescription)
        {
            try
            {
                string descriptionLine = string.IsNullOrWhiteSpace(taskDescription)
                    ? string.Empty
                    : $"<text>{SecurityElement.Escape(taskDescription.Trim())}</text>";

                var xml = new XmlDocument();
                xml.LoadXml(
                    $"""
                    <toast launch="{RepeatUri}">
                      <visual>
                        <binding template="ToastGeneric">
                          <text>Timer complete</text>
                          <text>{FormatWorkedDuration(duration)}</text>
                          {descriptionLine}
                        </binding>
                      </visual>
                      <actions>
                        <action content="Repeat" arguments="{RepeatUri}" activationType="protocol"/>
                      </actions>
                      <audio silent="true"/>
                    </toast>
                    """);

                var notification = new ToastNotification(xml);
                ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(notification);
                Log("Toast show requested.");
            }
            catch (Exception ex)
            {
                Log($"Toast show failed: {ex}");
                // Keep direct in-app sound independent from Windows notification failures.
            }
        }

        private static string FormatWorkedDuration(TimeSpan duration)
        {
            int totalHours = (int)duration.TotalHours;
            return $"Проработал {totalHours} ч {duration.Minutes:D2} мин";
        }

        public static bool IsRepeatActivation(string[] args)
        {
            foreach (string arg in args)
            {
                if (arg.StartsWith(RepeatUri, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void RegisterProtocol(string exePath)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProtocolName}");
            key.SetValue(null, "URL:Timer Protocol");
            key.SetValue("URL Protocol", string.Empty);

            using RegistryKey command = key.CreateSubKey(@"shell\open\command");
            command.SetValue(null, $"\"{exePath}\" \"%1\"");
        }

        private static void CreateStartMenuShortcut(string exePath)
        {
            string programsPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            string shortcutPath = Path.Combine(programsPath, "Programs", "Timer.lnk");

            object shellLinkObject = new CShellLink();
            var shellLink = (IShellLinkW)shellLinkObject;
            shellLink.SetPath(exePath);
            shellLink.SetArguments(string.Empty);

            var propertyStore = (IPropertyStore)shellLink;

            using (var appId = PropVariant.FromString(AppUserModelId))
            {
                var key = PropertyKeys.AppUserModelId;
                propertyStore.SetValue(ref key, appId);
            }

            using (var clsid = PropVariant.FromGuid(ToastActivatorClsid))
            {
                var key = PropertyKeys.ToastActivatorClsid;
                propertyStore.SetValue(ref key, clsid);
            }

            propertyStore.Commit();

            var persistFile = (IPersistFile)shellLink;
            persistFile.Save(shortcutPath, true);
        }

        private static string GetExePath()
        {
            return Process.GetCurrentProcess().MainModule?.FileName
                ?? Environment.ProcessPath
                ?? throw new InvalidOperationException("Unable to resolve executable path.");
        }

        private static void Log(string message)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "Timer-toast.log");
                File.AppendAllText(path, $"{DateTime.Now:O} {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(
            [MarshalAs(UnmanagedType.LPWStr)] string appId);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(IntPtr pvar);

        [ComImport]
        [ClassInterface(ClassInterfaceType.None)]
        [ComDefaultInterface(typeof(IShellLinkW))]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private sealed class CShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath(IntPtr pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription(IntPtr pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory(IntPtr pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments(IntPtr pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation(IntPtr pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        private interface IPropertyStore
        {
            void GetCount(out uint cProps);
            void GetAt(uint iProp, out PropertyKey pkey);
            void GetValue(ref PropertyKey key, out PropVariant pv);
            void SetValue(ref PropertyKey key, PropVariant pv);
            void Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            public Guid FormatId;
            public uint PropertyId;

            public PropertyKey(Guid formatId, uint propertyId)
            {
                FormatId = formatId;
                PropertyId = propertyId;
            }
        }

        private static class PropertyKeys
        {
            private static readonly Guid AppUserModelFormatId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

            public static PropertyKey AppUserModelId => new(AppUserModelFormatId, 5);
            public static PropertyKey ToastActivatorClsid => new(AppUserModelFormatId, 26);
        }

        [StructLayout(LayoutKind.Sequential)]
        private sealed class PropVariant : IDisposable
        {
            private const ushort VtLpWStr = 31;
            private const ushort VtClsid = 72;

            private ushort _vt;
            private ushort _wReserved1;
            private ushort _wReserved2;
            private ushort _wReserved3;
            private IntPtr _value;
            private IntPtr _value2;

            public static PropVariant FromString(string value)
            {
                return new PropVariant
                {
                    _vt = VtLpWStr,
                    _value = Marshal.StringToCoTaskMemUni(value)
                };
            }

            public static PropVariant FromGuid(Guid value)
            {
                IntPtr pointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<Guid>());
                Marshal.StructureToPtr(value, pointer, false);

                return new PropVariant
                {
                    _vt = VtClsid,
                    _value = pointer
                };
            }

            public void Dispose()
            {
                IntPtr ptr = Marshal.AllocCoTaskMem(Marshal.SizeOf<PropVariant>());
                try
                {
                    Marshal.StructureToPtr(this, ptr, false);
                    PropVariantClear(ptr);
                    _value = IntPtr.Zero;
                    _value2 = IntPtr.Zero;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(ptr);
                }
            }
        }
    }
}
