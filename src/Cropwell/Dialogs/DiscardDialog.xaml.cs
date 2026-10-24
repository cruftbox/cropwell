using System.Windows;

namespace Cropwell.Dialogs;

public enum DiscardChoice { Export, Discard }
public partial class DiscardDialog : Window
{
    public DiscardChoice Choice { get; private set; }
    public DiscardDialog() => InitializeComponent();
    private void Export_Click(object sender, RoutedEventArgs e) { Choice = DiscardChoice.Export; DialogResult = true; }
    private void Discard_Click(object sender, RoutedEventArgs e) { Choice = DiscardChoice.Discard; DialogResult = true; }
}
