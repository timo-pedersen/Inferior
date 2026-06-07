using Inferior.Persistence.Data;
using System.Text.Json;

namespace Inferior.Persistence;

public sealed class LocalFileShipRepository : IShipRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _careerDirectory;

    public LocalFileShipRepository(string careerDirectory)
    {
        _careerDirectory = careerDirectory;
    }

    private string ShipPath(string shipId)
        => Path.Combine(_careerDirectory, "ships", shipId, "ship.json");

    public async Task<ShipRecord?> GetAsync(string shipId)
    {
        var path = ShipPath(shipId);
        if (!File.Exists(path)) return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ShipRecord>(stream, JsonOptions);
    }

    public async Task SaveAsync(ShipRecord record)
    {
        var path = ShipPath(record.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, record, JsonOptions);
    }

    public Task DeleteAsync(string shipId)
    {
        var path = ShipPath(shipId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ShipSummary>> ListAsync()
    {
        var shipsDir = Path.Combine(_careerDirectory, "ships");
        if (!Directory.Exists(shipsDir))
            return Task.FromResult<IReadOnlyList<ShipSummary>>([]);

        var summaries = new List<ShipSummary>();
        foreach (var dir in Directory.EnumerateDirectories(shipsDir))
        {
            var shipFile = Path.Combine(dir, "ship.json");
            if (!File.Exists(shipFile)) continue;

            try
            {
                using var doc  = JsonDocument.Parse(File.ReadAllText(shipFile));
                var root = doc.RootElement;
                summaries.Add(new ShipSummary
                {
                    Id         = root.GetProperty("id").GetString()         ?? "",
                    HullTypeId = root.GetProperty("hullTypeId").GetString() ?? "",
                    Name       = root.TryGetProperty("name", out var n) ? n.GetString() : null,
                });
            }
            catch { /* skip corrupted files */ }
        }

        return Task.FromResult<IReadOnlyList<ShipSummary>>(summaries);
    }
}
