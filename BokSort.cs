// BokSort.cs — сортировщик и валидатор боковины (.bok)
// .NET 6+, консоль.
//
// Использование:
//   dotnet run --project BokSort -- bokrai_bs5.txt sorted_bokrai_bs5.txt
//   dotnet run --project BokSort -- --fix input.bok output.bok

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BokSort;

public enum BokKind { T, C, R, P, Width, Unknown }

public sealed class BokLine
{
    public BokKind Kind { get; init; }
    public string Text { get; init; } = "";
    public int? N { get; init; }
    public int? M { get; init; }
    public int? Width { get; init; }
    public bool NewSheet { get; init; }          // флаг L после //
    public string Raw { get; init; } = "";
    public int LineNo { get; init; }

    public override string ToString() => Raw;
}

public static class BokParser
{
    // т/T/с/C + N + [/M] + // + [L]
    private static readonly Regex ReData = new(
        @"^(?<text>.*?)/(?<kind>[тTсC])(?<n>\d+)(?:/(?<m>\d+))?//(?<L>L)?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex ReR = new(
        @"^(?<text>.*?)/R//(?<L>L)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ReP = new(
        @"^(?<text>.*?)/П//(?<L>L)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ReWidth = new(
        @"^\*\s*(?<w>\d+)\s*/\s*$",
        RegexOptions.Compiled);

    public static BokLine Parse(string raw, int lineNo)
    {
        string s = raw.TrimEnd();
        if (string.IsNullOrWhiteSpace(s))
            return new BokLine { Kind = BokKind.Unknown, Raw = raw, LineNo = lineNo };

        var mw = ReWidth.Match(s);
        if (mw.Success)
        {
            return new BokLine
            {
                Kind = BokKind.Width,
                Width = int.Parse(mw.Groups["w"].Value),
                Raw = raw, LineNo = lineNo
            };
        }

        var md = ReData.Match(s);
        if (md.Success)
        {
            string k = md.Groups["kind"].Value;
            var kind = (k is "т" or "T") ? BokKind.T : BokKind.C;
            return new BokLine
            {
                Kind = kind,
                Text = md.Groups["text"].Value,
                N = int.Parse(md.Groups["n"].Value),
                M = md.Groups["m"].Success ? int.Parse(md.Groups["m"].Value) : null,
                NewSheet = md.Groups["L"].Success,
                Raw = raw, LineNo = lineNo
            };
        }

        var mr = ReR.Match(s);
        if (mr.Success)
            return new BokLine
            {
                Kind = BokKind.R,
                Text = mr.Groups["text"].Value,
                NewSheet = mr.Groups["L"].Success,
                Raw = raw, LineNo = lineNo
            };

        var mp = ReP.Match(s);
        if (mp.Success)
            return new BokLine
            {
                Kind = BokKind.P,
                Text = mp.Groups["text"].Value,
                NewSheet = mp.Groups["L"].Success,
                Raw = raw, LineNo = lineNo
            };

        return new BokLine { Kind = BokKind.Unknown, Raw = raw, LineNo = lineNo };
    }

    public static List<BokLine> ParseFile(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var list = new List<BokLine>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
            list.Add(Parse(lines[i], i + 1));
        return list;
    }
}

public static class BokChecker
{
    public static (List<string> Errors, List<string> Warnings) Check(List<BokLine> items)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var seen = new HashSet<(BokKind, int, int)>();

        foreach (var it in items)
        {
            switch (it.Kind)
            {
                case BokKind.Unknown:
                    warnings.Add($"W01 строка {it.LineNo}: не распознана → «{it.Raw}»");
                    break;

                case BokKind.T:
                case BokKind.C:
                    if (it.N is null or <= 0)
                        errors.Add($"E01 строка {it.LineNo}: N должно быть > 0 → «{it.Raw}»");
                    if (it.M is < 0)
                        errors.Add($"E02 строка {it.LineNo}: M должно быть >= 0 → «{it.Raw}»");
                    if (it.Kind == BokKind.C)
                        warnings.Add($"W02 строка {it.LineNo}: строка сравнения (АППГ) → «{it.Raw}»");
                    if (it.Raw != it.Raw.TrimEnd())
                        warnings.Add($"W03 строка {it.LineNo}: хвостовые пробелы → «{it.Raw}»");

                    var key = (it.Kind, it.N ?? 0, it.M ?? -1);
                    if (!seen.Add(key))
                        warnings.Add($"W06 строка {it.LineNo}: дубликат ({it.Kind}{it.N}/{it.M}) → «{it.Raw}»");
                    break;

                case BokKind.R:
                    if (string.IsNullOrWhiteSpace(it.Text))
                        errors.Add($"E03 строка {it.LineNo}: пустой разделитель → «{it.Raw}»");
                    break;

                case BokKind.P:
                    if (it.Raw.Contains('Y') || it.Raw.Contains('V'))
                        errors.Add($"E07 строка {it.LineNo}: символ Y/V вместо ╧ → «{it.Raw}»");
                    break;

                case BokKind.Width:
                    if (it.Width is null or <= 0)
                        errors.Add($"E05 строка {it.LineNo}: ширина должна быть > 0 → «{it.Raw}»");
                    break;
            }
        }
        return (errors, warnings);
    }
}

public static class BokSorter
{
    /// <summary>
    /// Сортирует data-строки по (N, M, T→C), остальные оставляет на местах.
    /// </summary>
    public static List<BokLine> Sort(List<BokLine> items)
    {
        var data = items.Where(x => x.Kind is BokKind.T or BokKind.C)
                        .OrderBy(x => x.N ?? int.MaxValue)
                        .ThenBy(x => x.M ?? -1)
                        .ThenBy(x => x.Kind == BokKind.T ? 0 : 1)
                        .ToList();

        var result = new List<BokLine>(items.Count);
        int idx = 0;
        foreach (var it in items)
        {
            if (it.Kind is BokKind.T or BokKind.C)
                result.Add(data[idx++]);
            else
                result.Add(it);
        }
        return result;
    }

    public static (List<int> Nums, List<int> Missing) BuildHeader(List<BokLine> sorted)
    {
        var nums = sorted.Where(x => x.Kind is BokKind.T or BokKind.C)
                         .Select(x => x.N ?? 0)
                         .Where(n => n > 0)
                         .Distinct()
                         .OrderBy(n => n)
                         .ToList();

        var missing = new List<int>();
        if (nums.Count > 0)
        {
            var full = Enumerable.Range(1, nums.Max()).ToHashSet();
            missing = full.Except(nums).OrderBy(n => n).ToList();
        }
        return (nums, missing);
    }
}

public static class BokSerializer
{
    public static string Serialize(BokLine it)
    {
        string L = it.NewSheet ? "L" : "";
        return it.Kind switch
        {
            BokKind.T or BokKind.C => $"{it.Text}/{(it.Kind == BokKind.T ? "T" : "C")}{it.N}"
                                     + (it.M.HasValue ? $"/{it.M}" : "")
                                     + $"//{L}",
            BokKind.R => $"{it.Text}/R//{L}",
            BokKind.P => $"{it.Text}/П//{L}",
            BokKind.Width => $"*{it.Width}/",
            _ => it.Raw
        };
    }
}

public static class BokFixer
{
    /// <summary>Режим --fix: нормализация, устранение W03/W04/E07.</summary>
    public static List<BokLine> Fix(List<BokLine> items)
    {
        var result = new List<BokLine>(items.Count);
        foreach (var it in items)
        {
            // F02: убрать хвостовые пробелы
            string raw = it.Raw.TrimEnd();

            // F04: Y/V → ╧ в /П//-строках
            if (it.Kind == BokKind.P)
                raw = raw.Replace('Y', '╧').Replace('V', '╧');

            // F03/F05 нормализация уже в сериализации
            var fixedIt = BokParser.Parse(raw, it.LineNo);
            result.Add(fixedIt);
        }
        return result;
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        bool doFix = args.Contains("--fix");
        var files = args.Where(a => !a.StartsWith("--")).ToArray();

        string src = files.Length > 0 ? files[0] : "bokrai_bs5.txt";
        string dst = files.Length > 1 ? files[1] : "sorted_" + src;

        if (!File.Exists(src))
        {
            Console.Error.WriteLine($"Файл не найден: {src}");
            return 2;
        }

        var items = BokParser.ParseFile(src);
        Console.WriteLine($"BokSort: «{src}» — {items.Count} строк");

        if (doFix)
        {
            items = BokFixer.Fix(items);
            Console.WriteLine("Режим --fix: нормализация выполнена.");
        }

        // 1) Проверка
        var (errors, warnings) = BokChecker.Check(items);
        if (errors.Count > 0)
        {
            Console.WriteLine($"\nОШИБКИ ({errors.Count}):");
            foreach (var e in errors) Console.WriteLine("  " + e);
        }
        if (warnings.Count > 0)
        {
            Console.WriteLine($"\nПРЕДУПРЕЖДЕНИЯ ({warnings.Count}):");
            foreach (var w in warnings.Take(30)) Console.WriteLine("  " + w);
            if (warnings.Count > 30) Console.WriteLine($"  … ещё {warnings.Count - 30}");
        }
        if (errors.Count == 0 && warnings.Count == 0)
            Console.WriteLine("Написание боковины корректно.");

        // 2) Сортировка
        var sorted = BokSorter.Sort(items);
        int dataCount = sorted.Count(x => x.Kind is BokKind.T or BokKind.C);
        Console.WriteLine($"\nСтрок с данными: {dataCount}");
        var preview = sorted.Where(x => x.Kind is BokKind.T or BokKind.C)
                            .Take(20)
                            .Select(x => $"{x.Kind}{x.N}");
        Console.WriteLine("  " + string.Join("  ", preview));

        // 3) Шапка
        var (nums, missing) = BokSorter.BuildHeader(sorted);
        Console.WriteLine($"\nНомера N ({nums.Count}): {string.Join(", ", nums.Take(30))}"
                          + (nums.Count > 30 ? " …" : ""));
        if (missing.Count > 0)
            Console.WriteLine($"  ПРОПУЩЕНЫ N: {string.Join(", ", missing)}");

        // 4) Запись
        var output = sorted.Select(BokSerializer.Serialize);
        File.WriteAllLines(dst, output, new UTF8Encoding(false));
        Console.WriteLine($"\nЗаписано: «{dst}»");

        var widths = items.Where(x => x.Kind == BokKind.Width)
                          .Select(x => x.Width!.Value).ToList();
        if (widths.Count > 0)
            Console.WriteLine($"Ширины столбцов: {string.Join(", ", widths)}");

        return errors.Count > 0 ? 1 : 0;
    }
}