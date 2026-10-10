using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Media.Imaging;
using SlashText.Design;
using Forms = System.Windows.Forms;

namespace SlashText.Views;

/// <summary>Retains native tray placement, dismissal and keyboard navigation with the app palette and icons.</summary>
internal static class TrayMenuTheme
{
    internal static Color Color(string key)
    {
        var value = ((System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.FindResource(key)).Color;
        return System.Drawing.Color.FromArgb(value.A, value.R, value.G, value.B);
    }

    internal static void Apply(Forms.ToolStripDropDown menu)
    {
        var scale = menu.DeviceDpi / 96d;
        menu.BackColor = Color("Lab.panel"); menu.ForeColor = Color("Lab.text");
        menu.Renderer = new Renderer(); menu.Padding = new Forms.Padding((int)(6 * scale));
        menu.ImageScalingSize = new Size((int)(18 * scale), (int)(18 * scale));
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            item.BackColor = menu.BackColor; item.ForeColor = menu.ForeColor;
            item.Padding = item is Forms.ToolStripSeparator ? new Forms.Padding(0, (int)(3 * scale), 0, (int)(3 * scale)) : new Forms.Padding((int)(6 * scale), (int)(7 * scale), (int)(14 * scale), (int)(7 * scale));
            if (item.Tag is string kind)
            {
                var old = item.Image; item.Image = Icon(kind, (int)(18 * scale)); old?.Dispose();
            }
            if (item is Forms.ToolStripMenuItem child && child.HasDropDownItems) Apply(child.DropDown);
        }
        var previous = menu.Region;
        using var shape = Rounded(new Rectangle(0, 0, Math.Max(1, menu.Width), Math.Max(1, menu.Height)), (float)(9 * scale));
        menu.Region = new Region(shape); previous?.Dispose();
    }

    internal static void Wire(Forms.ToolStripDropDown menu)
    {
        var font = new Font(((System.Windows.Media.FontFamily)System.Windows.Application.Current.FindResource("Lab.Font")).Source, 10f);
        menu.Font = font;
        menu.Opening += (_, _) => Apply(menu);
        menu.SizeChanged += (_, _) =>
        {
            var previous = menu.Region;
            using var shape = Rounded(new Rectangle(0, 0, Math.Max(1, menu.Width), Math.Max(1, menu.Height)), 9f * menu.DeviceDpi / 96);
            menu.Region = new Region(shape); previous?.Dispose();
        };
        foreach (Forms.ToolStripMenuItem item in menu.Items.OfType<Forms.ToolStripMenuItem>())
            if (item.HasDropDownItems) Wire(item.DropDown);
        menu.Disposed += (_, _) => { font.Dispose(); foreach (Forms.ToolStripItem item in menu.Items) item.Image?.Dispose(); };
    }

    private static Bitmap Icon(string kind, int pixels)
    {
        var glyph = new LabIcon { Kind = kind, Width = 24, Height = 24 };
        glyph.SetResourceReference(LabIcon.ForegroundProperty, "Lab.accent-text");
        glyph.Measure(new System.Windows.Size(24, 24)); glyph.Arrange(new System.Windows.Rect(0, 0, 24, 24));
        var image = new RenderTargetBitmap(pixels, pixels, 96d * pixels / 24, 96d * pixels / 24, System.Windows.Media.PixelFormats.Pbgra32); image.Render(glyph);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
        using var decoded = new Bitmap(stream); return new Bitmap(decoded);
    }

    private static GraphicsPath Rounded(Rectangle rectangle, float radius)
    {
        var path = new GraphicsPath(); var diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
    }

    private sealed class Renderer : Forms.ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        { e.Graphics.Clear(Color("Lab.panel")); }
        protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e) { }
        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = Rounded(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 9f * e.ToolStrip.DeviceDpi / 96);
            using var pen = new Pen(Color("Lab.line-strong")); e.Graphics.DrawPath(pen, shape);
        }
        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = Rounded(new Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2), 6f * (e.ToolStrip?.DeviceDpi ?? 96) / 96);
            using var fill = new SolidBrush(Color("Lab.tint")); e.Graphics.FillPath(fill, shape);
            using var pen = new Pen(Color("Lab.accent")); e.Graphics.DrawPath(pen, shape);
        }
        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            // Native text bounds retain their old vertical origin after adding item padding.
            // Keep the native horizontal layout, but align the label to the whole row like its icon.
            e.TextRectangle = new Rectangle(e.TextRectangle.X, 0, e.TextRectangle.Width, e.Item.Height);
            e.TextFormat = (e.TextFormat & ~Forms.TextFormatFlags.Bottom) | Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.SingleLine;
            e.TextColor = Color(e.Item.Enabled ? "Lab.text" : "Lab.muted");
            base.OnRenderItemText(e);
        }
        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        { using var pen = new Pen(Color("Lab.line")); e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2); }
        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        { e.ArrowColor = Color(e.Item?.Enabled == true ? "Lab.muted" : "Lab.line-strong"); base.OnRenderArrow(e); }
    }
}
