namespace CodeMemo.Services;

/// <summary>一段文本片段；IsMatch 为 true 表示这段命中了搜索词，需要高亮。</summary>
public sealed record HighlightSegment(string Text, bool IsMatch);

/// <summary>
/// 搜索关键词高亮切分（纯函数，可单测）。
/// 关键词按空白拆分（与搜索同一套规则），大小写不敏感，允许一个词出现多次；
/// 相邻或重叠的命中区间会合并成一段，保证生成高亮片段时不会切碎。
/// </summary>
public static class HighlightText
{
    public static IReadOnlyList<HighlightSegment> Split(string? text, string? query)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var ranges = MatchRanges(text, CommandSearch.SplitTokens(query));
        if (ranges.Count == 0)
        {
            return [new HighlightSegment(text, false)];
        }

        var segments = new List<HighlightSegment>();
        var cursor = 0;
        foreach (var (start, end) in Merge(ranges))
        {
            if (start > cursor)
            {
                segments.Add(new HighlightSegment(text[cursor..start], false));
            }
            segments.Add(new HighlightSegment(text[start..end], true));
            cursor = end;
        }
        if (cursor < text.Length)
        {
            segments.Add(new HighlightSegment(text[cursor..], false));
        }
        return segments;
    }

    private static List<(int Start, int End)> MatchRanges(string text, IReadOnlyList<string> tokens)
    {
        var ranges = new List<(int Start, int End)>();
        foreach (var token in tokens)
        {
            if (token.Length == 0)
            {
                continue;
            }

            var searchFrom = 0;
            while (searchFrom <= text.Length - token.Length)
            {
                var hit = text.IndexOf(token, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (hit < 0)
                {
                    break;
                }
                ranges.Add((hit, hit + token.Length));
                searchFrom = hit + token.Length;
            }
        }
        return ranges;
    }

    private static List<(int Start, int End)> Merge(List<(int Start, int End)> ranges)
    {
        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<(int Start, int End)>();
        foreach (var range in ranges)
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
            }
            else
            {
                merged.Add(range);
            }
        }
        return merged;
    }
}
