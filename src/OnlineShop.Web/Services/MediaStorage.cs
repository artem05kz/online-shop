namespace OnlineShop.Web.Services;

/// <summary>
/// Файлы в папке загрузок: фото товаров и баннеров. В базе лежит путь относительно этой папки,
/// например products/2026/10/131122.jpg. Старый файл удаляется, когда фото заменили или убрали
/// (в Django-версии это делал пакет django-cleanup).
/// </summary>
public class MediaStorage(string root)
{
    private static readonly Dictionary<string, byte[][]> Signatures = new()
    {
        [".jpg"] = [[0xFF, 0xD8, 0xFF]],
        [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
        [".png"] = [[0x89, 0x50, 0x4E, 0x47]],
        [".gif"] = [[0x47, 0x49, 0x46, 0x38]],
        [".webp"] = [[0x52, 0x49, 0x46, 0x46]],
    };

    public string Root => root;

    public static IReadOnlyCollection<string> AllowedExtensions => Signatures.Keys;

    /// <summary>
    /// Проверка, что это действительно картинка: и по расширению, и по первым байтам файла.
    /// Иначе под видом фото можно было бы положить на сайт что угодно.
    /// </summary>
    public static bool LooksLikeImage(string fileName, byte[] head)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return Signatures.TryGetValue(ext, out var variants)
            && variants.Any(sig => head.Length >= sig.Length && head.AsSpan(0, sig.Length).SequenceEqual(sig));
    }

    /// <summary>Сохраняет файл в папку вида products/2026/10/ и возвращает путь для базы.</summary>
    public async Task<string> SaveAsync(byte[] content, string folder, string fileName)
    {
        var now = DateTime.UtcNow;
        var relativeDir = $"{folder}/{now:yyyy}/{now:MM}";
        var dir = Path.Combine(root, relativeDir);
        Directory.CreateDirectory(dir);

        var name = SafeName(fileName);
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        // Имя занято: добавляем короткий случайный хвост, как Django.
        while (File.Exists(Path.Combine(dir, name)))
        {
            name = $"{stem}_{Guid.NewGuid().ToString("N")[..7]}{ext}";
        }
        await File.WriteAllBytesAsync(Path.Combine(dir, name), content);
        return $"{relativeDir}/{name}";
    }

    /// <summary>Полный путь к файлу в папке загрузок или null, если путь пытается выйти за её пределы.</summary>
    public string? FullPath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return null;
        }
        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, relativePath));
        return full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>Удаляет файл и его уменьшенные копии (см. <see cref="Thumbnails"/>).</summary>
    public void Delete(string? relativePath)
    {
        // Удаляем только внутри папки загрузок, что бы ни оказалось в базе.
        if (FullPath(relativePath) is not { } full)
        {
            return;
        }
        if (File.Exists(full))
        {
            File.Delete(full);
        }
        foreach (var size in Thumbnails.Sizes)
        {
            if (FullPath($"{Thumbnails.Folder}/{size}/{relativePath}.webp") is { } copy && File.Exists(copy))
            {
                File.Delete(copy);
            }
        }
    }

    private static string SafeName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return name.Length == 0 ? "file" : name;
    }
}
