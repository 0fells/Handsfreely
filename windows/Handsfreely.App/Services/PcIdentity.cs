using Handsfreely.App.Infrastructure;
using Handsfreely.Core.Crypto;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace Handsfreely.App.Services;

public static class PcIdentity
{
    private static readonly string PathName = AppPaths.File("pc.key");

    public static async Task<P256KeyPair> LoadOrCreateAsync()
    {
        var provider = new DataProtectionProvider("LOCAL=user");
        if (File.Exists(PathName))
        {
            try
            {
                var sealedKey = CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(PathName));
                var plain = await provider.UnprotectAsync(sealedKey);
                CryptographicBuffer.CopyToByteArray(plain, out var d);
                return P256KeyPair.FromPrivateKey(d);
            }
            catch (Exception e)
            {
                Log.Error("pc key unreadable, creating a new one", e);
            }
        }

        var key = P256KeyPair.Generate();
        var protectedKey = await provider.ProtectAsync(CryptographicBuffer.CreateFromByteArray(key.ExportPrivateKey()));
        CryptographicBuffer.CopyToByteArray(protectedKey, out var bytes);
        await File.WriteAllBytesAsync(PathName, bytes);
        return key;
    }
}
