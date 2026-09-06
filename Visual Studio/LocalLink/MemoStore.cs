using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalLink;

public sealed class MemoStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _lock = new();
    private readonly List<MemoEntry> _memos = new();

    public static string MemoPath => Path.Combine(LocalLinkSettings.SettingsDirectory, "memos.json");

    public static MemoStore LoadOrCreate()
    {
        Directory.CreateDirectory(LocalLinkSettings.SettingsDirectory);
        var store = new MemoStore();

        if (!File.Exists(MemoPath))
        {
            store.Save();
            return store;
        }

        var json = File.ReadAllText(MemoPath);
        var memos = JsonSerializer.Deserialize<List<MemoEntry>>(json, SerializerOptions) ?? new List<MemoEntry>();
        store._memos.AddRange(memos.Where(IsValid).Select(Normalize));
        return store;
    }

    public IReadOnlyList<MemoEntry> GetAll()
    {
        lock (_lock)
        {
            return _memos
                .OrderBy(memo => memo.Year)
                .ThenBy(memo => memo.Month)
                .ThenBy(memo => memo.Day)
                .ThenBy(memo => memo.Order)
                .ThenBy(memo => memo.Text)
                .Select(Clone)
                .ToArray();
        }
    }

    public IReadOnlyList<MemoEntry> GetByYear(int year)
    {
        lock (_lock)
        {
            return _memos
                .Where(memo => memo.Year == year)
                .OrderBy(memo => memo.Month)
                .ThenBy(memo => memo.Day)
                .ThenBy(memo => memo.Order)
                .ThenBy(memo => memo.Text)
                .Select(Clone)
                .ToArray();
        }
    }

    public MemoEntry Add(MemoEntry memo)
    {
        var normalized = Normalize(memo);

        lock (_lock)
        {
            if (ContainsDuplicate(normalized))
            {
                return Clone(_memos.First(existing => IsSameMemo(existing, normalized)));
            }

            normalized.Order = GetNextOrder(normalized.Year, normalized.Month, normalized.Day);
            _memos.Add(normalized);
            SaveCore();
            return Clone(normalized);
        }
    }

    public int AddRange(IEnumerable<MemoEntry> memos)
    {
        var added = 0;
        lock (_lock)
        {
            foreach (var memo in memos.Where(IsValid).Select(Normalize))
            {
                if (ContainsDuplicate(memo))
                {
                    continue;
                }

                memo.Order = GetNextOrder(memo.Year, memo.Month, memo.Day);
                _memos.Add(memo);
                added++;
            }

            if (added > 0)
            {
                SaveCore();
            }
        }

        return added;
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            var removed = _memos.RemoveAll(memo => string.Equals(memo.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                SaveCore();
            }

            return removed;
        }
    }

    public bool UpdateDayOrder(int year, int month, int day, IReadOnlyList<string> orderedIds)
    {
        lock (_lock)
        {
            var changed = false;
            for (var index = 0; index < orderedIds.Count; index++)
            {
                var memo = _memos.FirstOrDefault(item =>
                    item.Year == year &&
                    item.Month == month &&
                    item.Day == day &&
                    string.Equals(item.Id, orderedIds[index], StringComparison.OrdinalIgnoreCase));

                if (memo is not null && memo.Order != index)
                {
                    memo.Order = index;
                    changed = true;
                }
            }

            if (changed)
            {
                SaveCore();
            }

            return changed;
        }
    }

    public MemoEntry? Update(string id, string type, string text)
    {
        lock (_lock)
        {
            var memo = _memos.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (memo is null || string.IsNullOrWhiteSpace(text) || !IsSupportedType(type))
            {
                return null;
            }

            var updated = Clone(memo);
            updated.Type = NormalizeType(type);
            updated.Text = text.Trim();
            if (_memos.Any(existing => !string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase) && IsSameMemo(existing, updated)))
            {
                return null;
            }

            memo.Type = updated.Type;
            memo.Text = updated.Text;
            SaveCore();
            return Clone(memo);
        }
    }

    public MemoEntry? Move(string id, int year, int month, int day)
    {
        lock (_lock)
        {
            var memo = _memos.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (memo is null)
            {
                return null;
            }

            var targetYear = Math.Max(year, 1);
            var targetMonth = Math.Clamp(month, 1, 12);
            var targetDay = Math.Clamp(day, 1, DateTime.DaysInMonth(targetYear, targetMonth));

            if (memo.Year == targetYear && memo.Month == targetMonth && memo.Day == targetDay)
            {
                return Clone(memo);
            }

            var moved = Clone(memo);
            moved.Year = targetYear;
            moved.Month = targetMonth;
            moved.Day = targetDay;
            if (ContainsDuplicate(moved))
            {
                return null;
            }

            memo.Year = targetYear;
            memo.Month = targetMonth;
            memo.Day = targetDay;
            memo.Order = GetNextOrder(targetYear, targetMonth, targetDay);
            SaveCore();
            return Clone(memo);
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            SaveCore();
        }
    }

    private void SaveCore()
    {
        Directory.CreateDirectory(LocalLinkSettings.SettingsDirectory);
        File.WriteAllText(MemoPath, JsonSerializer.Serialize(_memos, SerializerOptions));
    }

    private static bool IsValid(MemoEntry memo)
    {
        return memo.Year > 0 &&
            memo.Month is >= 1 and <= 12 &&
            memo.Day is >= 1 and <= 31 &&
            IsSupportedType(memo.Type) &&
            !string.IsNullOrWhiteSpace(memo.Text);
    }

    private static MemoEntry Normalize(MemoEntry memo)
    {
        return new MemoEntry
        {
            Id = string.IsNullOrWhiteSpace(memo.Id) ? Guid.NewGuid().ToString("N") : memo.Id,
            Year = memo.Year,
            Month = Math.Clamp(memo.Month, 1, 12),
            Day = Math.Clamp(memo.Day, 1, DateTime.DaysInMonth(Math.Max(memo.Year, 1), Math.Clamp(memo.Month, 1, 12))),
            Type = NormalizeType(memo.Type),
            Text = memo.Text.Trim(),
            Order = Math.Max(0, memo.Order)
        };
    }

    private int GetNextOrder(int year, int month, int day)
    {
        var dayMemos = _memos.Where(memo => memo.Year == year && memo.Month == month && memo.Day == day);
        return dayMemos.Any() ? dayMemos.Max(memo => memo.Order) + 1 : 0;
    }

    private bool ContainsDuplicate(MemoEntry memo)
    {
        return _memos.Any(existing => IsSameMemo(existing, memo));
    }

    private static bool IsSameMemo(MemoEntry left, MemoEntry right)
    {
        return left.Year == right.Year &&
            left.Month == right.Month &&
            left.Day == right.Day &&
            string.Equals(left.Type, right.Type, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Text, right.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeType(string type)
    {
        var normalized = type.Trim().ToLowerInvariant();
        return IsSupportedType(normalized)
            ? normalized
            : "event";
    }

    private static bool IsSupportedType(string type)
    {
        var normalized = type.Trim().ToLowerInvariant();
        return normalized is "holiday" or "event" or "item";
    }

    private static MemoEntry Clone(MemoEntry memo)
    {
        return new MemoEntry
        {
            Id = memo.Id,
            Year = memo.Year,
            Month = memo.Month,
            Day = memo.Day,
            Type = memo.Type,
            Text = memo.Text,
            Order = memo.Order
        };
    }
}

public sealed class MemoEntry
{
    public string Id { get; set; } = "";

    public int Year { get; set; }

    public int Month { get; set; }

    public int Day { get; set; }

    public string Type { get; set; } = "event";

    public string Text { get; set; } = "";

    public int Order { get; set; }
}
