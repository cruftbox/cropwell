using System.Windows;
using System.Windows.Controls;
using Cropwell.Services;

namespace Cropwell.Dialogs;

public partial class ResizeDialog : Window
{
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private bool _updating;
    public int ResultWidth { get; private set; }
    public int ResultHeight { get; private set; }

    public ResizeDialog(int width, int height)
    {
        InitializeComponent();
        _sourceWidth = width; _sourceHeight = height;
        WidthBox.Text = width.ToString(); HeightBox.Text = height.ToString();
        WidthBox.SelectAll(); WidthBox.Focus();
    }

    private void WidthChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || !TryDimension(WidthBox.Text, out var width)) return;
        _updating = true;
        HeightBox.Text = ImageTransforms.AspectSize(_sourceWidth, _sourceHeight, width, null).Height.ToString();
        _updating = false; ErrorText.Text = "";
    }
    private void HeightChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || !TryDimension(HeightBox.Text, out var height)) return;
        _updating = true;
        WidthBox.Text = ImageTransforms.AspectSize(_sourceWidth, _sourceHeight, null, height).Width.ToString();
        _updating = false; ErrorText.Text = "";
    }
    private void Resize_Click(object sender, RoutedEventArgs e)
    {
        if (!TryDimension(WidthBox.Text, out var width) || !TryDimension(HeightBox.Text, out var height))
        { ErrorText.Text = "Enter whole-pixel dimensions from 1 to 16384."; return; }
        ResultWidth = width; ResultHeight = height; DialogResult = true;
    }
    private static bool TryDimension(string text, out int value) => int.TryParse(text, out value) && value is >= 1 and <= 16384;
}
