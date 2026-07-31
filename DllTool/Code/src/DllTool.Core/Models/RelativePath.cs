namespace DllTool.Core.Models;

/// <summary>
/// 文件系统条目的相对路径封装，提供大小写敏感的路径比较。
/// </summary>
public sealed class RelativePath : IEquatable<RelativePath>, IComparable<RelativePath>
{
    /// <summary>相对路径，使用反斜杠作为分隔符，不含前导/尾随分隔符。</summary>
    public string Value { get; }

    public RelativePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Replace('/', '\\').Trim('\\');
    }

    /// <summary>目录部分（不含文件名），根目录文件返回空字符串。</summary>
    public string DirectoryPart
    {
        get
        {
            var idx = Value.LastIndexOf('\\');
            return idx < 0 ? string.Empty : Value[..idx];
        }
    }

    /// <summary>纯文件名部分。</summary>
    public string FileName
    {
        get
        {
            var idx = Value.LastIndexOf('\\');
            return idx < 0 ? Value : Value[(idx + 1)..];
        }
    }

    /// <summary>在相对路径下追加一个相对子路径。</summary>
    public RelativePath Combine(string child) => new(Value + '\\' + child.Replace('/', '\\').Trim('\\'));

    /// <summary>大小写敏感比较。</summary>
    public bool Equals(RelativePath? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is RelativePath other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public int CompareTo(RelativePath? other) => string.Compare(Value, other?.Value, StringComparison.Ordinal);

    public static bool operator ==(RelativePath? left, RelativePath? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(RelativePath? left, RelativePath? right) => !(left == right);

    public override string ToString() => Value;
}
