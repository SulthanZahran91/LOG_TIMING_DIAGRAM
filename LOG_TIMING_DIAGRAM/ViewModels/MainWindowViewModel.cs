using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using LOG_TIMING_DIAGRAM.Models;
using LOG_TIMING_DIAGRAM.Parsers;
using LOG_TIMING_DIAGRAM.Utils;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace LOG_TIMING_DIAGRAM.ViewModels
{
    public sealed class MainWindowViewModel : ViewModelBase
    {
        private readonly ObservableCollection<LogEntry> _entries;
        private readonly ObservableCollection<SignalData> _activeSignals;
        private readonly ObservableCollection<LogEntry> _visibleEntries;
        private readonly ObservableCollection<SignalFilterItemViewModel> _signalFilters;
        private readonly Dictionary<string, SignalCache> _signalLookup;
        private readonly List<string> _currentFiles;
        private readonly ICollectionView _filterCollectionView;
        private readonly ObservableCollection<string> _filterPresetNames;
        private readonly Dictionary<string, FilterPreset> _filterPresets;
        private readonly string _presetStoragePath;

        private bool _hasData;
        private bool _isBusy;
        private double _windowDurationSeconds = 10.0;
        private double _timelinePosition;
        private double _zoomLevel = 1.0;
        private bool _isApplyingViewportWindow;
        private bool _isApplyingZoom;
        private string _visibleRangeText = string.Empty;
        private Tuple<DateTime, DateTime> _logTimeRange;
        private string _filterSearchText = string.Empty;
        private bool _filterIncludeBoolean = true;
        private bool _filterIncludeInteger = true;
        private bool _filterIncludeString = true;
        private bool _filterShowOnlyChanged;
        private string _filterStatusText = "No signals loaded.";
        private bool _suppressFilterRefresh;
        private bool _suppressSelectionNotifications;
        private bool _isApplyingPreset;
        private string _selectedFilterPreset;
        private string _newFilterPresetName = string.Empty;

        public MainWindowViewModel()
        {
            _entries = new ObservableCollection<LogEntry>();
            _activeSignals = new ObservableCollection<SignalData>();
            _visibleEntries = new ObservableCollection<LogEntry>();
            _signalFilters = new ObservableCollection<SignalFilterItemViewModel>();
            _signalLookup = new Dictionary<string, SignalCache>(StringComparer.OrdinalIgnoreCase);
            _currentFiles = new List<string>();
            _filterCollectionView = CollectionViewSource.GetDefaultView(_signalFilters);
            if (_filterCollectionView != null)
            {
                _filterCollectionView.Filter = FilterSignalItem;
                if (_filterCollectionView is ListCollectionView listCollectionView)
                {
                    listCollectionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SignalFilterItemViewModel.DeviceGroupName)));
                    listCollectionView.SortDescriptions.Add(new SortDescription(nameof(SignalFilterItemViewModel.DeviceGroupName), ListSortDirection.Ascending));
                    listCollectionView.SortDescriptions.Add(new SortDescription(nameof(SignalFilterItemViewModel.DisplayName), ListSortDirection.Ascending));
                }
            }

            _filterPresetNames = new ObservableCollection<string>();
            FilterPresetNames = new ReadOnlyObservableCollection<string>(_filterPresetNames);
            _filterPresets = new Dictionary<string, FilterPreset>(StringComparer.OrdinalIgnoreCase);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _presetStoragePath = Path.Combine(appData, "LOG_TIMING_DIAGRAM", "filter-presets.json");

            Stats = new StatsViewModel();
            ParseJob = new ParseJobViewModel();
            Viewport = new ViewportStateViewModel();

            CancelParseCommand = new RelayCommand(() => ParseJob.Cancel(), () => ParseJob.IsRunning);
            ClearCommand = new RelayCommand(Clear, () => HasData && !IsBusy);

            ZoomInCommand = new RelayCommand(ZoomIn, CanNavigate);
            ZoomOutCommand = new RelayCommand(ZoomOut, CanNavigate);
            ResetViewCommand = new RelayCommand(ResetViewportToFull, () => HasData);
            PanLeftCommand = new RelayCommand(() => PanByFraction(-0.1), CanNavigate);
            PanRightCommand = new RelayCommand(() => PanByFraction(0.1), CanNavigate);
            SelectAllFiltersCommand = new RelayCommand(SelectAllFilters, () => HasVisibleFilter(filter => !filter.IsSelected));
            DeselectAllFiltersCommand = new RelayCommand(DeselectAllFilters, () => HasVisibleFilter(filter => filter.IsSelected));
            ClearFiltersCommand = new RelayCommand(ClearFilters, () => _signalFilters.Count > 0);
            ToggleDeviceSelectionCommand = new RelayCommand<CollectionViewGroup>(ToggleDeviceSelection, group => group != null);
            SaveFilterPresetCommand = new RelayCommand(SaveCurrentFilterPreset, CanSaveFilterPreset);
            DeleteFilterPresetCommand = new RelayCommand(DeleteSelectedFilterPreset, () => !string.IsNullOrEmpty(SelectedFilterPreset));

            ParseJob.PropertyChanged += (sender, args) =>
            {
                if (string.Equals(args.PropertyName, nameof(ParseJobViewModel.IsRunning), StringComparison.Ordinal))
                {
                    CancelParseCommand.RaiseCanExecuteChanged();
                    RefreshCommandStates();
                }
            };

            Viewport.TimeRangeChanged += OnViewportTimeRangeChanged;
            Viewport.ZoomLevelChanged += OnViewportZoomLevelChanged;

            LoadFilterPresets();
        }

        private void RefreshVisibleEntries(ICollection<string> selectedKeys)
        {
            _visibleEntries.Clear();

            if (selectedKeys == null || selectedKeys.Count == 0)
            {
                return;
            }

            var orderedEntries = new List<LogEntry>();

            foreach (var key in selectedKeys)
            {
                if (key == null)
                {
                    continue;
                }

                if (_signalLookup.TryGetValue(key, out var cache) && cache.Entries != null)
                {
                    orderedEntries.AddRange(cache.Entries);
                }
            }

            if (orderedEntries.Count == 0)
            {
                return;
            }

            orderedEntries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            foreach (var entry in orderedEntries)
            {
                _visibleEntries.Add(entry);
            }
        }

        public IReadOnlyList<LogEntry> Entries => _entries;

        public ObservableCollection<SignalData> ActiveSignals => _activeSignals;

        public ObservableCollection<LogEntry> VisibleEntries => _visibleEntries;

        public ObservableCollection<SignalFilterItemViewModel> SignalFilters => _signalFilters;

        public StatsViewModel Stats { get; }

        public ParseJobViewModel ParseJob { get; }

        public ViewportStateViewModel Viewport { get; }

        public RelayCommand CancelParseCommand { get; }

        public RelayCommand ClearCommand { get; }

        public RelayCommand ZoomInCommand { get; }

        public RelayCommand ZoomOutCommand { get; }

        public RelayCommand ResetViewCommand { get; }

        public RelayCommand PanLeftCommand { get; }

        public RelayCommand PanRightCommand { get; }

        public RelayCommand SelectAllFiltersCommand { get; }

        public RelayCommand DeselectAllFiltersCommand { get; }

        public RelayCommand ClearFiltersCommand { get; }

        public RelayCommand<CollectionViewGroup> ToggleDeviceSelectionCommand { get; }

        public RelayCommand SaveFilterPresetCommand { get; }

        public RelayCommand DeleteFilterPresetCommand { get; }

        public ReadOnlyObservableCollection<string> FilterPresetNames { get; }

        public string SelectedFilterPreset
        {
            get => _selectedFilterPreset;
            set
            {
                if (SetProperty(ref _selectedFilterPreset, value))
                {
                    DeleteFilterPresetCommand?.RaiseCanExecuteChanged();
                    if (!string.IsNullOrEmpty(value))
                    {
                        ApplyFilterPreset(value);
                    }
                }
            }
        }

        public string NewFilterPresetName
        {
            get => _newFilterPresetName;
            set
            {
                if (SetProperty(ref _newFilterPresetName, value))
                {
                    SaveFilterPresetCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasData
        {
            get => _hasData;
            private set
            {
                if (SetProperty(ref _hasData, value))
                {
                    ClearCommand.RaiseCanExecuteChanged();
                    ResetViewCommand.RaiseCanExecuteChanged();
                    RefreshCommandStates();
                    SaveFilterPresetCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    ClearCommand.RaiseCanExecuteChanged();
                    RefreshCommandStates();
                }
            }
        }

        public double WindowDurationSeconds
        {
            get => _windowDurationSeconds;
            set
            {
                var clamped = NormalizeWindowDuration(value);
                if (SetProperty(ref _windowDurationSeconds, clamped) && !_isApplyingViewportWindow)
                {
                    UpdateViewportWindow();
                }
            }
        }

        public double TimelinePosition
        {
            get => _timelinePosition;
            set
            {
                var clamped = Math.Max(0.0, Math.Min(1.0, value));
                if (SetProperty(ref _timelinePosition, clamped) && !_isApplyingViewportWindow)
                {
                    UpdateViewportWindow();
                }
            }
        }

        public double ZoomLevel
        {
            get => _zoomLevel;
            set => SetZoomLevelInternal(value, propagate: true);
        }

        public string VisibleRangeText
        {
            get => _visibleRangeText;
            private set => SetProperty(ref _visibleRangeText, value);
        }

        public ICollectionView SignalFilterView => _filterCollectionView;

        public string FilterSearchText
        {
            get => _filterSearchText;
            set
            {
                var normalized = value ?? string.Empty;
                if (SetProperty(ref _filterSearchText, normalized))
                {
                    if (!_suppressFilterRefresh)
                    {
                        RefreshFilterView();
                    }

                    MarkPresetDirty();
                }
            }
        }

        public bool FilterIncludeBoolean
        {
            get => _filterIncludeBoolean;
            set
            {
                if (SetProperty(ref _filterIncludeBoolean, value))
                {
                    if (!_suppressFilterRefresh)
                    {
                        RefreshFilterView();
                    }

                    MarkPresetDirty();
                }
            }
        }

        public bool FilterIncludeInteger
        {
            get => _filterIncludeInteger;
            set
            {
                if (SetProperty(ref _filterIncludeInteger, value))
                {
                    if (!_suppressFilterRefresh)
                    {
                        RefreshFilterView();
                    }

                    MarkPresetDirty();
                }
            }
        }

        public bool FilterIncludeString
        {
            get => _filterIncludeString;
            set
            {
                if (SetProperty(ref _filterIncludeString, value))
                {
                    if (!_suppressFilterRefresh)
                    {
                        RefreshFilterView();
                    }

                    MarkPresetDirty();
                }
            }
        }

        public bool FilterShowOnlyChanged
        {
            get => _filterShowOnlyChanged;
            set
            {
                if (SetProperty(ref _filterShowOnlyChanged, value))
                {
                    if (!_suppressFilterRefresh)
                    {
                        RefreshFilterView();
                    }

                    MarkPresetDirty();
                }
            }
        }

        public string FilterStatusText
        {
            get => _filterStatusText;
            private set => SetProperty(ref _filterStatusText, value);
        }

        public async Task LoadFilesAsync(IEnumerable<string> filePaths)
        {
            Debug.WriteLine("[MainWindowViewModel] LoadFilesAsync invoked.");
            if (filePaths == null)
            {
                Debug.WriteLine("[MainWindowViewModel] Aborting: filePaths was null.");
                return;
            }

            var paths = filePaths
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Select(p => Path.GetFullPath(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Debug.WriteLine($"[MainWindowViewModel] Candidate paths after validation: {paths.Count}.");
            if (paths.Count == 0)
            {
                Debug.WriteLine("[MainWindowViewModel] No usable paths found. Exiting.");
                return;
            }

            if (IsBusy)
            {
                Debug.WriteLine("[MainWindowViewModel] Parse already in progress. Ignoring request.");
                return;
            }

            IsBusy = true;
            Debug.WriteLine("[MainWindowViewModel] IsBusy set to true.");

            try
            {
                _currentFiles.Clear();
                _currentFiles.AddRange(paths);
                Debug.WriteLine($"[MainWindowViewModel] Tracking {_currentFiles.Count} current file(s).");

                Stats.Clear();
                _entries.Clear();
                _activeSignals.Clear();
                DetachSignalFilterHandlers();
                _signalFilters.Clear();
                _signalLookup.Clear();
                _visibleEntries.Clear();

                _logTimeRange = null;
                HasData = false;
                ClearViewportBindings();
                Debug.WriteLine("[MainWindowViewModel] Cleared previous parse data.");

                ParseJob.Start(paths[0]);
                var token = ParseJob.Token;
                var progress = new Progress<ParseProgress>(p => ParseJob.Update(p));
                Debug.WriteLine($"[MainWindowViewModel] Parse job started. Primary file: {paths[0]}.");

                var resultMap = new Dictionary<string, ParseResult>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in paths)
                {
                    Debug.WriteLine($"[MainWindowViewModel] Parsing file '{file}'.");
                    ParseJob.Update(new ParseProgress(file, 0));
                    var result = await ParserRegistry.Instance.ParseAsync(file, null, token, progress);
                    resultMap[file] = result;
                    Debug.WriteLine($"[MainWindowViewModel] Finished parsing '{file}'. Success={result?.Success ?? false}; Errors={result?.Errors?.Count ?? 0}.");
                }

                Debug.WriteLine("[MainWindowViewModel] Merging parse results.");
                var merged = ParseResultMerger.Merge(resultMap);
                Stats.Update(merged);
                Debug.WriteLine($"[MainWindowViewModel] Merge completed. Success={merged.Success}; EntryCount={merged.Data?.Entries.Count ?? 0}; ErrorCount={merged.Errors.Count}.");

                if (!merged.Success || merged.Data == null)
                {
                    Debug.WriteLine("[MainWindowViewModel] Merge did not produce data. Reporting failure.");
                    ParseJob.Fail("No entries parsed.");
                    return;
                }

                Viewport.SetFullTimeRange(merged.Data.TimeRange.Item1, merged.Data.TimeRange.Item2);
                Debug.WriteLine($"[MainWindowViewModel] Viewport set to {merged.Data.TimeRange.Item1:o} - {merged.Data.TimeRange.Item2:o}.");

                foreach (var entry in merged.Data.Entries)
                {
                    _entries.Add(entry);
                }
                Debug.WriteLine($"[MainWindowViewModel] Added {_entries.Count} entries.");

                _logTimeRange = merged.Data.TimeRange;

                DetachSignalFilterHandlers();
                _signalFilters.Clear();
                _signalLookup.Clear();
                
                _activeSignals.Clear();

                var groupedSignals = SignalProcessing.GroupBySignal(merged.Data)
                    .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var kvp in groupedSignals)
                {
                    var entriesForSignal = kvp.Value;
                    if (entriesForSignal == null || entriesForSignal.Count == 0)
                    {
                        continue;
                    }

                    var firstEntry = entriesForSignal[0];
                    var cache = new SignalCache(
                        kvp.Key,
                        CreateSignalDisplayName(firstEntry.DeviceId, firstEntry.SignalName),
                        firstEntry.DeviceId,
                        firstEntry.SignalType,
                        entriesForSignal);

                    _signalLookup[kvp.Key] = cache;

                    var filterItem = new SignalFilterItemViewModel(
                        cache.Key,
                        cache.DisplayName,
                        cache.DeviceId,
                        cache.SignalType,
                        cache.HasChanges);
                    filterItem.PropertyChanged += OnSignalFilterChanged;
                    _signalFilters.Add(filterItem);
                }
                Debug.WriteLine($"[MainWindowViewModel] Prepared {_signalLookup.Count} signal filters.");

                ClearFilters();
                ResetViewportWindow();

                HasData = true;
                ParseJob.Complete("Parse complete");
                Debug.WriteLine("[MainWindowViewModel] Parse job completed successfully.");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[MainWindowViewModel] Parsing cancelled.");
                ParseJob.Fail("Parsing canceled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindowViewModel] Exception during parsing: {ex}");
                ParseJob.Fail(ex.Message);
            }
            finally
            {
                IsBusy = false;
                RefreshCommandStates();
                Debug.WriteLine("[MainWindowViewModel] LoadFilesAsync finished. IsBusy reset to false.");
            }
        }

        private void Clear()
        {
            _entries.Clear();
            _activeSignals.Clear();
            _visibleEntries.Clear();
            DetachSignalFilterHandlers();
            _signalFilters.Clear();
            _signalLookup.Clear();
            _suppressFilterRefresh = true;
            FilterSearchText = string.Empty;
            FilterIncludeBoolean = true;
            FilterIncludeInteger = true;
            FilterIncludeString = true;
            FilterShowOnlyChanged = false;
            _suppressFilterRefresh = false;
            RefreshFilterView();

            _currentFiles.Clear();
            _logTimeRange = null;
            HasData = false;
            Stats.Clear();
            FilterStatusText = "No signals loaded.";
            UpdateFilterCommands();
            ClearViewportBindings();
        }

        private void OnSignalFilterChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_suppressSelectionNotifications)
            {
                return;
            }

            if (!string.Equals(e.PropertyName, nameof(SignalFilterItemViewModel.IsSelected), StringComparison.Ordinal))
            {
                return;
            }

            UpdateVisibleSignalsFromFilters();
        }

        private void UpdateVisibleSignalsFromFilters()
        {
            _activeSignals.Clear();
            var selectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var filter in _signalFilters)
            {
                if (!filter.IsSelected)
                {
                    continue;
                }

                if (_signalLookup.TryGetValue(filter.Key, out var cache))
                {
                    selectedKeys.Add(cache.Key);
                    var signalData = cache.GetOrCreateSignalData(_logTimeRange);
                    if (signalData != null)
                    {
                        _activeSignals.Add(signalData);
                    }
                }
            }

            RefreshVisibleEntries(selectedKeys);
            UpdateFilterStatus();
            RefreshCommandStates();

            MarkPresetDirty();
            SaveFilterPresetCommand?.RaiseCanExecuteChanged();
        }

        public void JumpToTimestamp(DateTime timestamp)
        {
            if (!HasData)
            {
                return;
            }

            var visibleStart = Viewport.VisibleStart;
            var visibleEnd = Viewport.VisibleEnd;
            var currentDuration = visibleEnd > visibleStart
                ? (visibleEnd - visibleStart)
                : TimeSpan.FromSeconds(NormalizeWindowDuration(_windowDurationSeconds));

            if (currentDuration <= TimeSpan.Zero)
            {
                currentDuration = TimeSpan.FromSeconds(10);
            }

            Viewport.JumpToTime(timestamp, currentDuration);
        }

        private void RefreshFilterView()
        {
            _filterCollectionView?.Refresh();
            UpdateFilterStatus();
            UpdateFilterCommands();
        }

        private void UpdateFilterStatus()
        {
            if (_signalLookup.Count == 0)
            {
                FilterStatusText = "No signals loaded.";
                return;
            }

            var total = _signalLookup.Count;
            var filtered = _signalFilters.Count(IsFilterVisible);
            var activeCount = _activeSignals.Count;
            var visibleDevices = _signalFilters
                .Where(IsFilterVisible)
                .GroupBy(filter => filter.DeviceId)
                .ToList();

            var selectedDevices = visibleDevices.Count(group => group.All(filter => filter.IsSelected));

            FilterStatusText = $"Visible: {filtered}/{total} | Selected: {activeCount} | Devices: {selectedDevices}/{visibleDevices.Count}";
        }

        private void UpdateFilterCommands()
        {
            SelectAllFiltersCommand?.RaiseCanExecuteChanged();
            DeselectAllFiltersCommand?.RaiseCanExecuteChanged();
            ClearFiltersCommand?.RaiseCanExecuteChanged();
        }

        private bool FilterSignalItem(object item)
        {
            return item is SignalFilterItemViewModel filter && IsFilterVisible(filter);
        }

        private bool IsFilterVisible(SignalFilterItemViewModel filter)
        {
            if (filter == null)
            {
                return false;
            }

            switch (filter.SignalType)
            {
                case SignalType.Boolean:
                    if (!FilterIncludeBoolean)
                    {
                        return false;
                    }

                    break;
                case SignalType.Integer:
                    if (!FilterIncludeInteger)
                    {
                        return false;
                    }

                    break;
                case SignalType.String:
                default:
                    if (!FilterIncludeString)
                    {
                        return false;
                    }

                    break;
            }

            if (FilterShowOnlyChanged && !filter.HasChanges)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_filterSearchText))
            {
                var query = _filterSearchText.Trim();
                if (query.Length > 0)
                {
                    if (query.StartsWith("/", StringComparison.Ordinal) && query.Length > 1)
                    {
                        var pattern = query.Substring(1);
                        try
                        {
                            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                            if (!MatchesRegex(filter, regex))
                            {
                                return false;
                            }

                            return true;
                        }
                        catch (ArgumentException)
                        {
                            // Ignore invalid regex and fall back to substring matching.
                        }
                    }

                    var comparison = StringComparison.OrdinalIgnoreCase;
                    if (!ContainsSubstring(filter, query, comparison))
                    {
                        return false;
                    }
                }
            }

            return true;

            static bool MatchesRegex(SignalFilterItemViewModel item, Regex regex)
            {
                if (regex == null || item == null)
                {
                    return false;
                }

                return (item.DisplayName != null && regex.IsMatch(item.DisplayName))
                    || (item.DeviceId != null && regex.IsMatch(item.DeviceId))
                    || (item.Key != null && regex.IsMatch(item.Key));
            }

            static bool ContainsSubstring(SignalFilterItemViewModel item, string term, StringComparison comparison)
            {
                if (item == null || string.IsNullOrEmpty(term))
                {
                    return false;
                }

                return (item.DisplayName?.IndexOf(term, comparison) ?? -1) >= 0
                    || (item.DeviceId?.IndexOf(term, comparison) ?? -1) >= 0
                    || (item.Key?.IndexOf(term, comparison) ?? -1) >= 0;
            }
        }

        private bool HasVisibleFilter(Func<SignalFilterItemViewModel, bool> predicate)
        {
            foreach (var filter in _signalFilters)
            {
                if (IsFilterVisible(filter) && predicate(filter))
                {
                    return true;
                }
            }

            return false;
        }

        internal bool? GetDeviceSelectionState(CollectionViewGroup group)
        {
            if (group == null)
            {
                return false;
            }

            var items = group.Items?.OfType<SignalFilterItemViewModel>().ToList();
            if (items == null || items.Count == 0)
            {
                return false;
            }

            var selectedCount = items.Count(item => item.IsSelected);
            if (selectedCount == 0)
            {
                return false;
            }

            if (selectedCount == items.Count)
            {
                return true;
            }

            return null;
        }

        private void ToggleDeviceSelection(CollectionViewGroup group)
        {
            if (group == null)
            {
                return;
            }

            var currentState = GetDeviceSelectionState(group);
            var selectAll = currentState != true;
            ApplyDeviceSelection(group, selectAll);
        }

        private void ApplyDeviceSelection(CollectionViewGroup group, bool selectAll)
        {
            var items = group?.Items?.OfType<SignalFilterItemViewModel>().ToList();
            if (items == null || items.Count == 0)
            {
                return;
            }

            _suppressSelectionNotifications = true;
            try
            {
                foreach (var item in items)
                {
                    item.IsSelected = selectAll;
                }
            }
            finally
            {
                _suppressSelectionNotifications = false;
            }

            UpdateVisibleSignalsFromFilters();
        }

        private void SelectAllFilters()
        {
            if (_signalFilters.Count == 0)
            {
                return;
            }

            _suppressSelectionNotifications = true;
            try
            {
                foreach (var filter in _signalFilters)
                {
                    if (IsFilterVisible(filter))
                    {
                        filter.IsSelected = true;
                    }
                }
            }
            finally
            {
                _suppressSelectionNotifications = false;
            }

            UpdateVisibleSignalsFromFilters();
        }

        private void DeselectAllFilters()
        {
            if (_signalFilters.Count == 0)
            {
                return;
            }

            _suppressSelectionNotifications = true;
            try
            {
                foreach (var filter in _signalFilters)
                {
                    if (IsFilterVisible(filter))
                    {
                        filter.IsSelected = false;
                    }
                }
            }
            finally
            {
                _suppressSelectionNotifications = false;
            }

            UpdateVisibleSignalsFromFilters();
        }

        private void ClearFilters()
        {
            _suppressFilterRefresh = true;
            try
            {
                FilterSearchText = string.Empty;
                FilterIncludeBoolean = true;
                FilterIncludeInteger = true;
                FilterIncludeString = true;
                FilterShowOnlyChanged = false;
            }
            finally
            {
                _suppressFilterRefresh = false;
            }

            RefreshFilterView();

            _suppressSelectionNotifications = true;
            try
            {
                foreach (var filter in _signalFilters)
                {
                    filter.IsSelected = false;
                }
            }
            finally
            {
                _suppressSelectionNotifications = false;
            }

            UpdateVisibleSignalsFromFilters();
        }

        private void MarkPresetDirty()
        {
            if (_isApplyingPreset)
            {
                return;
            }

            if (!string.IsNullOrEmpty(_selectedFilterPreset))
            {
                _selectedFilterPreset = null;
                OnPropertyChanged(nameof(SelectedFilterPreset));
            }

            SaveFilterPresetCommand?.RaiseCanExecuteChanged();
            DeleteFilterPresetCommand?.RaiseCanExecuteChanged();
        }

        private bool CanSaveFilterPreset()
        {
            return HasData && !string.IsNullOrWhiteSpace(NewFilterPresetName) && _signalFilters.Count > 0;
        }

        private void SaveCurrentFilterPreset()
        {
            var name = NewFilterPresetName?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var preset = CaptureCurrentFilterPreset();
            preset.Name = name;

            _filterPresets[name] = preset;
            UpdatePresetCollections();
            PersistFilterPresets();

            _isApplyingPreset = true;
            try
            {
                SelectedFilterPreset = name;
            }
            finally
            {
                _isApplyingPreset = false;
            }

            NewFilterPresetName = string.Empty;
        }

        private void DeleteSelectedFilterPreset()
        {
            if (string.IsNullOrEmpty(SelectedFilterPreset))
            {
                return;
            }

            if (_filterPresets.Remove(SelectedFilterPreset))
            {
                PersistFilterPresets();
                UpdatePresetCollections();
            }

            _selectedFilterPreset = null;
            OnPropertyChanged(nameof(SelectedFilterPreset));
            SaveFilterPresetCommand?.RaiseCanExecuteChanged();
        }

        private FilterPreset CaptureCurrentFilterPreset()
        {
            var selectedSignals = _signalFilters
                .Where(filter => filter.IsSelected)
                .Select(filter => filter.Key)
                .ToList();

            var selectedDevices = _signalFilters
                .GroupBy(filter => filter.DeviceId)
                .Where(group => group.Any() && group.All(filter => filter.IsSelected))
                .Select(group => group.Key)
                .ToList();

            return new FilterPreset
            {
                SearchText = FilterSearchText,
                IncludeBoolean = FilterIncludeBoolean,
                IncludeInteger = FilterIncludeInteger,
                IncludeString = FilterIncludeString,
                ShowOnlyChanged = FilterShowOnlyChanged,
                SelectedSignals = selectedSignals,
                SelectedDevices = selectedDevices
            };
        }

        private void ApplyFilterPreset(string name)
        {
            if (!_filterPresets.TryGetValue(name, out var preset))
            {
                return;
            }

            _isApplyingPreset = true;
            try
            {
                _suppressFilterRefresh = true;
                _suppressSelectionNotifications = true;
                try
                {
                    FilterSearchText = preset.SearchText ?? string.Empty;
                    FilterIncludeBoolean = preset.IncludeBoolean;
                    FilterIncludeInteger = preset.IncludeInteger;
                    FilterIncludeString = preset.IncludeString;
                    FilterShowOnlyChanged = preset.ShowOnlyChanged;

                    var selectedKeys = new HashSet<string>(preset.SelectedSignals ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                    var selectAll = selectedKeys.Count == 0;

                    foreach (var filter in _signalFilters)
                    {
                        filter.IsSelected = selectAll || selectedKeys.Contains(filter.Key);
                    }
                }
                finally
                {
                    _suppressSelectionNotifications = false;
                    _suppressFilterRefresh = false;
                }

                RefreshFilterView();
                UpdateVisibleSignalsFromFilters();
            }
            finally
            {
                _isApplyingPreset = false;
            }

            SaveFilterPresetCommand?.RaiseCanExecuteChanged();
        }

        private void LoadFilterPresets()
        {
            try
            {
                if (!File.Exists(_presetStoragePath))
                {
                    return;
                }

                var json = File.ReadAllText(_presetStoragePath);
                var presets = JsonSerializer.Deserialize<List<FilterPreset>>(json) ?? new List<FilterPreset>();

                _filterPresets.Clear();
                foreach (var preset in presets)
                {
                    if (!string.IsNullOrWhiteSpace(preset.Name))
                    {
                        _filterPresets[preset.Name] = preset;
                    }
                }

                UpdatePresetCollections();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindowViewModel] Failed to load presets: {ex}");
            }
        }

        private void PersistFilterPresets()
        {
            try
            {
                var directory = Path.GetDirectoryName(_presetStoragePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var payload = _filterPresets.Values
                    .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_presetStoragePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindowViewModel] Failed to save presets: {ex}");
            }
        }

        private void UpdatePresetCollections()
        {
            _filterPresetNames.Clear();
            foreach (var name in _filterPresets.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                _filterPresetNames.Add(name);
            }

            SaveFilterPresetCommand?.RaiseCanExecuteChanged();
            DeleteFilterPresetCommand?.RaiseCanExecuteChanged();
        }

        private void DetachSignalFilterHandlers()
        {
            foreach (var filter in _signalFilters)
            {
                filter.PropertyChanged -= OnSignalFilterChanged;
            }
        }

        private void ResetViewportWindow()
        {
            _isApplyingViewportWindow = true;
            try
            {
                const double defaultWindowSeconds = 10.0;
                if (!AreClose(_windowDurationSeconds, defaultWindowSeconds))
                {
                    _windowDurationSeconds = defaultWindowSeconds;
                    OnPropertyChanged(nameof(WindowDurationSeconds));
                }

                if (!AreClose(_timelinePosition, 0.0))
                {
                    _timelinePosition = 0.0;
                    OnPropertyChanged(nameof(TimelinePosition));
                }
            }
            finally
            {
                _isApplyingViewportWindow = false;
            }

            UpdateViewportWindow();
        }

        private void UpdateViewportWindow()
        {
            var fullStart = Viewport.FullStart;
            var fullEnd = Viewport.FullEnd;
            if (fullEnd <= fullStart)
            {
                return;
            }

            var total = fullEnd - fullStart;
            var windowSeconds = NormalizeWindowDuration(_windowDurationSeconds);
            windowSeconds = Math.Min(windowSeconds, total.TotalSeconds);
            if (windowSeconds <= 0)
            {
                windowSeconds = total.TotalSeconds;
            }

            var window = TimeSpan.FromSeconds(windowSeconds);
            var maxOffsetTicks = total.Ticks - window.Ticks;
            var position = Math.Max(0.0, Math.Min(1.0, _timelinePosition));
            var offsetTicks = maxOffsetTicks > 0 ? (long)Math.Round(maxOffsetTicks * position) : 0;
            offsetTicks = Math.Max(0, Math.Min(maxOffsetTicks, offsetTicks));

            var start = fullStart + TimeSpan.FromTicks(offsetTicks);
            var end = start + window;

            Viewport.SetTimeRange(start, end);
        }

        private void ClearViewportBindings()
        {
            _isApplyingViewportWindow = true;
            try
            {
                if (!AreClose(_windowDurationSeconds, 10.0))
                {
                    _windowDurationSeconds = 10.0;
                    OnPropertyChanged(nameof(WindowDurationSeconds));
                }

                if (!AreClose(_timelinePosition, 0.0))
                {
                    _timelinePosition = 0.0;
                    OnPropertyChanged(nameof(TimelinePosition));
                }
            }
            finally
            {
                _isApplyingViewportWindow = false;
            }

            SetZoomLevelInternal(1.0, propagate: false);
            VisibleRangeText = string.Empty;
        }

        private void RefreshViewportBindings()
        {
            var fullStart = Viewport.FullStart;
            var fullEnd = Viewport.FullEnd;
            if (fullEnd <= fullStart)
            {
                ClearViewportBindings();
                return;
            }

            var visibleStart = Viewport.VisibleStart;
            var visibleEnd = Viewport.VisibleEnd;
            if (visibleEnd < visibleStart)
            {
                visibleEnd = visibleStart;
            }

            var total = fullEnd - fullStart;
            var visible = visibleEnd - visibleStart;

            var offset = (visibleStart - fullStart).TotalSeconds;
            var maxOffset = Math.Max(total.TotalSeconds - visible.TotalSeconds, 0.0);
            var newPosition = maxOffset > 0 ? offset / maxOffset : 0.0;
            var newWindowSeconds = Math.Max(visible.TotalSeconds, 0.0);

            _isApplyingViewportWindow = true;
            try
            {
                if (!AreClose(_windowDurationSeconds, newWindowSeconds))
                {
                    _windowDurationSeconds = newWindowSeconds;
                    OnPropertyChanged(nameof(WindowDurationSeconds));
                }

                if (!AreClose(_timelinePosition, newPosition))
                {
                    _timelinePosition = Math.Max(0.0, Math.Min(1.0, newPosition));
                    OnPropertyChanged(nameof(TimelinePosition));
                }
            }
            finally
            {
                _isApplyingViewportWindow = false;
            }

            VisibleRangeText = FormatRange(visibleStart, visibleEnd);
        }

        private void ZoomIn()
        {
            if (!CanNavigate())
            {
                return;
            }

            Viewport.ZoomIn(1.25);
        }

        private void ZoomOut()
        {
            if (!CanNavigate())
            {
                return;
            }

            Viewport.ZoomOut(1.25);
        }

        private void ResetViewportToFull()
        {
            if (!HasData)
            {
                return;
            }

            Viewport.ResetZoom();
            RefreshViewportBindings();
        }

        private void PanByFraction(double fraction)
        {
            if (!CanNavigate())
            {
                return;
            }

            var visible = Viewport.VisibleEnd - Viewport.VisibleStart;
            if (visible <= TimeSpan.Zero)
            {
                return;
            }

            var deltaSeconds = visible.TotalSeconds * fraction;
            Viewport.Pan(TimeSpan.FromSeconds(deltaSeconds));
        }

        private void OnViewportTimeRangeChanged(object sender, EventArgs e)
        {
            RefreshViewportBindings();
        }

        private void OnViewportZoomLevelChanged(object sender, EventArgs e)
        {
            SetZoomLevelInternal(Viewport.ZoomLevel, propagate: false);
        }

        private bool CanNavigate() => HasData && !IsBusy && _activeSignals.Count > 0;

        private void RefreshCommandStates()
        {
            ZoomInCommand?.RaiseCanExecuteChanged();
            ZoomOutCommand?.RaiseCanExecuteChanged();
            ResetViewCommand?.RaiseCanExecuteChanged();
            PanLeftCommand?.RaiseCanExecuteChanged();
            PanRightCommand?.RaiseCanExecuteChanged();
            UpdateFilterCommands();
        }

        private void SetZoomLevelInternal(double value, bool propagate)
        {
            var clamped = Math.Max(1.0, Math.Min(1000.0, value));
            if (SetProperty(ref _zoomLevel, clamped) && propagate && !_isApplyingZoom)
            {
                _isApplyingZoom = true;
                try
                {
                    Viewport.SetZoomLevel(clamped);
                }
                finally
                {
                    _isApplyingZoom = false;
                }
            }
        }

        private static bool AreClose(double a, double b, double tolerance = 0.0001)
        {
            return Math.Abs(a - b) <= tolerance;
        }

        private static double NormalizeWindowDuration(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 10.0;
            }

            return Math.Max(0.1, Math.Min(86400.0, value));
        }

        private static string FormatRange(DateTime start, DateTime end)
        {
            if (end < start)
            {
                end = start;
            }

            var duration = end - start;
            if (duration < TimeSpan.Zero)
            {
                duration = TimeSpan.Zero;
            }

            return $"{start:yyyy-MM-dd HH:mm:ss.fff} - {end:yyyy-MM-dd HH:mm:ss.fff}  ({duration.TotalSeconds:F2}s)";
        }

        private static string CreateSignalDisplayName(string deviceId, string signalName)
        {
            if (string.IsNullOrEmpty(signalName))
            {
                return string.IsNullOrEmpty(deviceId) ? string.Empty : deviceId;
            }

            var separatorIndex = signalName.LastIndexOf("::", StringComparison.Ordinal);
            if (separatorIndex >= 0 && separatorIndex < signalName.Length - 2)
            {
                return signalName.Substring(separatorIndex + 2);
            }

            separatorIndex = signalName.LastIndexOf('/', signalName.Length - 1);
            if (separatorIndex >= 0 && separatorIndex < signalName.Length - 1)
            {
                return signalName.Substring(separatorIndex + 1);
            }

            return signalName;
        }

        private sealed class SignalCache
        {
            private SignalData _cachedData;

            public SignalCache(string key, string displayName, string deviceId, SignalType signalType, IReadOnlyList<LogEntry> entries)
            {
                Key = key;
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName;
                DeviceId = deviceId;
                SignalType = signalType;
                Entries = entries;
                HasChanges = DetectChanges(entries);
            }

            public string Key { get; }

            public string DisplayName { get; }

            public string DeviceId { get; }

            public SignalType SignalType { get; }

            public IReadOnlyList<LogEntry> Entries { get; }

            public bool HasChanges { get; }

            public SignalData GetOrCreateSignalData(Tuple<DateTime, DateTime> timeRange)
            {
                if (_cachedData == null)
                {
                    if (Entries == null || Entries.Count == 0)
                    {
                        return null;
                    }

                    var effectiveRange = timeRange ?? Tuple.Create(Entries[0].Timestamp, Entries[Entries.Count - 1].Timestamp);
                    var states = SignalProcessing.CalculateSignalStates(Entries, effectiveRange);
                    _cachedData = new SignalData(Key, DeviceId, DisplayName, SignalType, Entries, states);
                }

                return _cachedData;
            }

            private static bool DetectChanges(IReadOnlyList<LogEntry> entries)
            {
                if (entries == null || entries.Count <= 1)
                {
                    return false;
                }

                var previous = entries[0].Value;
                for (var i = 1; i < entries.Count; i++)
                {
                    var current = entries[i].Value;
                    if (!Equals(previous, current))
                    {
                        return true;
                    }

                    previous = current;
                }

                return false;
            }
        }
    }
}
