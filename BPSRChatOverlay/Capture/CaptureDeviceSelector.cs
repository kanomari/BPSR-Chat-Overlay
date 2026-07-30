using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Serilog;
using SharpPcap;
using SharpPcap.LibPcap;

namespace BPSR_ZDPSLib;

public sealed record CaptureDeviceSelectionResult(
    ICaptureDevice Device,
    CaptureDeviceSelectionReason SelectionReason,
    bool WasFallback,
    bool ConfiguredDeviceMissing,
    bool WasConfigurationEmpty,
    string? ConfiguredDeviceName,
    IPAddress? GameConnectionLocalAddress,
    int? WindowsBestRouteInterfaceIndex);

public static class CaptureDeviceSelector
{
    private const short AddressFamilyInterNetwork = 2;
    private const uint NoError = 0;

    public static bool HasSavedDevice(
        IReadOnlyList<ICaptureDevice> devices,
        string? configuredDeviceName)
    {
        if (string.IsNullOrWhiteSpace(configuredDeviceName))
        {
            return false;
        }

        return devices.Any(device =>
            string.Equals(
                device.Name,
                configuredDeviceName,
                StringComparison.Ordinal) ||
            string.Equals(
                device.Description,
                configuredDeviceName,
                StringComparison.Ordinal));
    }

    public static CaptureDeviceSelectionResult Select(
        IReadOnlyList<ICaptureDevice> devices,
        string? configuredDeviceName,
        IReadOnlyList<string> gameProcessNames)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(gameProcessNames);

        if (devices.Count == 0)
        {
            throw new ArgumentException(
                "At least one capture device is required.",
                nameof(devices));
        }

        bool wasConfigurationEmpty =
            string.IsNullOrWhiteSpace(configuredDeviceName);

        if (!wasConfigurationEmpty)
        {
            ICaptureDevice? savedDevice = devices.FirstOrDefault(device =>
                string.Equals(
                    device.Name,
                    configuredDeviceName,
                    StringComparison.Ordinal));
            if (savedDevice is not null)
            {
                return CreateResult(
                    savedDevice,
                    CaptureDeviceSelectionReason.SavedNameMatch,
                    configuredDeviceName,
                    wasConfigurationEmpty);
            }

            savedDevice = devices.FirstOrDefault(device =>
                string.Equals(
                    device.Description,
                    configuredDeviceName,
                    StringComparison.Ordinal));
            if (savedDevice is not null)
            {
                return CreateResult(
                    savedDevice,
                    CaptureDeviceSelectionReason.SavedDescriptionMatch,
                    configuredDeviceName,
                    wasConfigurationEmpty);
            }
        }

        bool configuredDeviceMissing = !wasConfigurationEmpty;

        IReadOnlyList<IPAddress> gameLocalAddresses =
            TryGetGameLocalAddresses(gameProcessNames);
        IPAddress? observedGameLocalAddress =
            gameLocalAddresses.FirstOrDefault();
        foreach (IPAddress gameLocalAddress in gameLocalAddresses)
        {
            ICaptureDevice? gameDevice =
                FindCaptureDeviceByAddress(devices, gameLocalAddress);
            if (gameDevice is not null)
            {
                return CreateResult(
                    gameDevice,
                    CaptureDeviceSelectionReason.GameConnectionLocalAddress,
                    configuredDeviceName,
                    wasConfigurationEmpty,
                    configuredDeviceMissing,
                    gameLocalAddress);
            }
        }

        IReadOnlyList<NetworkInterface> networkInterfaces =
            TryGetNetworkInterfaces();
        int? bestRouteInterfaceIndex = TryGetBestRouteInterfaceIndex();
        if (bestRouteInterfaceIndex is int interfaceIndex)
        {
            NetworkInterface? bestRouteInterface =
                FindNetworkInterfaceByIndex(
                    networkInterfaces,
                    interfaceIndex);
            ICaptureDevice? bestRouteDevice =
                bestRouteInterface is null
                    ? null
                    : TryFindCaptureDeviceForNetworkInterface(
                        devices,
                        bestRouteInterface);
            if (bestRouteDevice is not null)
            {
                return CreateResult(
                    bestRouteDevice,
                    CaptureDeviceSelectionReason.WindowsBestRoute,
                    configuredDeviceName,
                    wasConfigurationEmpty,
                    configuredDeviceMissing,
                    observedGameLocalAddress,
                    interfaceIndex);
            }
        }

        foreach (NetworkInterface networkInterface in
                 GetActiveGatewayInterfaces(networkInterfaces))
        {
            ICaptureDevice? activeDevice =
                TryFindCaptureDeviceForNetworkInterface(
                    devices,
                    networkInterface);
            if (activeDevice is not null)
            {
                return CreateResult(
                    activeDevice,
                    CaptureDeviceSelectionReason.ActiveGatewayInterface,
                    configuredDeviceName,
                    wasConfigurationEmpty,
                    configuredDeviceMissing,
                    observedGameLocalAddress,
                    bestRouteInterfaceIndex);
            }
        }

        return CreateResult(
            devices[0],
            CaptureDeviceSelectionReason.FirstEnumeratedDevice,
            configuredDeviceName,
            wasConfigurationEmpty,
            configuredDeviceMissing,
            observedGameLocalAddress,
            bestRouteInterfaceIndex);
    }

    private static CaptureDeviceSelectionResult CreateResult(
        ICaptureDevice device,
        CaptureDeviceSelectionReason reason,
        string? configuredDeviceName,
        bool wasConfigurationEmpty,
        bool configuredDeviceMissing = false,
        IPAddress? gameConnectionLocalAddress = null,
        int? windowsBestRouteInterfaceIndex = null)
    {
        return new CaptureDeviceSelectionResult(
            device,
            reason,
            configuredDeviceMissing ||
            reason == CaptureDeviceSelectionReason.FirstEnumeratedDevice,
            configuredDeviceMissing,
            wasConfigurationEmpty,
            configuredDeviceName,
            gameConnectionLocalAddress,
            windowsBestRouteInterfaceIndex);
    }

    private static IReadOnlyList<IPAddress> TryGetGameLocalAddresses(
        IReadOnlyList<string> gameProcessNames)
    {
        try
        {
            return Utils.GetTCPConnectionsForExe(
                    [.. gameProcessNames])
                .Select(connection => connection.LocalAddress)
                .Select(addressText =>
                    IPAddress.TryParse(addressText, out IPAddress? address)
                        ? address
                        : null)
                .OfType<IPAddress>()
                .Where(IsUsableIPv4Address)
                .GroupBy(address => address)
                .OrderByDescending(group => group.Count())
                .ThenBy(
                    group => group.Key.ToString(),
                    StringComparer.Ordinal)
                .Select(group => group.Key)
                .ToArray();
        }
        catch (Exception ex)
        {
            Log.Debug(
                ex,
                "Could not inspect game TCP connections while selecting a capture device");
            return [];
        }
    }

    private static IReadOnlyList<NetworkInterface>
        TryGetNetworkInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception ex)
        {
            Log.Debug(
                ex,
                "Could not enumerate Windows network interfaces while selecting a capture device");
            return [];
        }
    }

    private static int? TryGetBestRouteInterfaceIndex()
    {
        try
        {
            SockaddrIn destination = new()
            {
                Family = AddressFamilyInterNetwork,
                Address = 0x01010101
            };

            uint result = GetBestInterfaceEx(
                ref destination,
                out uint interfaceIndex);
            if (result == NoError)
            {
                return checked((int)interfaceIndex);
            }

            Log.Debug(
                "GetBestInterfaceEx could not select an interface. ErrorCode: {ErrorCode}",
                result);
        }
        catch (Exception ex)
        {
            Log.Debug(
                ex,
                "GetBestInterfaceEx failed while selecting a capture device");
        }

        return null;
    }

    private static NetworkInterface? FindNetworkInterfaceByIndex(
        IReadOnlyList<NetworkInterface> networkInterfaces,
        int interfaceIndex)
    {
        foreach (NetworkInterface networkInterface in networkInterfaces)
        {
            try
            {
                if (networkInterface.Supports(
                        NetworkInterfaceComponent.IPv4) &&
                    networkInterface.GetIPProperties()
                        .GetIPv4Properties()?.Index == interfaceIndex)
                {
                    return networkInterface;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(
                    ex,
                    "Could not read an IPv4 interface index while selecting a capture device");
            }
        }

        return null;
    }

    private static ICaptureDevice?
        TryFindCaptureDeviceForNetworkInterface(
            IReadOnlyList<ICaptureDevice> devices,
            NetworkInterface networkInterface)
    {
        try
        {
            return FindCaptureDeviceForNetworkInterface(
                devices,
                networkInterface);
        }
        catch (Exception ex)
        {
            Log.Debug(
                ex,
                "Could not map a Windows network interface to a capture device");
            return null;
        }
    }

    private static ICaptureDevice? FindCaptureDeviceForNetworkInterface(
        IReadOnlyList<ICaptureDevice> devices,
        NetworkInterface networkInterface)
    {
        IPAddress[] interfaceAddresses =
            GetUsableUnicastAddresses(networkInterface).ToArray();
        ICaptureDevice? match = devices.FirstOrDefault(device =>
            GetCaptureDeviceAddresses(device)
                .Intersect(interfaceAddresses)
                .Any());
        if (match is not null)
        {
            return match;
        }

        PhysicalAddress interfaceMacAddress;
        try
        {
            interfaceMacAddress =
                networkInterface.GetPhysicalAddress();
        }
        catch (Exception ex)
        {
            Log.Debug(
                ex,
                "Could not read the MAC address for {InterfaceName}",
                networkInterface.Name);
            interfaceMacAddress = PhysicalAddress.None;
        }

        if (!IsEmptyPhysicalAddress(interfaceMacAddress))
        {
            match = devices.FirstOrDefault(device =>
                TryGetCaptureDeviceMacAddress(device) is
                    PhysicalAddress deviceMacAddress &&
                deviceMacAddress.Equals(interfaceMacAddress));
            if (match is not null)
            {
                return match;
            }
        }

        if (!string.IsNullOrWhiteSpace(networkInterface.Id))
        {
            match = devices.FirstOrDefault(device =>
                (device.Name ?? string.Empty).Contains(
                    networkInterface.Id,
                    StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return devices.FirstOrDefault(device =>
            NamesMatch(GetFriendlyName(device), networkInterface.Name) ||
            NamesMatch(device.Description, networkInterface.Description) ||
            NamesMatch(device.Description, networkInterface.Name));
    }

    private static ICaptureDevice? FindCaptureDeviceByAddress(
        IReadOnlyList<ICaptureDevice> devices,
        IPAddress address)
    {
        return devices.FirstOrDefault(device =>
            GetCaptureDeviceAddresses(device).Contains(address));
    }

    private static IEnumerable<IPAddress> GetCaptureDeviceAddresses(
        ICaptureDevice device)
    {
        if (device is not LibPcapLiveDevice liveDevice)
        {
            return [];
        }

        try
        {
            return liveDevice.Addresses
                .Select(address => address.Addr?.ipAddress)
                .OfType<IPAddress>()
                .Where(address =>
                    address.AddressFamily == AddressFamily.InterNetwork)
                .ToArray();
        }
        catch (Exception ex)
        {
            Log.Debug(
                ex,
                "Could not read addresses for capture device {DeviceName}",
                device.Name);
            return [];
        }
    }

    private static PhysicalAddress? TryGetCaptureDeviceMacAddress(
        ICaptureDevice device)
    {
        try
        {
            return device.MacAddress;
        }
        catch (Exception ex)
        {
            Log.Debug(
                ex,
                "Could not read the MAC address for capture device {DeviceName}",
                device.Name);
            return null;
        }
    }

    private static IReadOnlyList<NetworkInterface>
        GetActiveGatewayInterfaces(
            IReadOnlyList<NetworkInterface> networkInterfaces)
    {
        List<ActiveInterfaceCandidate> candidates = [];
        foreach (NetworkInterface networkInterface in networkInterfaces)
        {
            try
            {
                NetworkInterfaceType interfaceType =
                    networkInterface.NetworkInterfaceType;
                if (networkInterface.OperationalStatus !=
                        OperationalStatus.Up ||
                    interfaceType is
                        NetworkInterfaceType.Loopback or
                        NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                IPInterfaceProperties properties =
                    networkInterface.GetIPProperties();
                bool hasGateway = properties.GatewayAddresses
                    .Select(gateway => gateway.Address)
                    .Any(IsUsableGatewayAddress);
                bool hasUsableAddress = properties.UnicastAddresses
                    .Select(unicast => unicast.Address)
                    .Any(IsUsableIPv4Address);
                if (!hasGateway || !hasUsableAddress)
                {
                    continue;
                }

                candidates.Add(new ActiveInterfaceCandidate(
                    networkInterface,
                    GetInterfaceTypePriority(interfaceType),
                    networkInterface.Speed,
                    networkInterface.Name));
            }
            catch (Exception ex)
            {
                Log.Debug(
                    ex,
                    "Could not evaluate a Windows network interface while selecting a capture device");
            }
        }

        return candidates
            .OrderBy(candidate => candidate.TypePriority)
            .ThenByDescending(candidate => candidate.Speed)
            .ThenBy(
                candidate => candidate.StableName,
                StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.NetworkInterface)
            .ToArray();
    }

    private static IEnumerable<IPAddress> GetUsableUnicastAddresses(
        NetworkInterface networkInterface)
    {
        try
        {
            return networkInterface.GetIPProperties()
                .UnicastAddresses
                .Select(unicast => unicast.Address)
                .Where(IsUsableIPv4Address)
                .ToArray();
        }
        catch (Exception ex)
        {
            Log.Debug(
                ex,
                "Could not read unicast addresses for {InterfaceName}",
                networkInterface.Name);
            return [];
        }
    }

    private static bool IsUsableGatewayAddress(IPAddress address)
    {
        return address.AddressFamily == AddressFamily.InterNetwork &&
               !address.Equals(IPAddress.Any) &&
               !address.Equals(IPAddress.None);
    }

    private static bool IsUsableIPv4Address(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.None) ||
            IPAddress.IsLoopback(address))
        {
            return false;
        }

        byte[] bytes = address.GetAddressBytes();
        bool isLinkLocal = bytes[0] == 169 && bytes[1] == 254;
        bool isMulticast = bytes[0] is >= 224 and <= 239;
        bool isBroadcast = bytes.All(value => value == byte.MaxValue);
        return !isLinkLocal && !isMulticast && !isBroadcast;
    }

    private static int GetInterfaceTypePriority(
        NetworkInterfaceType interfaceType)
    {
        return interfaceType switch
        {
            NetworkInterfaceType.Ethernet => 0,
            NetworkInterfaceType.Wireless80211 => 1,
            _ => 2
        };
    }

    private static bool IsEmptyPhysicalAddress(
        PhysicalAddress physicalAddress)
    {
        byte[] bytes = physicalAddress.GetAddressBytes();
        return bytes.Length == 0 || bytes.All(value => value == 0);
    }

    private static string? GetFriendlyName(ICaptureDevice device)
    {
        return device is LibPcapLiveDevice liveDevice
            ? liveDevice.Interface?.FriendlyName
            : null;
    }

    private static bool NamesMatch(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(
                   left.Trim(),
                   right.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetBestInterfaceEx(
        ref SockaddrIn destinationAddress,
        out uint bestInterfaceIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct SockaddrIn
    {
        public short Family;
        public ushort Port;
        public uint Address;
        public ulong Padding;
    }

    private sealed record ActiveInterfaceCandidate(
        NetworkInterface NetworkInterface,
        int TypePriority,
        long Speed,
        string StableName);
}
