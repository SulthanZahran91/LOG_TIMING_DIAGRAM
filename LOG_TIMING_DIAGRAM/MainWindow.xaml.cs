using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using LOG_TIMING_DIAGRAM.ViewModels;

namespace LOG_TIMING_DIAGRAM
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
        }

        private MainWindowViewModel ViewModel => DataContext as MainWindowViewModel;

        private async void OnLoadFilesClicked(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[MainWindow] Browse button clicked.");
            var dialog = new OpenFileDialog
            {
                Title = "Select PLC log files",
                Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*",
                Multiselect = true
            };

            Debug.WriteLine("[MainWindow] Showing file picker dialog.");
            if (dialog.ShowDialog(this) == true)
            {
                Debug.WriteLine($"[MainWindow] File picker returned {dialog.FileNames.Length} selection(s).");
                foreach (var file in dialog.FileNames)
                {
                    Debug.WriteLine($"[MainWindow] Selected file: {file}");
                }

                Debug.WriteLine("[MainWindow] Initiating file load.");
                await LoadFilesAsync(dialog.FileNames);
            }
            else
            {
                Debug.WriteLine("[MainWindow] File picker cancelled by user.");
            }

            Debug.WriteLine("[MainWindow] Browse handler complete.");
        }

        private async Task LoadFilesAsync(string[] fileNames)
        {
            Debug.WriteLine("[MainWindow] LoadFilesAsync invoked.");
            if (ViewModel == null || fileNames == null || fileNames.Length == 0)
            {
                Debug.WriteLine("[MainWindow] LoadFilesAsync aborted: missing view-model or files.");
                return;
            }

            Debug.WriteLine($"[MainWindow] Loading {fileNames.Length} file(s).");

            await ViewModel.LoadFilesAsync(fileNames);

            Debug.WriteLine("[MainWindow] LoadFilesAsync completed.");
        }

        private async void OnFilesDropped(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any())
            {
                await LoadFilesAsync(files);
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void OnDurationTextPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox textBox)
            {
                var binding = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
                binding?.UpdateSource();
                textBox.SelectAll();
                e.Handled = true;
            }
        }
    }
}
