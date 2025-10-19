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

        private const double RowHeight = 48.0;
        private const double WaveAmplitude = 0.35;
        private const double BandPadding = 0.45;
        private const double MinTransitionMilliseconds = 3.0;

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

            var activeSignals = Signals?.Where(s => s != null).ToList() ?? new List<SignalData>();
            Plot.Model = BuildPlotModel(activeSignals);
            UpdatePlotHeight(activeSignals.Count);
            Plot.InvalidatePlot(true);
        }

        private void UpdatePlotHeight(int signalCount)
        {
            var desired = signalCount > 0
                ? 120.0 + signalCount * RowHeight
                : 320.0;

            Plot.Height = Math.Max(desired, 320.0);
        }

        private PlotModel BuildPlotModel(IReadOnlyList<SignalData> signals)
        {
            var model = new PlotModel
            {
                Background = OxyColor.FromRgb(30, 30, 30),
                TextColor = OxyColor.FromRgb(224, 224, 224),
                PlotAreaBorderColor = OxyColor.FromRgb(70, 70, 70),
                PlotAreaBorderThickness = new OxyThickness(0, 0, 0, 1)
            };

            if (signals.Count == 0)
            {
                var now = DateTime.Now;
                model.Axes.Add(CreateTimeAxis(now, now.AddSeconds(10)));
                model.Axes.Add(CreateValueAxis(Array.Empty<string>()));
                return model;
            }

            var range = GetRangeFromSignals(signals);
            if (range == null)
            {
                var now = DateTime.Now;
                model.Axes.Add(CreateTimeAxis(now, now.AddSeconds(10)));
                model.Axes.Add(CreateValueAxis(Array.Empty<string>()));
                return model;
            }

            var start = range.Item1;
            var end = range.Item2;

            var viewport = Viewport;
            if (viewport != null)
            {
                var candidateStart = viewport.VisibleStart;
                var candidateEnd = viewport.VisibleEnd;

                if (candidateEnd > candidateStart)
                {
                    start = candidateStart;
                    end = candidateEnd;
                }
            }

            if (end <= start)
            {
                end = start.AddSeconds(10);
            }

            var timeAxis = CreateTimeAxis(start, end);
            model.Axes.Add(timeAxis);

            var labels = signals.Select(FormatSignalLabel).ToArray();
            var valueAxis = CreateValueAxis(labels);
            model.Axes.Add(valueAxis);

            AddRowBackgrounds(model, start, end, signals.Count);
            AddGridOverlays(model, start, end, signals.Count);

            for (var index = 0; index < signals.Count; index++)
            {
                AddSignalWaveform(model, signals[index], index, start, end);
            }

            model.IsLegendVisible = false;
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

        private static LinearAxis CreateValueAxis(string[] labels)
        {
            return new LinearAxis
            {
                Key = "SignalAxis",
                Position = AxisPosition.Left,
                Minimum = -0.5,
                Maximum = Math.Max(labels.Length - 0.5, 0.5),
                MajorStep = 1,
                MinorStep = 1,
                TickStyle = TickStyle.None,
                IsPanEnabled = false,
                IsZoomEnabled = false,
                AxislineColor = OxyColor.FromRgb(96, 96, 96),
                TicklineColor = OxyColor.FromRgb(96, 96, 96),
                TextColor = OxyColor.FromRgb(214, 214, 214),
                Title = string.Empty,
                LabelFormatter = value => FormatAxisLabel(value, labels)
            };
        }

        private static string FormatAxisLabel(double value, IReadOnlyList<string> labels)
        {
            var index = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            if (index < 0 || index >= labels.Count)
            {
                return string.Empty;
            }

            return labels[index];
        }

        private static string FormatSignalLabel(SignalData signal)
        {
            if (signal == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(signal.DeviceId)
                ? signal.SignalName
                : $"{signal.DeviceId} :: {signal.SignalName}";
        }

        private void AddRowBackgrounds(PlotModel model, DateTime start, DateTime end, int signalCount)
        {
            if (signalCount <= 0)
            {
                return;
            }

            var minX = DateTimeAxis.ToDouble(start);
            var maxX = DateTimeAxis.ToDouble(end);

            for (var i = 0; i < signalCount; i++)
            {
                var fill = i % 2 == 0
                    ? OxyColor.FromArgb(30, 90, 90, 90)
                    : OxyColor.FromArgb(16, 90, 90, 90);

                model.Annotations.Add(new RectangleAnnotation
                {
                    MinimumX = minX,
                    MaximumX = maxX,
                    MinimumY = i - BandPadding,
                    MaximumY = i + BandPadding,
                    Fill = fill,
                    Stroke = OxyColors.Transparent,
                    Layer = AnnotationLayer.BelowSeries
                });
            }
        }

        private void AddSignalWaveform(PlotModel model, SignalData signal, int index, DateTime start, DateTime end)
        {
            if (signal?.States == null || signal.States.Count == 0)
            {
                return;
            }

            var context = BuildWaveContext(signal, index);

            if (signal.SignalType == SignalType.String)
            {
                RenderStringBus(model, signal, index, start, end);
                return;
            }

            var states = signal.States;
            var strokeColor = SelectColor(signal.SignalType, states[0].Value, false);

            var series = new LineSeries
            {
                Color = strokeColor,
                StrokeThickness = 2.6,
                LineStyle = LineStyle.Solid,
                LineJoin = LineJoin.Round,
                YAxisKey = "SignalAxis",
                Title = FormatSignalLabel(signal),
                TrackerFormatString = "{0}\nTime: {1:yyyy-MM-dd HH:mm:ss.fff}\nLevel: {2:0.000}"
            };

            var levelProvider = context.LevelProvider;
            double? previousLevel = null;

            foreach (var state in states)
            {
                var effectiveStart = state.StartTimestamp < start ? start : state.StartTimestamp;
                var effectiveEnd = state.EndTimestamp > end ? end : state.EndTimestamp;
                if (effectiveEnd <= effectiveStart)
                {
                    continue;
                }

                var x0 = DateTimeAxis.ToDouble(effectiveStart);
                var x1 = DateTimeAxis.ToDouble(effectiveEnd);
                var level = levelProvider(state);

                if (previousLevel.HasValue && !AreClose(previousLevel.Value, level))
                {
                    series.Points.Add(new DataPoint(x0, previousLevel.Value));
                }

                series.Points.Add(new DataPoint(x0, level));
                series.Points.Add(new DataPoint(x1, level));

                var fillColor = OxyColor.FromAColor(80, strokeColor);
                AddStateBand(model, signal.SignalType, index, state, level, fillColor, start, end);
                AddStateLabel(model, signal.SignalType, index, state, level, strokeColor);

                previousLevel = level;
            }

            model.Series.Add(series);

            if (signal.SignalType == SignalType.Integer && context.Min.HasValue && context.Max.HasValue)
            {
                AddRangeAnnotation(model, index, context.Min.Value, context.Max.Value, start, end);
            }
        }

        private readonly struct SignalWaveContext
        {
            public SignalWaveContext(Func<SignalState, double> levelProvider, double? min, double? max, IReadOnlyList<string> tokens)
            {
                LevelProvider = levelProvider ?? (_ => 0);
                Min = min;
                Max = max;
                Tokens = tokens ?? Array.Empty<string>();
            }

            public Func<SignalState, double> LevelProvider { get; }

            public double? Min { get; }

            public double? Max { get; }

            public IReadOnlyList<string> Tokens { get; }
        }

        private SignalWaveContext BuildWaveContext(SignalData signal, int index)
        {
            var baseline = index;
            var high = baseline + WaveAmplitude;
            var low = baseline - WaveAmplitude;

            switch (signal.SignalType)
            {
                case SignalType.Boolean:
                    return new SignalWaveContext(state => InterpretBoolean(state.Value) ? high : low, null, null, Array.Empty<string>());

                case SignalType.Integer:
                {
                    var numericValues = signal.States
                        .Select(s => TryConvertToDouble(s.Value))
                        .Where(v => v.HasValue)
                        .Select(v => v.Value)
                        .ToList();

                    if (numericValues.Count == 0)
                    {
                        return new SignalWaveContext(_ => baseline, null, null, Array.Empty<string>());
                    }

                    var min = numericValues.Min();
                    var max = numericValues.Max();
                    var range = Math.Max(max - min, 1e-9);

                    return new SignalWaveContext(
                        state =>
                        {
                            var value = TryConvertToDouble(state.Value) ?? min;
                            var normalized = (value - min) / range;
                            return low + normalized * (high - low);
                        },
                        min,
                        max,
                        Array.Empty<string>());
                }

                case SignalType.String:
                default:
                {
                    var tokens = signal.States
                        .Select(s => Convert.ToString(s.Value, CultureInfo.InvariantCulture) ?? string.Empty)
                        .ToList();

                    return new SignalWaveContext(_ => baseline, null, null, tokens);
                }
            }
        }

        private void RenderStringBus(PlotModel model, SignalData signal, int index, DateTime start, DateTime end)
        {
            var states = signal.States;

            for (var i = 0; i < states.Count; i++)
            {
                var state = states[i];
                var effectiveStart = state.StartTimestamp < start ? start : state.StartTimestamp;
                var effectiveEnd = state.EndTimestamp > end ? end : state.EndTimestamp;
                if (effectiveEnd <= effectiveStart)
                {
                    continue;
                }

                var x0 = DateTimeAxis.ToDouble(effectiveStart);
                var x1 = DateTimeAxis.ToDouble(effectiveEnd);
                var span = x1 - x0;
                var bevel = Math.Min(Math.Max(span * 0.12, 0.0), TimeSpan.FromMilliseconds(MinTransitionMilliseconds).TotalDays);

                var stateColor = SelectColor(signal.SignalType, state.Value, false);
                var fill = OxyColor.FromAColor(90, stateColor);
                var stroke = OxyColor.FromRgb(70, 70, 70);

                var rectangle = new RectangleAnnotation
                {
                    MinimumX = x0,
                    MaximumX = x1,
                    MinimumY = index - BandPadding,
                    MaximumY = index + BandPadding,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = 1.2,
                    Layer = AnnotationLayer.BelowSeries
                };

                model.Annotations.Add(rectangle);

                if (bevel > 0)
                {
                    if (i > 0)
                    {
                        var leftConnector = new PolygonAnnotation
                        {
                            Fill = fill,
                            Stroke = stroke,
                            StrokeThickness = 1.2,
                            Layer = AnnotationLayer.BelowSeries
                        };

                        leftConnector.Points.Add(new DataPoint(x0 - bevel, index));
                        leftConnector.Points.Add(new DataPoint(x0, index + BandPadding));
                        leftConnector.Points.Add(new DataPoint(x0, index - BandPadding));
                        model.Annotations.Add(leftConnector);
                    }

                    if (i < states.Count - 1)
                    {
                        var rightConnector = new PolygonAnnotation
                        {
                            Fill = fill,
                            Stroke = stroke,
                            StrokeThickness = 1.2,
                            Layer = AnnotationLayer.BelowSeries
                        };

                        rightConnector.Points.Add(new DataPoint(x1 + bevel, index));
                        rightConnector.Points.Add(new DataPoint(x1, index + BandPadding));
                        rightConnector.Points.Add(new DataPoint(x1, index - BandPadding));
                        model.Annotations.Add(rightConnector);
                    }
                }

                AddStateLabel(model, SignalType.String, index, state, index, stroke);
            }
        }

        private void AddRangeAnnotation(PlotModel model, int index, double min, double max, DateTime start, DateTime end)
        {
            if (!double.IsFinite(min) || !double.IsFinite(max))
            {
                return;
            }

            var window = end - start;
            var offset = window == TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(1)
                : TimeSpan.FromTicks(window.Ticks / 40);

            var anchor = start + offset;
            var text = $"min {min:G4}\nmax {max:G4}";

            var annotation = new TextAnnotation
            {
                Text = text,
                FontSize = 9,
                TextColor = OxyColor.FromRgb(190, 190, 190),
                Background = OxyColor.FromAColor(140, OxyColors.Black),
                Stroke = OxyColors.Transparent,
                TextPosition = new DataPoint(DateTimeAxis.ToDouble(anchor), index + BandPadding + 0.1),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
                TextVerticalAlignment = OxyPlot.VerticalAlignment.Top,
                Layer = AnnotationLayer.AboveSeries
            };

            model.Annotations.Add(annotation);
        }

        private void AddStateBand(PlotModel model, SignalType signalType, int index, SignalState state, double level, OxyColor fill, DateTime start, DateTime end)
        {
            var x0 = DateTimeAxis.ToDouble(state.StartTimestamp < start ? start : state.StartTimestamp);
            var x1 = DateTimeAxis.ToDouble(state.EndTimestamp > end ? end : state.EndTimestamp);
            if (x1 <= x0)
            {
                return;
            }

            var span = x1 - x0;
            var bevel = Math.Min(span * 0.1, TimeSpan.FromMilliseconds(MinTransitionMilliseconds).TotalDays);
            var baseline = index;
            double lower;
            double upper;

            if (signalType == SignalType.String)
            {
                var band = new RectangleAnnotation
                {
                    MinimumX = x0,
                    MaximumX = x1,
                    MinimumY = index - BandPadding,
                    MaximumY = index + BandPadding,
                    Fill = fill,
                    Stroke = OxyColor.FromRgb(70, 70, 70),
                    StrokeThickness = 1.1,
                    Layer = AnnotationLayer.BelowSeries
                };

                model.Annotations.Add(band);

                if (bevel > 0)
                {
                    var left = new PolygonAnnotation
                    {
                        Fill = fill,
                        Stroke = OxyColor.FromRgb(70, 70, 70),
                        StrokeThickness = 1.1,
                        Layer = AnnotationLayer.BelowSeries
                    };
                    left.Points.Add(new DataPoint(x0 - bevel, index));
                    left.Points.Add(new DataPoint(x0, index + BandPadding));
                    left.Points.Add(new DataPoint(x0, index - BandPadding));
                    model.Annotations.Add(left);

                    var right = new PolygonAnnotation
                    {
                        Fill = fill,
                        Stroke = OxyColor.FromRgb(70, 70, 70),
                        StrokeThickness = 1.1,
                        Layer = AnnotationLayer.BelowSeries
                    };
                    right.Points.Add(new DataPoint(x1 + bevel, index));
                    right.Points.Add(new DataPoint(x1, index + BandPadding));
                    right.Points.Add(new DataPoint(x1, index - BandPadding));
                    model.Annotations.Add(right);
                }

                return;
            }

            if (signalType == SignalType.Boolean)
            {
                var isHigh = InterpretBoolean(state.Value);
                lower = isHigh ? baseline : baseline - WaveAmplitude;
                upper = isHigh ? baseline + WaveAmplitude : baseline;
            }
            else
            {
                lower = level - WaveAmplitude * 0.25;
                upper = level + WaveAmplitude * 0.25;
            }

            var polygon = new PolygonAnnotation
            {
                Fill = fill,
                Stroke = OxyColors.Transparent,
                Layer = AnnotationLayer.BelowSeries
            };

            polygon.Points.Add(new DataPoint(x0, lower));
            polygon.Points.Add(new DataPoint(x0 + bevel, upper));
            polygon.Points.Add(new DataPoint(x1 - bevel, upper));
            polygon.Points.Add(new DataPoint(x1, lower));

            model.Annotations.Add(polygon);
        }

        private void AddStateLabel(PlotModel model, SignalType signalType, int index, SignalState state, double level, OxyColor color)
        {
            if (signalType == SignalType.Boolean)
            {
                return;
            }

            var text = FormatStateLabel(signalType, state.Value, false);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var duration = (state.EndTimestamp - state.StartTimestamp).TotalSeconds;
            if (duration < 0.02)
            {
                return;
            }

            var midpoint = state.StartTimestamp + TimeSpan.FromTicks((state.EndTimestamp - state.StartTimestamp).Ticks / 2);
            var positionY = signalType == SignalType.String ? index : level + (level >= index ? 0.08 : -0.08);
            var annotation = new TextAnnotation
            {
                Text = text,
                FontSize = 10,
                TextColor = OxyColor.FromAColor(220, color),
                Stroke = OxyColors.Transparent,
                Background = OxyColors.Transparent,
                TextPosition = new DataPoint(DateTimeAxis.ToDouble(midpoint), positionY),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle,
                Layer = AnnotationLayer.AboveSeries
            };

            model.Annotations.Add(annotation);
        }

        private static OxyColor SelectColor(SignalType signalType, object? value, bool isSynthetic)
        {
            if (isSynthetic || value == null)
            {
                return OxyColor.FromRgb(90, 90, 90);
            }

            return signalType switch
            {
                SignalType.Boolean => InterpretBoolean(value)
                    ? OxyColor.FromRgb(94, 189, 136)
                    : OxyColor.FromRgb(200, 112, 106),
                SignalType.Integer => OxyColor.FromRgb(80, 140, 220),
                SignalType.String => OxyColor.FromRgb(214, 174, 120),
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
                SignalType.Boolean => string.Empty,
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

        private static double? TryConvertToDouble(object? value)
        {
            if (value == null)
            {
                return null;
            }

            switch (value)
            {
                case double d:
                    return d;
                case float f:
                    return f;
                case int i:
                    return i;
                case long l:
                    return l;
                case short s:
                    return s;
                case byte b:
                    return b;
                case string str when double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                    return parsed;
                case IConvertible convertible:
                    try
                    {
                        return convertible.ToDouble(CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        return null;
                    }
                default:
                    return null;
            }
        }

        private static bool AreClose(double left, double right, double tolerance = 1e-9)
        {
            return Math.Abs(left - right) <= tolerance;
        }

        private static Tuple<DateTime, DateTime>? GetRangeFromSignals(IReadOnlyList<SignalData> signals)
        {
            if (signals == null || signals.Count == 0)
            {
                return null;
            }

            var earliest = DateTime.MaxValue;
            var latest = DateTime.MinValue;

            foreach (var signal in signals)
            {
                if (signal?.States == null)
                {
                    continue;
                }

                foreach (var state in signal.States)
                {
                    if (state.StartTimestamp < earliest)
                    {
                        earliest = state.StartTimestamp;
                    }

                    if (state.EndTimestamp > latest)
                    {
                        latest = state.EndTimestamp;
                    }
                }
            }

            if (latest <= earliest || earliest == DateTime.MaxValue)
            {
                return null;
            }

            return Tuple.Create(earliest, latest);
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
