using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace InProcess.DevTools
{
    internal static class VisualExtensions
    {
        /// <summary>
        /// Render control to the destination stream.
        /// </summary>
        /// <param name="source">Control to be rendered.</param>
        /// <param name="destination">Destination stream.</param>
        /// <param name="dpi">Dpi quality.</param>
        public static void RenderTo(this Control source, Stream destination, double dpi = 96)
        {
            source.RenderTo(destination, dpi, null, out _);
        }

        /// <summary>
        /// Render control (or a region of it) to the destination stream.
        /// </summary>
        /// <param name="source">Control to be rendered.</param>
        /// <param name="destination">Destination stream.</param>
        /// <param name="dpi">Dpi quality. The output pixel size is the size in device-independent units scaled by dpi / 96.</param>
        /// <param name="region">Optional region, in the control's own coordinates. Null renders the whole control.</param>
        /// <param name="pixelSize">The size in pixels of the written image.</param>
        public static void RenderTo(this Control source, Stream destination, double dpi, Rect? region, out PixelSize pixelSize)
        {
            pixelSize = default;
            var transform = source.TransformToVisual(source.GetVisualRoot()!);
            if (transform == null)
                return;

            var rect = (region ?? new Rect(source.Bounds.Size)).TransformToAABB(transform.Value);
            var top = rect.TopLeft;
            var scale = dpi / 96d;
            pixelSize = new PixelSize(Math.Max(1, (int)Math.Ceiling(rect.Width * scale)), Math.Max(1, (int)Math.Ceiling(rect.Height * scale)));
            var dpiVector = new Vector(dpi, dpi);

            // get Visual root
            var root = TopLevel.GetTopLevel(source) as Control ?? source;

            IDisposable? clipSetter = default;
            IDisposable? clipToBoundsSetter = default;
            IDisposable? renderTransformOriginSetter = default;
            IDisposable? renderTransformSetter = default;
            try
            {
                // Set clip region
                var clipRegion = new Avalonia.Media.RectangleGeometry(rect);
                clipToBoundsSetter = root.SetValue(Visual.ClipToBoundsProperty, true, BindingPriority.Animation);
                clipSetter = root.SetValue(Visual.ClipProperty, clipRegion, BindingPriority.Animation);

                // Translate origin
                renderTransformOriginSetter = root.SetValue(Visual.RenderTransformOriginProperty,
                    new RelativePoint(top, RelativeUnit.Absolute),
                    BindingPriority.Animation);

                renderTransformSetter = root.SetValue(Visual.RenderTransformProperty,
                    new Avalonia.Media.TranslateTransform(-top.X, -top.Y),
                    BindingPriority.Animation);

                using (var bitmap = new RenderTargetBitmap(pixelSize, dpiVector))
                {
                    bitmap.Render(root);
                    bitmap.Save(destination);
                }
            }
            finally
            {
                // Restore values before trasformation
                renderTransformSetter?.Dispose();
                renderTransformOriginSetter?.Dispose();
                clipSetter?.Dispose();
                clipToBoundsSetter?.Dispose();
                source?.InvalidateVisual();
            }
        }
    }
}
