using System.IO;
using CreateTpl.Services;
using Xunit;

namespace CreateTpl.Tests;

/// <summary>InputParser 输入解析与校验规则单元测试（对应需求 3.1 / 3.6）。</summary>
public class InputParserTests
{
    // ─────────────── Parse：分行解析 ───────────────

    [Fact]
    public void Parse_MultipleLines_TrimsWhitespaceAndIgnoresEmptyLines()
    {
        var result = InputParser.Parse("  MyApp  \r\n\r\n工具箱\n   \nDemo");

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "MyApp", "工具箱", "Demo" }, result.Names);
    }

    [Fact]
    public void Parse_DuplicateNames_CaseInsensitiveDedupe_KeepsFirst()
    {
        var result = InputParser.Parse("MyApp\nMYAPP\nmyapp\nOther");

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "MyApp", "Other" }, result.Names);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsError()
    {
        var result = InputParser.Parse("\n   \n");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("至少输入一个工程名称"));
    }

    // ─────────────── Parse：逐行校验 ───────────────

    [Theory]
    [InlineData("a<b")]
    [InlineData("c/d")]
    [InlineData("e:f")]
    [InlineData("g*h")]
    [InlineData("i?j")]
    [InlineData("k\"l")]
    [InlineData("m<n")]
    [InlineData("o|p")]
    [InlineData("q\\r")]
    public void Parse_InvalidCharacters_ReturnsError(string name)
    {
        var result = InputParser.Parse(name);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("不允许的字符"));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void Parse_ReservedDeviceNames_ReturnsError(string name)
    {
        var result = InputParser.Parse(name);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("保留设备名"));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("abc.")]
    public void Parse_SpecialOrDotEndingNames_ReturnsError(string name)
    {
        var result = InputParser.Parse(name);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateName_TrailingSpaceOrDot_ReturnsError()
    {
        // 直接调用校验层：尾随空格/点号是 Windows 目录名硬限制
        //（Parse 入口会先 Trim 清理尾随空格，故该规则在 ValidateName 层体现）
        Assert.NotNull(InputParser.ValidateName("abc "));
        Assert.NotNull(InputParser.ValidateName("abc."));
    }

    [Fact]
    public void Parse_NameTooLong_ReturnsError()
    {
        var result = InputParser.Parse(new string('a', InputParser.MaxNameLength + 1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("长度超过"));
    }

    [Fact]
    public void Parse_ValidChineseAndEnglishNames_Accepted()
    {
        var result = InputParser.Parse("我的工程\nMyProject123\nMy_Project-1.0");

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Names.Count);
    }

    [Fact]
    public void Parse_OverBatchLimit_ReturnsError()
    {
        var input = string.Join("\n", Enumerable.Range(1, InputParser.MaxBatchCount + 1).Select(i => $"Proj{i}"));
        var result = InputParser.Parse(input);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("数量不能超过"));
    }

    [Fact]
    public void Parse_ErrorReportsLineNumber()
    {
        var result = InputParser.Parse("OK1\nbad<name\nOK2");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("第 2 行"));
    }

    // ─────────────── ValidateRootPath：根目录校验 ───────────────

    [Fact]
    public void ValidateRootPath_Empty_ReturnsError()
    {
        Assert.NotNull(InputParser.ValidateRootPath(""));
        Assert.NotNull(InputParser.ValidateRootPath("   "));
    }

    [Fact]
    public void ValidateRootPath_RelativePath_ReturnsError()
    {
        Assert.NotNull(InputParser.ValidateRootPath("Projects"));
    }

    [Fact]
    public void ValidateRootPath_ForwardSlashStyle_Accepted()
    {
        // 需求示例格式：D:/Projects/
        var drive = Path.GetTempPath().Substring(0, 3); // 如 "C:\"
        Assert.Null(InputParser.ValidateRootPath(drive.Replace('\\', '/') + "SomeDir/"));
    }

    [Fact]
    public void ValidateRootPath_NonExistentDrive_ReturnsError()
    {
        Assert.NotNull(InputParser.ValidateRootPath("Q:\\NoSuchDrive\\Projects"));
    }

    [Fact]
    public void ValidateRootPath_IllegalCharacters_ReturnsError()
    {
        Assert.NotNull(InputParser.ValidateRootPath("D:\\Proje<cts"));
    }

    [Fact]
    public void ValidateRootPath_ValidAbsolutePath_ReturnsNull()
    {
        var drive = Path.GetTempPath().Substring(0, 3);
        Assert.Null(InputParser.ValidateRootPath(drive + "Projects"));
    }
}
