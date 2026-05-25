using System.Text.Json;

namespace Matarchive.Web.Infrastructure;

public sealed class JsonFileStore<T>
{
    private readonly string _path;
    private readonly Func<T> _defaultFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public JsonFileStore(string path, Func<T> defaultFactory)
    {
        _path = path;
        _defaultFactory = defaultFactory;
    }

    public async Task<T> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                return _defaultFactory();
            }

            await using var stream = File.OpenRead(_path);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken);
            return value is null ? _defaultFactory() : value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(T value, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_path}.tmp";

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, _options, cancellationToken);
            }

            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            File.Move(tempPath, _path);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            _gate.Release();
        }
    }
}

