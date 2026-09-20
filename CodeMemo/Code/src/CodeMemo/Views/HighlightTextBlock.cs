using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CodeMemo.Services;

namespace CodeMemo.Views;

/// <summary>
/// 列表里带关键词高亮的文本：Source 是原文，Query 是搜索词，
/// 命中片段用半透明琥珀底色 + 加粗标出（明暗皮肤下都看得清）。
/// 不使用 TextBlock.Text 是因为它和 Inlines 会互相覆盖，自定义一个 Source 更干净。
/// </summary>
public sealed class HighlightTextBlock : TextBlock
{
    private static readonly Brush DefaultMatchBackground = CreateDefaultMatchBackground();

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(HighlightTextBlock),
        new FrameworkPropertyMetadata("", OnContentChanged));

    public static readonly DependencyProperty QueryProperty = DependencyProperty.Register(
        nameof(Query), typeof(string), typeof(HighlightTextBlock),
        new FrameworkPropertyMetadata("", OnContentChanged));

    public static readonly DependencyProperty MatchBackgroundProperty = DependencyProperty.Register(
        nameof(MatchBackground), typeof(Brush), typeof(HighlightTextBlock),
        new FrameworkPropertyMetadata(DefaultMatchBackground, OnContentChanged));

    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string Query
    {
        get => (string)GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    public Brush MatchBackground
    {
        get => (Brush)GetValue(MatchBackgroundProperty);
        set => SetValue(MatchBackgroundProperty, value);
    }

    private static Brush CreateDefaultMatchBackground()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xC1, 0x07));
        brush.Freeze();
        return brush;
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((HighlightTextBlock)d).Rebuild();

    private void Rebuild()
    {
        Inlines.Clear();
        foreach (var segment in HighlightText.Split(Source, Query))
        {
            var run = new Run(segment.Text);
            if (segment.IsMatch)
            {
                run.Background = MatchBackground;
                run.FontWeight = FontWeights.Bold;
            }
            Inlines.Add(run);
        }
    }
}
