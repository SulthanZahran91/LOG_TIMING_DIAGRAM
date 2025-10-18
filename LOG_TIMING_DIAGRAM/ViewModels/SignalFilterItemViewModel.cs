using LOG_TIMING_DIAGRAM.Models;

namespace LOG_TIMING_DIAGRAM.ViewModels
{
    public sealed class SignalFilterItemViewModel : ViewModelBase
    {
        private bool _isSelected;

        public SignalFilterItemViewModel(
            string key,
            string displayName,
            string deviceId,
            SignalType signalType,
            bool hasChanges,
            bool isSelected = false)
        {
            Key = key;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName;
            DeviceId = deviceId ?? string.Empty;
            SignalType = signalType;
            HasChanges = hasChanges;
            _isSelected = isSelected;
        }

        public string Key { get; }

        public string DisplayName { get; }

        public string DeviceId { get; }

        public SignalType SignalType { get; }

        public bool HasChanges { get; }

        public string DeviceGroupName => string.IsNullOrWhiteSpace(DeviceId) ? "Unknown Device" : DeviceId;

        public string DetailText => string.IsNullOrWhiteSpace(DeviceId)
            //? $"{SignalType}{(HasChanges ? " (changes)" : string.Empty)}"
            ? $"{SignalType}"
            //: $"{DeviceId} - {SignalType}{(HasChanges ? " (changes)" : string.Empty)}";
            : $"{DeviceId} - {SignalType}";

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
