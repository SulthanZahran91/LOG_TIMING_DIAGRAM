using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LOG_TIMING_DIAGRAM.Models;
using LOG_TIMING_DIAGRAM.ViewModels;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace LOG_TIMING_DIAGRAM.Rendering
{
    /// <summary>
    /// Renders all active signals on a shared, professional-looking timeline with frozen identifiers.
    /// </summary>
    public partial class ActiveSignalsPlotView : UserControl
    {
        public static readonly DependencyProperty SignalsProperty =
            System.Windows.DependencyProperty.Register(
                nameof(Signals),
                typeof(IEnumerable<SignalData>),
                typeof(ActiveSignalsPlotView),
                new System.Windows.PropertyMetadata(null, OnSignalsChanged));

        public static readonly System.Windows.DependencyProperty ViewportProperty =
            System.Windows.DependencyProperty.Register(
                nameof(Viewport),
                typeof(ViewportStateViewModel),
                typeof(ActiveSignalsPlotView),
                new System.Windows.PropertyMetadata(null, OnViewportChanged));

        private readonly PlotController _controller;

        private INotifyCollectionChanged? _currentSignalsNotifier;

        public ActiveSignalsPlotView()
        {
            InitializeComponent();

            _controller = new PlotController();
            _controller.UnbindAll(); // we drive panning/zooming via external controls
            Plot.Controller = _controller;
        }

        public IEnumerable<SignalData> Signals
        {
            get => (IEnumerable<SignalData>)GetValue(SignalsProperty);
            set => SetValue(SignalsProperty, value);
        }

        public ViewportStateViewModel Viewport
        {
            get => (ViewportStateViewModel)GetValue(ViewportProperty);
            set => SetValue(ViewportProperty, value);
        }

        private static void OnSignalsChanged(System.Windows.DependencyObject d, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            var control = (ActiveSignalsPlotView)d;
            control.DetachSignals(e.OldValue);
            control.AttachSignals(e.NewValue);
            control.RefreshPlot();
        }

        private static void OnViewportChanged(System.Windows.DependencyObject d, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            var control = (ActiveSignalsPlotView)d;

            if (e.OldValue is ViewportStateViewModel oldViewport)
            {
                oldViewport.TimeRangeChanged -= control.OnViewportInvalidated;
                oldViewport.ZoomLevelChanged -= control.OnViewportInvalidated;
            }

            if (e.NewValue is ViewportStateViewModel newViewport)
            {
                newViewport.TimeRangeChanged += control.OnViewportInvalidated;
                newViewport.ZoomLevelChanged += control.OnViewportInvalidated;
            }

            control.RefreshPlot();
        }

        private void OnViewportInvalidated(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(RefreshPlot, DispatcherPriority.Render);
            }
            else
            {
                RefreshPlot();
            }
        }

        private void RefreshPlot()
        {
            if (Plot == null)
            {
                return;
            }

            Plot.Model = BuildPlotModel();
            Plot.InvalidatePlot(true);
        }

        private PlotModel BuildPlotModel()
        {
            var viewport = Viewport;
            var signals = Signals?.Where(s => s != null).ToList() ?? new List<SignalData>();

            var model = new PlotModel
            {
                Background = OxyColor.FromRgb(30, 30, 30),
                TextColor = OxyColor.FromRgb(224, 224, 224),
                PlotAreaBorderColor = OxyColor.FromRgb(70, 70, 70),
                PlotAreaBorderThickness = new OxyThickness(0, 0, 0, 1)
            };

            if (viewport == null || signals.Count == 0)
            {
                model.Axes.Add(CreateTimeAxis(DateTime.Now, DateTime.Now.AddSeconds(10)));
                model.Axes.Add(CreateCategoryAxis(Array.Empty<string>()));
                model.Series.Add(new IntervalBarSeries());
                return model;
            }

            var visibleStart = viewport.VisibleStart;
            var visibleEnd = viewport.VisibleEnd;

            if (visibleEnd <= visibleStart)
            {
                visibleEnd = visibleStart.AddMilliseconds(1);
            }

            var labelTexts = signals.Select(s => $"{s.DeviceId}{Environment.NewLine}{s.SignalName}").ToArray();

            var timeAxis = CreateTimeAxis(visibleStart, visibleEnd);
            var categoryAxis = CreateCategoryAxis(labelTexts);
            var series = CreateSeries(timeAxis, categoryAxis);

            AddGridOverlays(model, visibleStart, visibleEnd, signals.Count);

            foreach (var (signal, index) in signals.Select((signal, index) => (signal, index)))
            {
                var segments = BuildSegments(signal, visibleStart, visibleEnd);

                foreach (var segment in segments)
                {
                    var startValue = DateTimeAxis.ToDouble(segment.Start);
                    var endValue = DateTimeAxis.ToDouble(segment.End);
                    if (endValue <= startValue)
                    {
                        continue;
                    }

                    var color = SelectColor(segment.Type, segment.Value, segment.IsSynthetic);
                    var stateLabel = FormatStateLabel(segment.Type, segment.Value, segment.IsSynthetic);

                    series.Items.Add(new IntervalBarItem
                    {
                        CategoryIndex = index,
                        Start = startValue,
                        End = endValue,
                        Color = color,
                        Title = string.Empty
                    });

                    if (!string.IsNullOrWhiteSpace(stateLabel))
                    {
                        model.Annotations.Add(new TextAnnotation
                        {
                            Text = stateLabel,
                            FontSize = 12,
                            TextColor = OxyColor.FromRgb(245, 245, 245),
                            Stroke = OxyColors.Transparent,
                            Background = OxyColors.Transparent,
                            TextPosition = new DataPoint((startValue + endValue) / 2.0, index),
                            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                            TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle,
                            Layer = AnnotationLayer.AboveSeries
                        });
                    }
                }
            }

            model.Axes.Add(timeAxis);
            model.Axes.Add(categoryAxis);
            model.Series.Add(series);

            return model;
        }

        private static DateTimeAxis CreateTimeAxis(DateTime start, DateTime end)
        {
            return new DateTimeAxis
            {
                Key = "SharedTimeAxis",
                Position = AxisPosition.Bottom,
                Minimum = DateTimeAxis.ToDouble(start),
                Maximum = DateTimeAxis.ToDouble(end),
                StringFormat = "HH:mm:ss",
                Title = string.Empty,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromAColor(96, OxyColors.White),
                MinorGridlineColor = OxyColor.FromAColor(32, OxyColors.White),
                TicklineColor = OxyColor.FromRgb(120, 120, 120),
                AxislineColor = OxyColor.FromRgb(96, 96, 96),
                IsZoomEnabled = false,
                IsPanEnabled = false
            };
        }

        private static CategoryAxis CreateCategoryAxis(IReadOnlyList<string> labels)
        {
            var axis = new CategoryAxis
            {
                Key = "SignalAxis",
                Position = AxisPosition.Left,
                IsPanEnabled = false,
                IsZoomEnabled = false,
                GapWidth = 0.2,
                IsTickCentered = true,
                AxislineColor = OxyColor.FromRgb(96, 96, 96),
                TicklineColor = OxyColor.FromRgb(96, 96, 96),
                TextColor = OxyColor.FromRgb(214, 214, 214),
                Title = string.Empty
            };

            if (labels?.Count > 0)
            {
                foreach (var label in labels)
                {
                    axis.Labels.Add(label);
                }
            }

            return axis;
        }

        private static IntervalBarSeries CreateSeries(Axis timeAxis, Axis categoryAxis)
        {
            return new IntervalBarSeries
            {
                XAxisKey = timeAxis.Key,
                YAxisKey = categoryAxis.Key,
                StrokeThickness = 1,
                StrokeColor = OxyColor.FromRgb(54, 54, 54),
                BarWidth = 0.6,
                RenderInLegend = false,
                LabelFormatString = null
            };
        }

        private static OxyColor SelectColor(SignalType signalType, object? value, bool isSynthetic)
        {
            if (isSynthetic || value == null)
            {
                return OxyColor.FromRgb(90, 90, 90);
            }

            return signalType switch
            {
                SignalType.Boolean => InterpretBoolean(value) ? OxyColor.FromRgb(99, 179, 119) : OxyColor.FromRgb(72, 72, 72),
                SignalType.Integer => OxyColor.FromRgb(80, 120, 200),
                SignalType.String => OxyColor.FromRgb(206, 166, 101),
                _ => OxyColor.FromRgb(120, 140, 200)
            };
        }

        private static string FormatStateLabel(SignalType signalType, object? value, bool isSynthetic)
        {
            if (value == null)
            {
                return isSynthetic ? "NONE" : string.Empty;
            }

            return signalType switch
            {
                SignalType.Boolean => InterpretBoolean(value) ? "HIGH" : "LOW",
                SignalType.Integer => value switch
                {
                    IFormattable numeric => numeric.ToString(null, CultureInfo.InvariantCulture),
                    string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                        => number.ToString(CultureInfo.InvariantCulture),
                    null => "0",
                    _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0"
                },
                SignalType.String => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            };
        }

        private static object? NormalizeStateValue(SignalState state)
        {
            if (state == null)
            {
                return null;
            }

            if (state.Value != null)
            {
                return state.Value;
            }

            return GetDefaultValue(state.SignalType);
        }

        private static bool InterpretBoolean(object value)
        {
            switch (value)
            {
                case bool b:
                    return b;
                case string s:
                    if (bool.TryParse(s, out var parsed))
                    {
                        return parsed;
                    }

                    if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var numericString))
                    {
                        return Math.Abs(numericString) > double.Epsilon;
                    }

                    return false;
                case IConvertible convertible:
                    try
                    {
                        return Math.Abs(convertible.ToDouble(CultureInfo.InvariantCulture)) > double.Epsilon;
                    }
                    catch
                    {
                        return false;
                    }
                default:
                    return false;
            }
        }

        private static object? GetDefaultValue(SignalType signalType)
        {
            return signalType switch
            {
                SignalType.Boolean => false,
                SignalType.Integer => 0,
                SignalType.String => string.Empty,
                _ => 0
            };
        }

        private static void AddGridOverlays(PlotModel model, DateTime start, DateTime end, int categoryCount)
        {
            if (categoryCount <= 0)
            {
                return;
            }

            var totalSeconds = Math.Max((end - start).TotalSeconds, 0.0001);
            var stepSeconds = DetermineGridStep(totalSeconds);
            if (stepSeconds <= 0)
            {
                return;
            }

            var stepTicks = TimeSpan.FromSeconds(stepSeconds).Ticks;
            var firstTick = RoundUpTicks(start.Ticks, stepTicks);

            for (var tick = firstTick; tick <= end.Ticks; tick += stepTicks)
            {
                var tickTime = new DateTime(tick);
                if (tickTime < start)
                {
                    continue;
                }

                model.Annotations.Add(new LineAnnotation
                {
                    Type = LineAnnotationType.Vertical,
                    X = DateTimeAxis.ToDouble(tickTime),
                    Color = OxyColor.FromAColor(120, OxyColors.White),
                    LineStyle = LineStyle.Solid,
                    MinimumY = -0.5,
                    MaximumY = categoryCount - 0.5,
                    Layer = AnnotationLayer.AboveSeries
                });
            }
        }

        private static double DetermineGridStep(double totalSeconds)
        {
            var preferredSteps = new[]
            {
                0.001, 0.002, 0.005,
                0.01, 0.02, 0.05,
                0.1, 0.2, 0.5,
                1, 2, 5,
                10, 15, 30,
                60, 120, 300,
                600, 900, 1800,
                3600, 7200, 14400
            };

            var target = totalSeconds / 8.0;
            foreach (var step in preferredSteps)
            {
                if (step >= target)
                {
                    return step;
                }
            }

            return preferredSteps[^1];
        }

        private static long RoundUpTicks(long value, long stepTicks)
        {
            var remainder = value % stepTicks;
            if (remainder == 0)
            {
                return value;
            }

            return value + (stepTicks - remainder);
        }

        private static List<Segment> BuildSegments(SignalData signal, DateTime visibleStart, DateTime visibleEnd)
        {
            var segments = new List<Segment>();

            if (visibleEnd <= visibleStart)
            {
                return segments;
            }

            var states = signal.States?.ToList() ?? new List<SignalState>();

            if (states.Count == 0)
            {
                segments.Add(new Segment(visibleStart, visibleEnd, signal.SignalType, null, true));
                return segments;
            }

            var lastKnownValue = NormalizeStateValue(states[0]);
            var lastSegmentEnd = visibleStart;
            var cursor = visibleStart;

            foreach (var state in states)
            {
                var segmentStart = state.StartTimestamp < visibleStart ? visibleStart : state.StartTimestamp;
                var segmentEnd = state.EndTimestamp > visibleEnd ? visibleEnd : state.EndTimestamp;

                if (segmentEnd <= segmentStart)
                {
                    continue;
                }

                if (segmentStart > cursor)
                {
                    segments.Add(new Segment(cursor, segmentStart, signal.SignalType, null, true));
                    cursor = segmentStart;
                }

                var normalized = NormalizeStateValue(state);
                segments.Add(new Segment(segmentStart, segmentEnd, state.SignalType, normalized, false));
                cursor = segmentEnd;
                lastKnownValue = normalized ?? lastKnownValue;
            }

            if (cursor < visibleEnd)
            {
                segments.Add(new Segment(cursor, visibleEnd, signal.SignalType, null, true));
            }

            if (segments.Count == 0)
            {
                segments.Add(new Segment(visibleStart, visibleEnd, signal.SignalType, null, true));
            }

            return segments;
        }

        private readonly struct Segment
        {
            public Segment(DateTime start, DateTime end, SignalType type, object? value, bool isSynthetic)
            {
                Start = start;
                End = end;
                Type = type;
                Value = value;
                IsSynthetic = isSynthetic;
            }

            public DateTime Start { get; }
            public DateTime End { get; }
            public SignalType Type { get; }
            public object? Value { get; }
            public bool IsSynthetic { get; }
        }

        private void AttachSignals(object? source)
        {
            if (_currentSignalsNotifier != null)
            {
                _currentSignalsNotifier.CollectionChanged -= OnSignalsCollectionChanged;
                _currentSignalsNotifier = null;
            }

            if (source is INotifyCollectionChanged notifier)
            {
                _currentSignalsNotifier = notifier;
                _currentSignalsNotifier.CollectionChanged += OnSignalsCollectionChanged;
            }
        }

        private void DetachSignals(object? source)
        {
            if (_currentSignalsNotifier != null)
            {
                _currentSignalsNotifier.CollectionChanged -= OnSignalsCollectionChanged;
                _currentSignalsNotifier = null;
            }
        }

        private void OnSignalsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(RefreshPlot, DispatcherPriority.Render);
            }
            else
            {
                RefreshPlot();
            }
        }
    }
}
