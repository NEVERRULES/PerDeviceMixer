using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace PerDeviceMixer.Audio;

internal static class PolicyConfig
{
    private static readonly Guid ClientClassId = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    public static void SetDefaultEndpoint(string deviceId)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Changing the default endpoint requires Windows.");
        }

        var clientType = Type.GetTypeFromCLSID(ClientClassId, throwOnError: true)
            ?? throw new InvalidOperationException("Windows audio policy service is unavailable.");
        var client = (IPolicyConfig)(Activator.CreateInstance(clientType)
            ?? throw new InvalidOperationException("Windows audio policy service could not be created."));
        try
        {
            SetForRole(client, deviceId, Role.Console);
            SetForRole(client, deviceId, Role.Multimedia);
            SetForRole(client, deviceId, Role.Communications);
        }
        finally
        {
            Marshal.FinalReleaseComObject(client);
        }
    }

    private static void SetForRole(IPolicyConfig client, string deviceId, Role role)
    {
        var result = client.SetDefaultEndpoint(deviceId, role);
        if (result != 0) Marshal.ThrowExceptionForHR(result);
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);

        [PreserveSig]
        int GetDeviceFormat(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            int defaultFormat,
            out IntPtr format);

        [PreserveSig]
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        [PreserveSig]
        int SetDeviceFormat(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr endpointFormat,
            IntPtr mixFormat);

        [PreserveSig]
        int GetProcessingPeriod(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            int defaultPeriod,
            out long defaultProcessingPeriod,
            out long minimumProcessingPeriod);

        [PreserveSig]
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref long period);

        [PreserveSig]
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

        [PreserveSig]
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

        [PreserveSig]
        int GetPropertyValue(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr propertyKey,
            IntPtr propertyValue);

        [PreserveSig]
        int SetPropertyValue(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr propertyKey,
            IntPtr propertyValue);

        [PreserveSig]
        int SetDefaultEndpoint(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            Role role);

        [PreserveSig]
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }
}
