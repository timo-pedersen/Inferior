using Inferior.Persistence.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Inferior.Persistence;

public sealed class LocalFileShipLogRepository : IShipLogRepository
{
    private const int PageSizeLimit = 32 * 1024; // 32 KB

    private readonly string _careerDirectory;
    private readonly string _commanderId;

    public LocalFileShipLogRepository(string careerDirectory, string commanderId)
    {
        _careerDirectory = careerDirectory;
        _commanderId     = commanderId;
    }

    private string LogDirectory(string shipId)
        => Path.Combine(_careerDirectory, "ships", shipId);

    private string[] GetLogPages(string shipId)
    {
        var dir = LogDirectory(shipId);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "log-*.ndjson").OrderBy(f => f).ToArray();
    }

    private string CurrentPagePath(string shipId)
    {
        var pages = GetLogPages(shipId);
        if (pages.Length == 0)
            return Path.Combine(LogDirectory(shipId), "log-0001.ndjson");

        var last = pages[^1];
        if (new FileInfo(last).Length >= PageSizeLimit)
        {
            var stem = Path.GetFileNameWithoutExtension(last);
            var num  = int.Parse(stem["log-".Length..]);
            return Path.Combine(LogDirectory(shipId), $"log-{num + 1:D4}.ndjson");
        }
        return last;
    }

    private string GetLastHash(string shipId)
    {
        var pages = GetLogPages(shipId);
        for (int i = pages.Length - 1; i >= 0; i--)
        {
            var lastLine = File.ReadAllLines(pages[i])
                               .LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (lastLine == null) continue;

            var entry = JsonSerializer.Deserialize<LogEntryRecord>(lastLine);
            if (entry?.Hash is { Length: > 0 } h) return h;
        }
        return HashString(_commanderId);
    }

    private static string ComputeEntryHash(LogEntryRecord entry, string previousHash)
    {
        var input = entry.Text + entry.Date.ToString("O") + entry.Type + previousHash;
        return HashString(input);
    }

    private static string HashString(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task AppendAsync(string shipId, LogEntryRecord entry)
    {
        var logDir = LogDirectory(shipId);
        Directory.CreateDirectory(logDir);

        var previousHash  = GetLastHash(shipId);
        var hashedEntry   = entry with { Hash = ComputeEntryHash(entry, previousHash) };
        var line          = JsonSerializer.Serialize(hashedEntry) + "\n";

        await File.AppendAllTextAsync(CurrentPagePath(shipId), line);
    }

    public Task<IReadOnlyList<LogEntryRecord>> GetRecentAsync(string shipId, int count = 50)
    {
        var pages   = GetLogPages(shipId);
        var entries = new List<LogEntryRecord>();

        for (int i = pages.Length - 1; i >= 0 && entries.Count < count; i--)
        {
            var lines = File.ReadAllLines(pages[i]);
            foreach (var line in lines.Reverse())
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var e = JsonSerializer.Deserialize<LogEntryRecord>(line);
                if (e != null) entries.Add(e);
                if (entries.Count >= count) break;
            }
        }

        entries.Reverse();
        return Task.FromResult<IReadOnlyList<LogEntryRecord>>(entries);
    }

    public Task<IReadOnlyList<LogEntryRecord>> GetAllAsync(string shipId)
    {
        var entries = new List<LogEntryRecord>();
        foreach (var page in GetLogPages(shipId))
        {
            foreach (var line in File.ReadAllLines(page))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var e = JsonSerializer.Deserialize<LogEntryRecord>(line);
                if (e != null) entries.Add(e);
            }
        }
        return Task.FromResult<IReadOnlyList<LogEntryRecord>>(entries);
    }

    public Task DeleteAllAsync(string shipId)
    {
        foreach (var page in GetLogPages(shipId))
            File.Delete(page);
        return Task.CompletedTask;
    }

    public async Task ValidateLog(string shipId)
    {
        var all          = await GetAllAsync(shipId);
        var previousHash = HashString(_commanderId);

        for (int i = 0; i < all.Count; i++)
        {
            var entry    = all[i];
            var expected = ComputeEntryHash(entry, previousHash);
            if (expected != entry.Hash)
                throw new InvalidDataException(
                    $"Hash mismatch at log entry {i} ({entry.Date:O}): " +
                    $"expected {expected}, got {entry.Hash}");
            previousHash = entry.Hash;
        }
    }
}
