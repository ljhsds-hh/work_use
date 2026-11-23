using System;
using System.Drawing;
using System.Windows.Forms;

namespace XiaoDing.UI
{
    /// <summary>自定义延后分钟数的输入对话框，卡片式自绘，与主界面同一套视觉语言。</summary>
    internal static class PromptDialog
    {
        public static bool TryAskMinutes(IWin32Window owner, string title, string message, int defaultValue, out int minutes)
        {
            minutes = 0;

            using (UiPromptForm form = new UiPromptForm(title, message, defaultValue))
            {
                // 提醒卡片与全屏遮罩都是置顶窗口，输入框必须同样置顶才不会被挡住。
                Form ownerForm = owner as Form;
                form.TopMost = ownerForm != null && ownerForm.TopMost;

                if (form.ShowDialog(owner) != DialogResult.OK)
                {
                    return false;
                }

                minutes = form.Minutes;
                return true;
            }
        }

        private sealed class UiPromptForm : UiCardDialog
        {
            private const int FormWidth = 340;

            private readonly string _title;
            private readonly string _message;
            private readonly UiTextField _input;

            public UiPromptForm(string title, string message, int defaultValue)
            {
                _title = title;
                _message = message;

                ClientSize = new Size(Theme.S(FormWidth), Theme.S(172));

                int left = Theme.S(26);
                int right = ClientSize.Width - Theme.S(26);

                _input = new UiTextField();
                _input.NumericOnly = true;
                _input.MaxLength = 4;
                _input.Text = Math.Max(1, Math.Min(1440, defaultValue)).ToString();
                _input.SetBounds(left, Theme.S(84), Theme.S(120), Theme.S(36));
                Controls.Add(_input);

                Label unit = new Label();
                unit.Text = "分钟";
                unit.ForeColor = Theme.TextMuted;
                unit.Font = Theme.FontMeta;
                unit.AutoSize = false;
                unit.BackColor = Color.Transparent;
                unit.TextAlign = ContentAlignment.MiddleLeft;
                unit.SetBounds(left + Theme.S(132), Theme.S(84), Theme.S(60), Theme.S(36));
                Controls.Add(unit);

                UiButton cancel = new UiButton();
                cancel.Text = "取消";
                cancel.SetBounds(right - Theme.S(84) - Theme.S(10) - Theme.S(84), Theme.S(124), Theme.S(84), Theme.S(34));
                cancel.Click += OnCancelClicked;
                Controls.Add(cancel);

                UiButton ok = new UiButton();
                ok.Primary = true;
                ok.Text = "确定";
                ok.SetBounds(right - Theme.S(84), Theme.S(124), Theme.S(84), Theme.S(34));
                ok.Click += delegate
                {
                    int value;
                    if (!int.TryParse(_input.Value, out value))
                    {
                        value = 15;
                    }

                    Minutes = Math.Max(1, Math.Min(1440, value));
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(ok);

                AcceptButton = ok;
                CancelButton = cancel;
            }

            public int Minutes { get; private set; }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                _input.FocusInput();
            }

            protected override void OnDrawContent(Graphics graphics)
            {
                int left = Theme.S(26);
                int textWidth = Width - left * 2;

                UiDraw.DrawText(
                    graphics,
                    _title,
                    Theme.FontTitle,
                    Theme.Text,
                    new RectangleF(left, Theme.S(24), textWidth, Theme.S(24)),
                    StringAlignment.Near);

                TextRenderer.DrawText(
                    graphics,
                    _message,
                    Theme.FontBody,
                    new Rectangle(left, Theme.S(52), textWidth, Theme.S(22)),
                    Theme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }
        }
    }
}
