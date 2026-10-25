using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Rat9Battery.V102
{
    internal sealed class V102HidDevice : IDisposable
    {
        internal const ushort V102VendorId = 0x05AC;
        internal const ushort V102ProductId = 0x024F;
        internal const ushort V102UsagePage = 0x00C0;
        internal const ushort V102Usage = 1;
        internal const uint ErrorIoPending = 997;

        private const int ReadTimeoutMilliseconds = 1000;
        private const int ResponseTimeoutMilliseconds = 2500;
        private const int QueryRetries = 3;

        private readonly SafeFileHandle _handle;
        private readonly int _inputReportLength;
        private readonly int _outputReportLength;
        private readonly object _ioSync = new object();

        private V102HidDevice(
            SafeFileHandle handle,
            V102HidDescriptor descriptor)
        {
            _handle = handle;
            Descriptor = descriptor;
            _inputReportLength =
                descriptor.InputReportByteLength;
            _outputReportLength =
                descriptor.OutputReportByteLength;
        }

        public V102HidDescriptor Descriptor { get; private set; }
        public Action<string> Trace { get; set; }

        public static V102HidDevice Open()
        {
            V102HidDescriptor descriptor =
                V102HidDiscovery.Find();
            if (descriptor == null)
            {
                throw new V102HidException(
                    "V102-mottagaren hittades inte.");
            }

            SafeFileHandle handle = NativeMethods.CreateFile(
                descriptor.DevicePath,
                NativeMethods.GenericRead |
                    NativeMethods.GenericWrite,
                NativeMethods.FileShareRead |
                    NativeMethods.FileShareWrite,
                IntPtr.Zero,
                NativeMethods.OpenExisting,
                NativeMethods.FileFlagOverlapped,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new V102HidException(
                    "Kunde inte öppna V102 HID-enheten (Windows fel " +
                    error + ": " +
                    new Win32Exception(error).Message + ").");
            }

            NativeMethods.HidD_SetNumInputBuffers(handle, 64);
            return new V102HidDevice(handle, descriptor);
        }

        public V102BatteryStatus QueryBattery()
        {
            return ReadDeviceInfo().Battery;
        }

        public V102DeviceInfo ReadDeviceInfo()
        {
            lock (_ioSync)
            {
                byte[] response = ExecuteCommand(
                    V102Protocol.EncodeDeviceInfoCommand(),
                    V102Protocol.DeviceInfoResponse);
                return V102Protocol
                    .DecodeDeviceInfoResponse(response);
            }
        }

        public V102KeyboardConfig ReadKeyboardConfig()
        {
            lock (_ioSync)
            {
                byte[] response = ExecuteCommand(
                    V102Protocol.EncodeKeyboardConfigRequest(),
                    V102Protocol.GetKeyboardConfigCommand);
                return V102Protocol
                    .DecodeKeyboardConfigResponse(response);
            }
        }

        public void ApplyKeyboardConfig(V102KeyboardConfig config)
        {
            lock (_ioSync)
            {
                ExecuteCommand(
                    V102Protocol.EncodeSetKeyboardConfigCommand(
                        config),
                    V102Protocol.GetKeyboardConfigCommand);
            }
        }

        private byte[] ExecuteCommand(
            byte[] command,
            byte responseCommand)
        {
            Exception lastError = null;

            for (int attempt = 0;
                 attempt < QueryRetries;
                 attempt++)
            {
                WriteReport(command);
                Stopwatch clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds <
                    ResponseTimeoutMilliseconds)
                {
                    byte[] report = ReadReport(
                        ReadTimeoutMilliseconds);
                    if (report == null)
                    {
                        continue;
                    }

                    byte[] data = NormalizeInputReport(report);
                    if (data == null ||
                        data.Length < 3 ||
                        data[0] !=
                            V102Protocol.ResponseMarker ||
                        data[2] != responseCommand)
                    {
                        continue;
                    }

                    return data;
                }
            }

            throw new V102HidException(
                "V102 skickade inget giltigt svar för kommando 0x" +
                responseCommand.ToString("x2") + ".",
                lastError);
        }

        public void Dispose()
        {
            _handle.Dispose();
        }

        private byte[] NormalizeInputReport(byte[] report)
        {
            if (report == null || report.Length == 0)
            {
                return null;
            }

            if (report.Length > 1 &&
                report[0] == Descriptor.ReportId &&
                report[1] == V102Protocol.ResponseMarker)
            {
                byte[] data = new byte[report.Length - 1];
                Array.Copy(report, 1, data, 0, data.Length);
                return data;
            }

            if (report[0] == V102Protocol.ResponseMarker)
            {
                return report;
            }

            return null;
        }

        private void WriteReport(byte[] payload)
        {
            if (payload == null ||
                payload.Length + 1 > _outputReportLength)
            {
                throw new V102HidException(
                    "V102-kommandot passar inte i HID-rapporten.");
            }

            byte[] output = new byte[_outputReportLength];
            output[0] = Descriptor.ReportId;
            Array.Copy(
                payload,
                0,
                output,
                1,
                payload.Length);
            TraceReport("HID-skrivning", output);
            OverlappedIo.Write(_handle, output, 1000);
        }

        private byte[] ReadReport(int timeoutMilliseconds)
        {
            byte[] input = new byte[_inputReportLength];
            int bytesRead;
            if (!OverlappedIo.Read(
                _handle,
                input,
                timeoutMilliseconds,
                out bytesRead))
            {
                return null;
            }

            byte[] result = new byte[bytesRead];
            Array.Copy(input, result, bytesRead);
            TraceReport("HID-läsning", result);
            return result;
        }

        private void TraceReport(string label, byte[] data)
        {
            Action<string> trace = Trace;
            if (trace == null || data == null)
            {
                return;
            }

            trace(
                label + ": " +
                BitConverter.ToString(data)
                    .Replace("-", " "));
        }
    }

    internal sealed class V102HidDescriptor
    {
        public string DevicePath { get; set; }
        public string Product { get; set; }
        public string Manufacturer { get; set; }
        public byte ReportId { get; set; }
        public int InputReportByteLength { get; set; }
        public int OutputReportByteLength { get; set; }
        public int FeatureReportByteLength { get; set; }
    }

    internal sealed class V102HidException : Exception
    {
        public V102HidException(string message)
            : base(message)
        {
        }

        public V102HidException(
            string message,
            Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal static class V102HidDiscovery
    {
        public static V102HidDescriptor Find()
        {
            Guid hidGuid =
                new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
            IntPtr deviceInfoSet =
                NativeMethods.SetupDiGetClassDevs(
                    ref hidGuid,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    NativeMethods.DigcfPresent |
                        NativeMethods.DigcfDeviceInterface);
            if (deviceInfoSet == IntPtr.Zero ||
                deviceInfoSet == new IntPtr(-1))
            {
                throw new V102HidException(
                    "Kunde inte räkna upp Windows HID-enheter.");
            }

            try
            {
                for (uint index = 0; ; index++)
                {
                    NativeMethods.SpDeviceInterfaceData
                        interfaceData =
                            new NativeMethods.SpDeviceInterfaceData();
                    interfaceData.Size =
                        Marshal.SizeOf(interfaceData);
                    if (!NativeMethods.SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        IntPtr.Zero,
                        ref hidGuid,
                        index,
                        ref interfaceData))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error ==
                            NativeMethods.ErrorNoMoreItems)
                        {
                            break;
                        }

                        throw new Win32Exception(error);
                    }

                    int requiredSize;
                    NativeMethods.SetupDiGetDeviceInterfaceDetail(
                        deviceInfoSet,
                        ref interfaceData,
                        IntPtr.Zero,
                        0,
                        out requiredSize,
                        IntPtr.Zero);
                    if (requiredSize <= 0)
                    {
                        continue;
                    }

                    IntPtr detail = Marshal.AllocHGlobal(
                        requiredSize);
                    try
                    {
                        Marshal.WriteInt32(
                            detail,
                            IntPtr.Size == 8 ? 8 : 6);
                        if (!NativeMethods
                            .SetupDiGetDeviceInterfaceDetail(
                                deviceInfoSet,
                                ref interfaceData,
                                detail,
                                requiredSize,
                                out requiredSize,
                                IntPtr.Zero))
                        {
                            continue;
                        }

                        string path = Marshal.PtrToStringAuto(
                            IntPtr.Add(detail, 4));
                        V102HidDescriptor descriptor =
                            Inspect(path);
                        if (descriptor != null)
                        {
                            return descriptor;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(
                    deviceInfoSet);
            }

            return null;
        }

        private static V102HidDescriptor Inspect(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            SafeFileHandle handle = NativeMethods.CreateFile(
                path,
                NativeMethods.GenericRead |
                    NativeMethods.GenericWrite,
                NativeMethods.FileShareRead |
                    NativeMethods.FileShareWrite,
                IntPtr.Zero,
                NativeMethods.OpenExisting,
                0,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                return null;
            }

            try
            {
                NativeMethods.HidAttributes attributes =
                    new NativeMethods.HidAttributes();
                attributes.Size = Marshal.SizeOf(attributes);
                if (!NativeMethods.HidD_GetAttributes(
                        handle,
                        ref attributes) ||
                    attributes.VendorId != V102HidDevice.V102VendorId ||
                    attributes.ProductId != V102HidDevice.V102ProductId)
                {
                    return null;
                }

                IntPtr preparsedData;
                if (!NativeMethods.HidD_GetPreparsedData(
                    handle,
                    out preparsedData))
                {
                    return null;
                }

                try
                {
                    NativeMethods.HidCaps caps =
                        new NativeMethods.HidCaps();
                    caps.Reserved = new ushort[17];
                    int status = NativeMethods.HidP_GetCaps(
                        preparsedData,
                        ref caps);
                    if (status !=
                            NativeMethods.HidpStatusSuccess ||
                        caps.UsagePage != V102HidDevice.V102UsagePage ||
                        caps.Usage != V102HidDevice.V102Usage ||
                        caps.InputReportByteLength <= 1 ||
                        caps.OutputReportByteLength <= 1)
                    {
                        return null;
                    }

                    string product = GetString(
                        handle,
                        NativeMethods.HidStringProduct);
                    if (!string.Equals(
                        product,
                        "2.4G",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    return new V102HidDescriptor
                    {
                        DevicePath = path,
                        Product = product,
                        Manufacturer = GetString(
                            handle,
                            NativeMethods.HidStringManufacturer),
                        ReportId = GetOutputReportId(
                            preparsedData,
                            caps),
                        InputReportByteLength =
                            caps.InputReportByteLength,
                        OutputReportByteLength =
                            caps.OutputReportByteLength,
                        FeatureReportByteLength =
                            caps.FeatureReportByteLength
                    };
                }
                finally
                {
                    NativeMethods.HidD_FreePreparsedData(
                        preparsedData);
                }
            }
            finally
            {
                handle.Dispose();
            }
        }

        private static byte GetOutputReportId(
            IntPtr preparsedData,
            NativeMethods.HidCaps caps)
        {
            if (caps.NumberOutputValueCaps == 0)
            {
                return 0;
            }

            ushort length = caps.NumberOutputValueCaps;
            NativeMethods.HidpValueCaps[] valueCaps =
                new NativeMethods.HidpValueCaps[length];
            for (int index = 0;
                 index < valueCaps.Length;
                 index++)
            {
                valueCaps[index].Reserved2 = new ushort[5];
            }

            int status = NativeMethods.HidP_GetValueCaps(
                NativeMethods.HidpReportTypeOutput,
                valueCaps,
                ref length,
                preparsedData);
            if (status != NativeMethods.HidpStatusSuccess ||
                length == 0)
            {
                return 0;
            }

            return valueCaps[0].ReportId;
        }

        private static string GetString(
            SafeFileHandle handle,
            int stringType)
        {
            StringBuilder buffer = new StringBuilder(256);
            bool success = stringType ==
                    NativeMethods.HidStringManufacturer
                ? NativeMethods.HidD_GetManufacturerString(
                    handle,
                    buffer,
                    buffer.Capacity)
                : NativeMethods.HidD_GetProductString(
                    handle,
                    buffer,
                    buffer.Capacity);
            return success ? buffer.ToString() : string.Empty;
        }
    }

    internal static class OverlappedIo
    {
        public static void Write(
            SafeFileHandle handle,
            byte[] buffer,
            int timeoutMilliseconds)
        {
            GCHandle pinned = GCHandle.Alloc(
                buffer,
                GCHandleType.Pinned);
            using (ManualResetEvent completedEvent =
                new ManualResetEvent(false))
            {
                try
                {
                    NativeMethods.NativeOverlapped overlapped =
                        CreateOverlapped(completedEvent);
                    int bytesWritten;
                    bool completed = NativeMethods.WriteFile(
                        handle,
                        pinned.AddrOfPinnedObject(),
                        buffer.Length,
                        out bytesWritten,
                        ref overlapped);
                    if (!completed)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != V102HidDevice.ErrorIoPending)
                        {
                            throw new V102HidException(
                                "Kunde inte skicka V102 HID-rapport: " +
                                new Win32Exception(error).Message);
                        }

                        if (!completedEvent.WaitOne(
                            timeoutMilliseconds))
                        {
                            NativeMethods.CancelIoEx(
                                handle,
                                IntPtr.Zero);
                            throw new V102HidException(
                                "Tidsgränsen för V102 HID-skrivning " +
                                "passerades.");
                        }

                        if (!NativeMethods.GetOverlappedResult(
                            handle,
                            ref overlapped,
                            out bytesWritten,
                            false))
                        {
                            int resultError =
                                Marshal.GetLastWin32Error();
                            throw new V102HidException(
                                "V102 HID-skrivning misslyckades: " +
                                new Win32Exception(
                                    resultError).Message);
                        }
                    }
                }
                finally
                {
                    pinned.Free();
                }
            }
        }

        public static bool Read(
            SafeFileHandle handle,
            byte[] buffer,
            int timeoutMilliseconds,
            out int bytesRead)
        {
            GCHandle pinned = GCHandle.Alloc(
                buffer,
                GCHandleType.Pinned);
            using (ManualResetEvent completedEvent =
                new ManualResetEvent(false))
            {
                try
                {
                    NativeMethods.NativeOverlapped overlapped =
                        CreateOverlapped(completedEvent);
                    bool completed = NativeMethods.ReadFile(
                        handle,
                        pinned.AddrOfPinnedObject(),
                        buffer.Length,
                        out bytesRead,
                        ref overlapped);
                    if (completed)
                    {
                        return bytesRead > 0;
                    }

                    int error = Marshal.GetLastWin32Error();
                    if (error != V102HidDevice.ErrorIoPending)
                    {
                        throw new V102HidException(
                            "Kunde inte läsa V102 HID-rapport: " +
                            new Win32Exception(error).Message);
                    }

                    if (!completedEvent.WaitOne(
                        timeoutMilliseconds))
                    {
                        NativeMethods.CancelIoEx(
                            handle,
                            IntPtr.Zero);
                        completedEvent.WaitOne(100);
                        bytesRead = 0;
                        return false;
                    }

                    if (!NativeMethods.GetOverlappedResult(
                        handle,
                        ref overlapped,
                        out bytesRead,
                        false))
                    {
                        int resultError =
                            Marshal.GetLastWin32Error();
                        throw new V102HidException(
                            "V102 HID-läsning misslyckades: " +
                            new Win32Exception(
                                resultError).Message);
                    }

                    return bytesRead > 0;
                }
                finally
                {
                    pinned.Free();
                }
            }
        }

        private static NativeMethods.NativeOverlapped
            CreateOverlapped(ManualResetEvent completedEvent)
        {
            return new NativeMethods.NativeOverlapped
            {
                EventHandle =
                    completedEvent.SafeWaitHandle.DangerousGetHandle()
            };
        }
    }

    internal static class NativeMethods
    {
        public const uint GenericRead = 0x80000000;
        public const uint GenericWrite = 0x40000000;
        public const uint FileShareRead = 0x00000001;
        public const uint FileShareWrite = 0x00000002;
        public const uint OpenExisting = 3;
        public const uint FileFlagOverlapped = 0x40000000;
        public const uint DigcfPresent = 0x00000002;
        public const uint DigcfDeviceInterface = 0x00000010;
        public const int ErrorNoMoreItems = 259;
        public const int HidpStatusSuccess = 0x00110000;
        public const ushort HidpReportTypeOutput = 1;
        public const int HidStringManufacturer = 1;
        public const int HidStringProduct = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct SpDeviceInterfaceData
        {
            public int Size;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HidAttributes
        {
            public int Size;
            public ushort VendorId;
            public ushort ProductId;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HidCaps
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;

            [MarshalAs(
                UnmanagedType.ByValArray,
                SizeConst = 17)]
            public ushort[] Reserved;

            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HidpValueCaps
        {
            public ushort UsagePage;
            public byte ReportId;
            public byte IsAlias;
            public ushort BitField;
            public ushort LinkCollection;
            public ushort LinkUsage;
            public ushort LinkUsagePage;
            public byte IsRange;
            public byte IsStringRange;
            public byte IsDesignatorRange;
            public byte IsAbsolute;
            public byte HasNull;
            public byte Reserved;
            public ushort BitSize;
            public ushort ReportCount;

            [MarshalAs(
                UnmanagedType.ByValArray,
                SizeConst = 5)]
            public ushort[] Reserved2;

            public uint UnitsExp;
            public uint Units;
            public int LogicalMin;
            public int LogicalMax;
            public int PhysicalMin;
            public int PhysicalMax;
            public HidpValueUsage Union;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct HidpValueUsage
        {
            [FieldOffset(0)]
            public HidpValueRange Range;

            [FieldOffset(0)]
            public HidpValueNotRange NotRange;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HidpValueRange
        {
            public ushort UsageMin;
            public ushort UsageMax;
            public ushort StringMin;
            public ushort StringMax;
            public ushort DesignatorMin;
            public ushort DesignatorMax;
            public ushort DataIndexMin;
            public ushort DataIndexMax;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HidpValueNotRange
        {
            public ushort Usage;
            public ushort Reserved1;
            public ushort StringIndex;
            public ushort Reserved2;
            public ushort DesignatorIndex;
            public ushort Reserved3;
            public ushort DataIndex;
            public ushort Reserved4;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NativeOverlapped
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr EventHandle;
        }

        [DllImport(
            "setupapi.dll",
            CharSet = CharSet.Auto,
            SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr parentWindow,
            uint flags);

        [DllImport(
            "setupapi.dll",
            CharSet = CharSet.Auto,
            SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport(
            "setupapi.dll",
            CharSet = CharSet.Auto,
            SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SpDeviceInterfaceData deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            int deviceInterfaceDetailDataSize,
            out int requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll")]
        public static extern bool SetupDiDestroyDeviceInfoList(
            IntPtr deviceInfoSet);

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Auto,
            SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(
            SafeFileHandle file,
            IntPtr buffer,
            int bytesToRead,
            out int bytesRead,
            ref NativeOverlapped overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(
            SafeFileHandle file,
            IntPtr buffer,
            int bytesToWrite,
            out int bytesWritten,
            ref NativeOverlapped overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(
            SafeFileHandle file,
            ref NativeOverlapped overlapped,
            out int bytesTransferred,
            bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(
            SafeFileHandle file,
            IntPtr overlapped);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetAttributes(
            SafeFileHandle device,
            ref HidAttributes attributes);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetPreparsedData(
            SafeFileHandle device,
            out IntPtr preparsedData);

        [DllImport("hid.dll")]
        public static extern bool HidD_FreePreparsedData(
            IntPtr preparsedData);

        [DllImport("hid.dll")]
        public static extern bool HidD_SetNumInputBuffers(
            SafeFileHandle device,
            int count);

        [DllImport("hid.dll")]
        public static extern int HidP_GetCaps(
            IntPtr preparsedData,
            ref HidCaps caps);

        [DllImport("hid.dll")]
        public static extern int HidP_GetValueCaps(
            ushort reportType,
            [Out] HidpValueCaps[] valueCaps,
            ref ushort valueCapsLength,
            IntPtr preparsedData);

        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        public static extern bool HidD_GetProductString(
            SafeFileHandle device,
            StringBuilder buffer,
            int bufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        public static extern bool HidD_GetManufacturerString(
            SafeFileHandle device,
            StringBuilder buffer,
            int bufferLength);
    }
}
