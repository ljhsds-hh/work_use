using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using XiaoDing.Core;

namespace XiaoDing.UI
{
    /// <summary>提醒设置：遮罩暗度与全屏背景图，按设计稿紧凑排布。</summary>
    internal sealed class SettingsForm : Form
    {
        private const int PadLeft = 26;
        private const int ContentWidth = 428;

        private readonly UiSlider _dimSlider;
        private readonly Label _dimValue;
        private readonly Label _imageHint;
        private readonly Panel _preview;
        private Image _previewImage;

        // 图片改动先挂在内存里，点「保存」才写盘；「取消」不落任何痕迹。
        private Bitmap _pendingImage;
        private bool _pendingClear;

        public SettingsForm(int currentDim)
        {
            Text = "提醒设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            Font = Theme.FontBody;
            ClientSize = new Size(Theme.S(480), Theme.S(348));

            int left = Theme.S(PadLeft);
            int width = Theme.S(ContentWidth);
            int y = Theme.S(20);

            Label secOverlay = AddSectionLabel("全屏遮罩");
            secOverlay.Location = new Point(left, y);
            y += Theme.S(26);

            Label dimLabel = AddFieldLabel("遮罩暗度");
            dimLabel.Location = new Point(left, y);
            y += Theme.S(22);

            _dimSlider = new UiSlider();
            _dimSlider.SetBounds(Theme.S(24), y, Theme.S(300), Theme.S(26));
            _dimSlider.Value = Math.Max(0, Math.Min(100, (int)Math.Round(currentDim * 100f / 255f)));
            _dimSlider.ValueChanged += delegate { UpdateDimText(); };
            Controls.Add(_dimSlider);

            _dimValue = new Label();
            _dimValue.ForeColor = Theme.Brand;
            _dimValue.Font = Theme.FontMeta;
            _dimValue.TextAlign = ContentAlignment.MiddleRight;
            _dimValue.AutoSize = false;
            _dimValue.Location = new Point(Theme.S(340), y);
            _dimValue.Size = new Size(Theme.S(60), Theme.S(26));
            _dimValue.BackColor = Color.Transparent;
            Controls.Add(_dimValue);

            y += Theme.S(26) + Theme.S(10);

            Label dimHint = AddHintLabel("数值越大，遮罩越暗。没有背景图时是纯色蒙布。");
            dimHint.SetBounds(left, y, width, Theme.S(18));
            y += Theme.S(18) + Theme.S(18);

            Label secImage = AddSectionLabel("背景图片");
            secImage.Location = new Point(left, y);
            y += Theme.S(26);

            _preview = new Panel();
            _preview.BackColor = Theme.SurfaceMuted;
            _preview.SetBounds(left, y, Theme.S(120), Theme.S(68));
            _preview.BackgroundImageLayout = ImageLayout.Zoom;
            _preview.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Border))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, _preview.Width - 1, _preview.Height - 1);
                }
            };
            Controls.Add(_preview);

            UiButton choose = new UiButton();
            choose.Text = "选择图片";
            choose.SetBounds(left + Theme.S(132), y, Theme.S(100), Theme.S(30));
            choose.Click += OnChooseImage;
            Controls.Add(choose);

            UiButton clear = new UiButton();
            clear.Text = "清除";
            clear.SetBounds(left + Theme.S(132), y + Theme.S(38), Theme.S(100), Theme.S(30));
            clear.Click += OnClearImage;
            Controls.Add(clear);

            y += Theme.S(68) + Theme.S(10);

            _imageHint = AddHintLabel(string.Empty);
            _imageHint.TextAlign = ContentAlignment.TopLeft;
            _imageHint.SetBounds(left, y, width, Theme.S(36));
            Controls.Add(_imageHint);

            y += Theme.S(36) + Theme.S(16);

            UiButton cancel = new UiButton();
            cancel.Text = "取消";
            cancel.SetBounds(Theme.S(272), y, Theme.S(88), Theme.S(34));
            cancel.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(cancel);

            UiButton ok = new UiButton();
            ok.Primary = true;
            ok.Text = "保存";
            ok.SetBounds(Theme.S(370), y, Theme.S(84), Theme.S(34));
            ok.Click += OnSave;
            Controls.Add(ok);

            AcceptButton = ok;
            CancelButton = cancel;

            UpdateDimText();
            UpdateImageStatus();
        }

        public int OverlayDim { get; private set; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ApplyDarkTitleBar(Handle, Theme.IsDark);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            NativeMethods.ApplyDarkTitleBar(Handle, Theme.IsDark);
        }

        private Label AddSectionLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Theme.TextFaint;
            label.Font = Theme.FontSection;
            label.AutoSize = false;
            label.Size = new Size(Theme.S(200), Theme.S(18));
            label.BackColor = Color.Transparent;
            Controls.Add(label);
            return label;
        }

        private Label AddFieldLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Theme.TextMuted;
            label.Font = Theme.FontMeta;
            label.AutoSize = false;
            label.Size = new Size(Theme.S(200), Theme.S(18));
            label.BackColor = Color.Transparent;
            Controls.Add(label);
            return label;
        }

        private Label AddHintLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Theme.TextMuted;
            label.Font = Theme.FontTag;
            label.AutoSize = false;
            label.BackColor = Color.Transparent;
            Controls.Add(label);
            return label;
        }

        private void UpdateDimText()
        {
            _dimValue.Text = _dimSlider.Value + "%";
        }

        private void UpdateImageStatus()
        {
            if (_previewImage != null)
            {
                _previewImage.Dispose();
                _previewImage = null;
            }

            _preview.BackgroundImage = null;

            if (_pendingImage != null)
            {
                _previewImage = new Bitmap(_pendingImage);
                _imageHint.Text = "已选择新背景图，点「保存」后生效。";
                _imageHint.ForeColor = Theme.Text;
            }
            else if (_pendingClear)
            {
                _imageHint.Text = "已清除背景图，点「保存」后生效。";
                _imageHint.ForeColor = Theme.TextMuted;
            }
            else if (File.Exists(AppPaths.OverlayImagePath))
            {
                try
                {
                    using (FileStream stream = new FileStream(AppPaths.OverlayImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (Image image = Image.FromStream(stream))
                    {
                        _previewImage = new Bitmap(image);
                    }

                    _imageHint.Text = "已设置自定义背景图，按屏幕比例铺满后再叠加暗度。";
                    _imageHint.ForeColor = Theme.Text;
                }
                catch (Exception ex)
                {
                    AppLog.Error("Settings.Preview.Failed", ex);
                    _imageHint.Text = "背景图读取失败，可在下方重新选择。";
                    _imageHint.ForeColor = Theme.TextMuted;
                }
            }
            else
            {
                _imageHint.Text = "未设置，使用纯色蒙布。图片会按屏幕比例铺满后再叠加暗度。";
                _imageHint.ForeColor = Theme.TextMuted;
            }

            _preview.BackgroundImage = _previewImage;
        }

        private void OnChooseImage(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择全屏提醒背景图";
                dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    Bitmap copy;
                    using (FileStream stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (Image image = Image.FromStream(stream))
                    {
                        copy = new Bitmap(image);
                    }

                    if (_pendingImage != null)
                    {
                        _pendingImage.Dispose();
                    }

                    _pendingImage = copy;
                    _pendingClear = false;
                    UpdateImageStatus();
                }
                catch (Exception ex)
                {
                    AppLog.Error("Settings.Background.ImportFailed", ex);
                    UiMessageBox.Warn(this, "图片读取失败，请换一张试试。");
                }
            }
        }

        private void OnClearImage(object sender, EventArgs e)
        {
            if (_pendingImage != null)
            {
                _pendingImage.Dispose();
                _pendingImage = null;
            }

            _pendingClear = true;
            UpdateImageStatus();
        }

        private void OnSave(object sender, EventArgs e)
        {
            if (!ApplyPendingImage())
            {
                return;
            }

            OverlayDim = (int)Math.Round(_dimSlider.Value * 255f / 100f);
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>把挂起的图片改动写盘。返回 false 表示写入失败，对话框保持打开可重试。</summary>
        private bool ApplyPendingImage()
        {
            try
            {
                if (_pendingImage != null)
                {
                    _pendingImage.Save(AppPaths.OverlayImagePath, ImageFormat.Png);
                }
                else if (_pendingClear && File.Exists(AppPaths.OverlayImagePath))
                {
                    File.Delete(AppPaths.OverlayImagePath);
                }

                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Settings.Background.ApplyFailed", ex);
                UiMessageBox.Warn(this, "背景图保存失败，请重试。");
                return false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_previewImage != null)
                {
                    _previewImage.Dispose();
                    _previewImage = null;
                }

                if (_pendingImage != null)
                {
                    _pendingImage.Dispose();
                    _pendingImage = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}
