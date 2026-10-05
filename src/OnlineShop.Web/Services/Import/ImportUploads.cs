using System.Text.RegularExpressions;

namespace OnlineShop.Web.Services.Import;

/// <summary>
/// Файлы выгрузки между предпросмотром и записью в базу. Лежат в App_Data, а не в папке загрузок:
/// та раздаётся сайтом по адресу /media/, и прайс с оптовыми ценами можно было бы скачать.
/// В Django-версии временные файлы лежали именно там.
/// </summary>
public partial class ImportUploads(string dir)
{
    public async Task<string> SaveAsync(IFormFile file)
    {
        Directory.CreateDirectory(dir);
        // Если на предпросмотре нажали «Отмена», файл остаётся. Подчищаем такие хвосты старше суток.
        foreach (var stale in Directory.GetFiles(dir, "*.xlsx").Where(f => File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-1)))
        {
            File.Delete(stale);
        }
        var name = $"{Guid.NewGuid():N}.xlsx";
        await using var stream = File.Create(Path.Combine(dir, name));
        await file.CopyToAsync(stream);
        return name;
    }

    /// <summary>
    /// Полный путь к файлу по имени из формы предпросмотра. Принимаем только имена, которые выдали
    /// сами: иначе подставленным путём вроде ..\..\ можно было бы прочитать другой файл на сервере.
    /// </summary>
    public string? Find(string? name) =>
        name is not null && NameRegex().IsMatch(name) && File.Exists(Path.Combine(dir, name))
            ? Path.Combine(dir, name)
            : null;

    public void Delete(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return;
        }
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Файл ещё занят: его удалит очистка старых файлов при следующей загрузке.
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}\\.xlsx$")]
    private static partial Regex NameRegex();
}
