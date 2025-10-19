using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Markup;
using LOG_TIMING_DIAGRAM.ViewModels;

namespace LOG_TIMING_DIAGRAM.Utils
{
    /// <summary>
    /// Converts device group selection states to a tri-state checkbox value.
    /// </summary>
    [MarkupExtensionReturnType(typeof(DeviceSelectionConverter))]
    public sealed class DeviceSelectionConverter : MarkupExtension, IMultiValueConverter
    {
        public override object ProvideValue(IServiceProvider serviceProvider) => this;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
            {
                return false;
            }

            if (values[0] is CollectionViewGroup group && values[1] is MainWindowViewModel viewModel)
            {
                return viewModel.GetDeviceSelectionState(group);
            }

            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            // One-way binding only.
            return Array.Empty<object>();
        }
    }
}
