using Handsfreely.App.Infrastructure;
using Handsfreely.App.Views;
using Microsoft.UI.Xaml.Media;

namespace Handsfreely.App.Services;

public sealed class ContactPhotos(PhoneHub hub)
{
    private readonly Dictionary<string, byte[]?> _cache = [];

    public async Task<ImageSource?> GetAsync(string contactId)
    {
        try
        {
            if (!_cache.TryGetValue(contactId, out var data))
            {
                if (hub.Active is not { } s) return null;
                data = await s.Client.GetContactPhotoAsync(contactId, s.Token);
                _cache[contactId] = data;
            }
            return data is null ? null : await Images.FromBytesAsync(data);
        }
        catch (Exception e)
        {
            Log.Error("photo load failed", e);
            return null;
        }
    }

    public void Clear() => _cache.Clear();
}
