using WpfUserControl = System.Windows.Controls.UserControl;

using System.Windows.Input;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VirtualMixer.ViewModels;

namespace VirtualMixer.Views;

public partial class ChannelStripView : WpfUserControl
{
    private static ChannelStripViewModel? activeRenameViewModel;
    private static System.Windows.Controls.TextBox? activeRenameTextBox;
    private bool isFormattingRenameText;

    public ChannelStripView()
    {
        InitializeComponent();
    }

    private void ChannelName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ChannelStripViewModel viewModel && viewModel.CanRenameChannel)
        {
            if (activeRenameViewModel is not null &&
                !ReferenceEquals(activeRenameViewModel, viewModel) &&
                activeRenameViewModel.IsRenaming)
            {
                activeRenameViewModel.CommitRenameCommand.Execute(null);
            }

            viewModel.BeginRenameCommand.Execute(null);
            activeRenameViewModel = viewModel;
            Dispatcher.BeginInvoke(() =>
            {
                RenameTextBox.Focus();
                RenameTextBox.SelectAll();
                activeRenameTextBox = RenameTextBox;
            }, DispatcherPriority.Input);
            e.Handled = true;
        }
    }

    public static void CommitActiveRenameIfClickOutside(object? originalSource)
    {
        if (activeRenameViewModel is null || !activeRenameViewModel.IsRenaming)
        {
            return;
        }

        if (activeRenameTextBox is not null &&
            originalSource is DependencyObject dependencyObject &&
            IsDescendantOf(dependencyObject, activeRenameTextBox))
        {
            return;
        }

        activeRenameViewModel.CommitRenameCommand.Execute(null);
        activeRenameViewModel = null;
        activeRenameTextBox = null;
    }

    private void RenameTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not ChannelStripViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            viewModel.CommitRenameCommand.Execute(null);
            if (ReferenceEquals(activeRenameViewModel, viewModel))
            {
                activeRenameViewModel = null;
                activeRenameTextBox = null;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CancelRenameCommand.Execute(null);
            if (ReferenceEquals(activeRenameViewModel, viewModel))
            {
                activeRenameViewModel = null;
                activeRenameTextBox = null;
            }
            e.Handled = true;
        }
    }

    private void RenameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (isFormattingRenameText || sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        var compactText = textBox.Text.Replace("\u2009", string.Empty, StringComparison.Ordinal);
        var spacedText = ApplyDisplaySpacing(compactText);
        if (string.Equals(textBox.Text, spacedText, StringComparison.Ordinal))
        {
            return;
        }

        isFormattingRenameText = true;
        try
        {
            textBox.Text = spacedText;
            textBox.CaretIndex = textBox.Text.Length;
            if (DataContext is ChannelStripViewModel viewModel)
            {
                viewModel.PendingName = spacedText;
            }
        }
        finally
        {
            isFormattingRenameText = false;
        }
    }

    private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChannelStripViewModel viewModel && viewModel.IsRenaming)
        {
            viewModel.CommitRenameCommand.Execute(null);
            if (ReferenceEquals(activeRenameViewModel, viewModel))
            {
                activeRenameViewModel = null;
                activeRenameTextBox = null;
            }
        }
    }

    private static bool IsDescendantOf(DependencyObject candidate, DependencyObject ancestor)
    {
        var current = candidate;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static string ApplyDisplaySpacing(string value) =>
        string.Join('\u2009', value.Select(character => character == ' ' ? "\u2009 \u2009" : character.ToString()));
}
