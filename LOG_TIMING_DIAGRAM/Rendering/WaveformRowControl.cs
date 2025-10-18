using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LOG_TIMING_DIAGRAM.Models;
using LOG_TIMING_DIAGRAM.ViewModels;

namespace LOG_TIMING_DIAGRAM.Rendering
{
    public sealed class WaveformRowControl : FrameworkElement
    {
        public static readonly DependencyProperty SignalDataProperty =
            DependencyProperty.Register(
                nameof(SignalData),
                typeof(SignalData),
                typeof(WaveformRowControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ViewportProperty =
            DependencyProperty.Register(
                nameof(Viewport),
                typeof(ViewportStateViewModel),
                typeof(WaveformRowControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnViewportChanged));

        private static readonly Typeface LabelTypeface = new Typeface("Segoe UI");
        private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        private static readonly Brush BooleanHighBrush = new SolidColorBrush(Color.FromRgb(99, 179, 119));
        private static readonly Brush BooleanLowBrush = new SolidColorBrush(Color.FromRgb(64, 64, 64));
        private static readonly Pen TransitionPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 220, 220)), 1);
        private static readonly Brush StateBrush = new SolidColorBrush(Color.FromRgb(80, 120, 200));
        private static readonly Brush StateTextBrush = Brushes.White;

        static WaveformRowControl()
        {
            BackgroundBrush.Freeze();
            BooleanHighBrush.Opacity = 0.6;
            BooleanHighBrush.Freeze();
            BooleanLowBrush.Opacity = 0.4;
            BooleanLowBrush.Freeze();
            TransitionPen.Freeze();
            StateBrush.Opacity = 0.6;
            StateBrush.Freeze();
            StateTextBrush.Freeze();
        }

        public SignalData SignalData
        {
            get => (SignalData)GetValue(SignalDataProperty);
            set => SetValue(SignalDataProperty, value);
        }

        public ViewportStateViewModel Viewport
        {
            get => (ViewportStateViewModel)GetValue(ViewportProperty);
            set => SetValue(ViewportProperty, value);
        }

        private static void OnViewportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (WaveformRowControl)d;
            if (e.OldValue is ViewportStateViewModel oldViewport)
            {
                oldViewport.TimeRangeChanged -= control.OnViewportChanged;
                oldViewport.ZoomLevelChanged -= control.OnViewportChanged;
            }

            if (e.NewValue is ViewportStateViewModel newViewport)
            {
                newViewport.TimeRangeChanged += control.OnViewportChanged;
                newViewport.ZoomLevelChanged += control.OnViewportChanged;
            }

            control.InvalidateVisual();
        }

        private void OnViewportChanged(object sender, EventArgs e) => InvalidateVisual();

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

            var viewport = Viewport;
            var signalData = SignalData;

            if (viewport == null || signalData == null || signalData.States.Count == 0 || viewport.VisibleEnd <= viewport.VisibleStart)
            {
                DrawEmptyMessage(drawingContext);
                return;
            }

            var visibleStart = viewport.VisibleStart;
            var visibleEnd = viewport.VisibleEnd;
            var totalSeconds = (visibleEnd - visibleStart).TotalSeconds;
            if (totalSeconds <= double.Epsilon)
            {
                DrawEmptyMessage(drawingContext);
                return;
            }

            var rowRect = new Rect(0, 0, ActualWidth, ActualHeight);
            foreach (var state in signalData.States)
            {
                var clipStart = state.StartTimestamp < visibleStart ? visibleStart : state.StartTimestamp;
                var clipEnd = state.EndTimestamp > visibleEnd ? visibleEnd : state.EndTimestamp;
                if (clipEnd <= visibleStart || clipStart >= visibleEnd)
                {
                    continue;
                }

                var startPct = (clipStart - visibleStart).TotalSeconds / totalSeconds;
                var endPct = (clipEnd - visibleStart).TotalSeconds / totalSeconds;
                var x = rowRect.Width * startPct;
                var width = Math.Max(1.0, rowRect.Width * (endPct - startPct));

                switch (state.SignalType)
                {
                    case SignalType.Boolean:
                        DrawBooleanState(drawingContext, state, x, width, rowRect.Height);
                        break;
                    case SignalType.Integer:
                    case SignalType.String:
                    default:
                        DrawGeneralState(drawingContext, state, x, width, rowRect.Height);
                        break;
                }
            }
        }

        private void DrawEmptyMessage(DrawingContext drawingContext)
        {
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var formatted = new FormattedText(
                "No data in range",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                12,
                Brushes.Gray,
                pixelsPerDip);

            drawingContext.DrawText(formatted, new Point(4, (ActualHeight - formatted.Height) / 2));
        }

        private void DrawBooleanState(DrawingContext drawingContext, SignalState state, double x, double width, double height)
        {
            var isHigh = state.Value is bool b && b;
            var rect = new Rect(x, 0, width, height);
            var brush = isHigh ? BooleanHighBrush : BooleanLowBrush;
            drawingContext.DrawRectangle(brush, TransitionPen, rect);

            if (!isHigh)
            {
                var midY = height / 2;
                drawingContext.DrawLine(TransitionPen, new Point(x, midY), new Point(x + width, midY));
            }
        }

        private void DrawGeneralState(DrawingContext drawingContext, SignalState state, double x, double width, double height)
        {
            var rect = new Rect(x, height * 0.15, width, height * 0.7);
            drawingContext.DrawRectangle(StateBrush, TransitionPen, rect);

            var valueText = state.Value?.ToString() ?? string.Empty;
            if (width > 10 && valueText.Length > 0)
            {
                var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                var formatted = new FormattedText(
                    valueText,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelTypeface,
                    11,
                    StateTextBrush,
                    pixelsPerDip)
                {
                    MaxTextWidth = Math.Max(0, width - 4)
                };
                formatted.Trimming = TextTrimming.CharacterEllipsis;

                drawingContext.DrawText(formatted, new Point(x + 2, (ActualHeight - formatted.Height) / 2));
            }
        }

        protected override Size MeasureOverride(Size availableSize) => new Size(availableSize.Width, 24);

        protected override Size ArrangeOverride(Size finalSize) => base.ArrangeOverride(new Size(finalSize.Width, 24));
    }
}
