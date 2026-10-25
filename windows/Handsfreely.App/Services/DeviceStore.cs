using System.Security.Cryptography;
using System.Text.Json;
using Handsfreely.App.Infrastructure;
using Handsfreely.Core.Crypto;
using Handsfreely.Core.Pairing;
using Handsfreely.Core.Protocol;

namespace Handsfreely.App.Services;

public sealed class TrustedPhone
{
    public required string Id { get; init; }
    public required string PublicKey { get; init; }
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string? BluetoothAddress { get; set; }
    public DateTimeOffset PairedAt { get; set; }
    public DateTimeOffset? LastSeen { get; set; }

    public static string IdFor(byte[] publicKey) => Convert.ToHexStringLower(SHA256.HashData(publicKey)[..8]);
}

public sealed class DeviceStore : IPairingAuthority
{
    public static readonly TimeSpan PairingWindow = TimeSpan.FromMinutes(5);

    private static readonly string PathName = AppPaths.File("devices.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly object _lock = new();
    private readonly List<TrustedPhone> _phones;
    private byte[]? _token;
    private DateTimeOffset _tokenExpires;

    public DeviceStore() => _phones = Demo.Enabled ? [] : Load();

    public event Action? Changed;
    public event Action<TrustedPhone>? Paired;

    public IReadOnlyList<TrustedPhone> Phones
    {
        get { lock (_lock) return [.. _phones]; }
    }

    public TrustedPhone? Find(string id)
    {
        lock (_lock) return _phones.FirstOrDefault(p => p.Id == id);
    }

    public byte[] OpenPairingWindow()
    {
        lock (_lock)
        {
            _token = RandomNumberGenerator.GetBytes(Handshake.TokenSize);
            _tokenExpires = DateTimeOffset.UtcNow + PairingWindow;
            return _token;
        }
    }

    public void ClosePairingWindow()
    {
        lock (_lock) _token = null;
    }

    public bool IsTrusted(byte[] phonePublicKey) => Find(TrustedPhone.IdFor(phonePublicKey)) is not null;

    public byte[]? CurrentPairingToken()
    {
        lock (_lock) return _token is not null && DateTimeOffset.UtcNow < _tokenExpires ? _token : null;
    }

    public bool CompletePairing(byte[] token, byte[] phonePublicKey, HelloInfo hello)
    {
        TrustedPhone phone;
        lock (_lock)
        {
            if (_token is null || DateTimeOffset.UtcNow >= _tokenExpires || !CryptographicOperations.FixedTimeEquals(_token, token))
                return false;
            _token = null;
            var id = TrustedPhone.IdFor(phonePublicKey);
            _phones.RemoveAll(p => p.Id == id);
            phone = new TrustedPhone
            {
                Id = id,
                PublicKey = Convert.ToBase64String(phonePublicKey),
                Name = hello.Name,
                Model = hello.Model,
                PairedAt = DateTimeOffset.Now,
            };
            _phones.Add(phone);
            SaveLocked();
        }
        Log.Info($"paired phone {phone.Id} ({phone.Name})");
        Paired?.Invoke(phone);
        Changed?.Invoke();
        return true;
    }

    public void Update(string id, Action<TrustedPhone> change)
    {
        lock (_lock)
        {
            var phone = _phones.FirstOrDefault(p => p.Id == id);
            if (phone is null) return;
            change(phone);
            SaveLocked();
        }
        Changed?.Invoke();
    }

    public void Remove(string id)
    {
        lock (_lock)
        {
            _phones.RemoveAll(p => p.Id == id);
            SaveLocked();
        }
        Changed?.Invoke();
    }

    private static List<TrustedPhone> Load()
    {
        try
        {
            if (File.Exists(PathName))
                return JsonSerializer.Deserialize<List<TrustedPhone>>(File.ReadAllText(PathName)) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Log.Error("devices load failed", e);
        }
        return [];
    }

    public void AddDemo(TrustedPhone phone)
    {
        lock (_lock) _phones.Add(phone);
        Changed?.Invoke();
    }

    private void SaveLocked()
    {
        if (Demo.Enabled) return;
        try
        {
            File.WriteAllText(PathName, JsonSerializer.Serialize(_phones, Options));
        }
        catch (IOException e)
        {
            Log.Error("devices save failed", e);
        }
    }
}
