using System;
using System.Drawing;
using System.Windows.Forms;

namespace XiaoDing.UI
{
    internal enum UiMessageIcon
    {
        Info,
        Warn,
        Question
    }

    /// <summary>
    /// 自绘消息框，替代系统 MessageBox：圆角卡片、品牌配色、主次按钮，
    /// 与主界面保持同一套视觉语言。
    /// </summary>
    internal static class UiMessageBox
    {
        public static void Info(IWin32Window owner, string message)
        {
            Show(owner, "提示", message, UiMessageIcon.Info, "知道了", null, false);
        }

        public static void Warn(IWin32Window owner, string message)
        {
            Show(owner, "提醒", message, UiMessageIcon.Warn, "知道了", null, false);
        }

        /// <summary>确认框：返回 true 表示用户点了确认。</summary>
        public static bool Confirm(IWin32Window owner, string message, string confirmText, bool danger)
        {
            return Show(owner, "请确认", message, UiMessageIcon.Question, confirmText, "取消", danger);
        }

        private static bool Show(IWin32Window owner, string title, string message, UiMessageIcon icon, string confirmText, string cancelText, bool danger)
        {
            using (UiMessageForm form = new UiMessageForm(title, message, icon, confirmText, cancelText, danger))
            {
                // 提醒卡片/遮罩是置顶窗口，从它们弹出的对话框必须同样置顶才不会被盖住。
                Form ownerForm = owner as Form;
                form.TopMost = ownerForm != null && ownerForm.TopMost;
                return form.ShowDialog(owner) == DialogResult.OK;
            }
        }

        private sealed class UiMessageForm : UiCardDialog
        {
            private const int FormWidth = 400;

            private readonly string _title;
            private readonly string _message;
            private readonly UiMessageIcon _icon;
            private readonly UiButton _confirm;
            private readonly UiButton _cancel;
            private readonly int _messageHeight;

            public UiMessageForm(string title, string message, UiMessageIcon icon, string confirmText, string cancelText, bool danger)
            {
                _title = title;
                _message = message ?? string.Empty;
                _icon = icon;

                int textWidth = Theme.S(FormWidth - 40 - 26);
                _messageHeight = MeasureHeight(_message, textWidth);
                int height = Theme.S(24 + 22 + 6) + _messageHeight + Theme.S(18 + 34 + 22);

                ClientSize = new Size(Theme.S(FormWidth), height);

                int buttonHeight = Theme.S(34);
                int buttonY = ClientSize.Height - Theme.S(22) - buttonHeight;

                _confirm = new UiButton();
                _confirm.Primary = true;
                _confirm.Danger = danger;
                _confirm.Text = confirmText;
                int confirmWidth = Math.Max(Theme.S(96), MeasureButton(confirmText));
                _confirm.SetBounds(ClientSize.Width - Theme.S(26) - confirmWidth, buttonY, confirmWidth, buttonHeight);
                _confirm.Click += delegate
                {
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(_confirm);

                if (!string.IsNullOrEmpty(cancelText))
                {
                    _cancel = new UiButton();
                    _cancel.Text = cancelText;
                    int cancelWidth = Math.Max(Theme.S(84), MeasureButton(cancelText));
                    _cancel.SetBounds(_confirm.Left - Theme.S(10) - cancelWidth, buttonY, cancelWidth, buttonHeight);
                    _cancel.Click += OnCancelClicked;
                    Controls.Add(_cancel);
                    CancelButton = _cancel;
                }

                AcceptButton = _confirm;
            }

            private int MeasureHeight(string text, int width)
            {
                Size measured = TextRenderer.MeasureText(
                    text,
                    Theme.FontBody,
                    new Size(width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                return Math.Max(Theme.S(44), measured.Height + Theme.S(4));
            }

            private int MeasureButton(string text)
            {
                Size size = TextRenderer.MeasureText(text, Theme.FontButton);
                return size.Width + Theme.S(36);
            }

            protected override void OnDrawContent(Graphics graphics)
            {
                int iconSize = Theme.S(38);
                Rectangle iconRect = new Rectangle(Theme.S(26), Theme.S(26), iconSize, iconSize);

                Color iconFill;
                Color iconFore;
                string glyph;
                switch (_icon)
                {
                    case UiMessageIcon.Warn:
                        iconFill = Theme.AmberSoft;
                        iconFore = Theme.Amber;
                        glyph = "!";
                        break;
                    case UiMessageIcon.Question:
                        iconFill = Theme.BrandSoft;
                        iconFore = Theme.Brand;
                        glyph = "?";
                        break;
                    default:
                        iconFill = Theme.BrandSoft;
                        iconFore = Theme.Brand;
                        glyph = "i";
                        break;
                }

                UiDraw.FillRoundedRect(graphics, iconRect, iconSize / 2f, iconFill);

                using (Font glyphFont = Theme.DisplayFont(15f, FontStyle.Bold))
                {
                    UiDraw.DrawText(graphics, glyph, glyphFont, iconFore, iconRect, StringAlignment.Center);
                }

                int textLeft = iconRect.Right + Theme.S(16);
                int textWidth = Width - textLeft - Theme.S(26);

                UiDraw.DrawText(
                    graphics,
                    _title,
                    Theme.FontTitle,
                    Theme.Text,
                    new RectangleF(textLeft, Theme.S(26), textWidth, Theme.S(24)),
                    StringAlignment.Near);

                TextRenderer.DrawText(
                    graphics,
                    _message,
                    Theme.FontBody,
                    new Rectangle(textLeft, Theme.S(24 + 22 + 6), textWidth, _messageHeight),
                    Theme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            }
        }
    }
}
