using Handsfreely.App.Infrastructure;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Handsfreely.App.Services;

public sealed class BluetoothServer : IDisposable
{
    public static readonly Guid ServiceUuid = new("e7530220-2b8a-460d-925c-0267c9b9b467");

    private const ushort SdpServiceNameAttributeId = 0x100;
    private const byte SdpTextType = 0x25;

    private RfcommServiceProvider? _provider;
    private StreamSocketListener? _listener;

    public event Action<Stream, string>? Connected;

    public string? AdapterAddress { get; private set; }

    public async Task<bool> StartAsync()
    {
        var adapter = await BluetoothAdapter.GetDefaultAsync();
        if (adapter is null)
        {
            Log.Error("no bluetooth adapter");
            return false;
        }
        AdapterAddress = adapter.BluetoothAddress.ToString("X12");

        _provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(ServiceUuid));
        _listener = new StreamSocketListener();
        _listener.ConnectionReceived += OnConnectionReceived;
        await _listener.BindServiceNameAsync(_provider.ServiceId.AsString(), SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

        var writer = new DataWriter();
        var name = "Handsfreely"u8.ToArray();
        writer.WriteByte(SdpTextType);
        writer.WriteByte((byte)name.Length);
        writer.WriteBytes(name);
        _provider.SdpRawAttributes.Add(SdpServiceNameAttributeId, writer.DetachBuffer());
        _provider.StartAdvertising(_listener, true);
        Log.Info($"rfcomm service advertised, adapter {AdapterAddress}");
        return true;
    }

    private void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        var socket = args.Socket;
        var address = NormalizeAddress(socket.Information.RemoteHostName?.RawName);
        Log.Info($"incoming rfcomm connection from {address}");
        Connected?.Invoke(new SocketStream(socket), address);
    }

    public static string NormalizeAddress(string? raw) =>
        new string((raw ?? "").Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

    public void Dispose()
    {
        try { _provider?.StopAdvertising(); } catch { }
        _listener?.Dispose();
    }
}
