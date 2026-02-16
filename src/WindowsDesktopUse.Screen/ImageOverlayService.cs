using System.Drawing;
using System.Drawing.Drawing2D;

namespace WindowsDesktopUse.Screen;

/// <summary>
/// Provides AI-friendly image overlay utilities for enhancing LLM video frame understanding.
/// Adds minimal visual hints (timestamp, event tags) to captured frames without destroying original content.
/// </summary>
public static class ImageOverlayService
{
    // AI-friendly overlay constants: chosen for readability at 640x360 resolution
    private const int FontSize = 14;
    private const int Padding = 4;
    private const int CornerRadius = 4;

    /// <summary>
    /// Overlays elapsed time timestamp on the top-left corner of the image.
    /// Format: [HH:MM:SS.m] - easily parseable by LLM via OCR.
    /// 
    /// Design rationale:
    /// - Black semi-transparent background: ensures contrast regardless of video content
    /// - White monospace font: maximizes character recognition accuracy
    /// - Top-left position: least likely to interfere with main video content
    /// - Semi-transparent alpha (180/255): visible but not overly distracting
    /// </summary>
    /// <param name="bmp">Source bitmap (modified in-place)</param>
    /// <param name="elapsed">Elapsed time since capture started</param>
    public static void OverlayTimestamp(Bitmap bmp, TimeSpan elapsed)
    {
        // Format: [00:00:05.2] - hours, minutes, seconds, deciseconds
        var timestamp = $"[{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100}]";

        using var g = Graphics.FromImage(bmp);
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        // Use monospace font for consistent character width (AI-friendly for OCR)
        using var font = new Font("Consolas", FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        var textSize = g.MeasureString(timestamp, font);
        var bgWidth = textSize.Width + (Padding * 2);
        var bgHeight = textSize.Height + (Padding * 2);

        // Draw semi-transparent black background box for contrast
        using var bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
        var bgRect = new RectangleF(0, 0, bgWidth, bgHeight);
        g.FillRoundedRectangle(bgBrush, bgRect, CornerRadius);

        // Draw white text
        using var textBrush = new SolidBrush(Color.White);
        g.DrawString(timestamp, font, textBrush, Padding, Padding);
    }

    /// <summary>
    /// Overlays event tag (e.g., [SCENE CHANGE]) on the top-left corner, below the timestamp.
    /// Uses contrasting color (yellow on red background) for immediate visual recognition.
    /// 
    /// Design rationale:
    /// - Red background: signals "important event" to both AI and human reviewers
    /// - Yellow text: maximum contrast against red, highly visible
    /// - Positioned below timestamp: maintains consistent layout hierarchy
    /// - Bold font: emphasizes urgency/importance of the event
    /// </summary>
    /// <param name="bmp">Source bitmap (modified in-place)</param>
    /// <param name="eventTag">Event tag string (e.g., "SCENE CHANGE"). If null or empty, no overlay is applied.</param>
    public static void OverlayEventTag(Bitmap bmp, string? eventTag)
    {
        if (string.IsNullOrWhiteSpace(eventTag))
            return;

        var tagText = $"[{eventTag}]";

        using var g = Graphics.FromImage(bmp);
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        // Use bold font for emphasis
        using var font = new Font("Consolas", FontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        var textSize = g.MeasureString(tagText, font);
        var bgWidth = textSize.Width + (Padding * 2);
        var bgHeight = textSize.Height + (Padding * 2);

        // Position below timestamp (y = FontSize + Padding*2 + Padding)
        var yPos = FontSize + (Padding * 2) + Padding;

        // Draw red background for high visibility
        using var bgBrush = new SolidBrush(Color.FromArgb(200, 200, 50, 0)); // Dark red with transparency
        var bgRect = new RectangleF(0, yPos, bgWidth, bgHeight);
        g.FillRoundedRectangle(bgBrush, bgRect, CornerRadius);

        // Draw yellow text (maximum contrast against red)
        using var textBrush = new SolidBrush(Color.Yellow);
        g.DrawString(tagText, font, textBrush, Padding, yPos);
    }

    /// <summary>
    /// Combines two bitmaps horizontally for side-by-side comparison.
    /// Useful for showing "before vs after" frames to LLM for change detection.
    /// 
    /// Design rationale:
    /// - Horizontal layout: matches natural left-to-right reading pattern
    /// - Equal sizing: prevents AI from biasing toward larger/smaller image
    /// - Optional label: clarifies which frame is "previous" vs "current"
    /// </summary>
    /// <param name="prev">Previous frame bitmap</param>
    /// <param name="curr">Current frame bitmap</param>
    /// <returns>Combined bitmap with both frames side-by-side</returns>
    public static Bitmap GenerateDiffImage(Bitmap prev, Bitmap curr)
    {
        // Ensure both images have the same dimensions
        var targetWidth = Math.Min(prev.Width, curr.Width);
        var targetHeight = Math.Min(prev.Height, curr.Height);

        // Create combined bitmap (2x width for side-by-side)
        var combined = new Bitmap(targetWidth * 2, targetHeight);

        using var g = Graphics.FromImage(combined);
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Draw previous frame on left
        g.DrawImage(prev, new Rectangle(0, 0, targetWidth, targetHeight));

        // Draw current frame on right
        g.DrawImage(curr, new Rectangle(targetWidth, 0, targetWidth, targetHeight));

        // Draw divider line for clear separation
        using var pen = new Pen(Color.White, 2);
        g.DrawLine(pen, targetWidth, 0, targetWidth, targetHeight);

        return combined;
    }

    /// <summary>
    /// Helper extension for drawing rounded rectangles (compatible with .NET 8)
    /// </summary>
    private static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF rect, int radius)
    {
        using var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
        path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
        path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
