using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PosSystem.Helpers;

/// <summary>
/// Attached property that restricts a TextBox to numeric input only.
/// Allows digits and at most one decimal separator (dot or comma).
/// </summary>
public static class NumericTextBox
{
    public static readonly DependencyProperty IsNumericProperty =
        DependencyProperty.RegisterAttached(
            "IsNumeric",
            typeof(bool),
            typeof(NumericTextBox),
            new PropertyMetadata(false, OnIsNumericChanged));

    public static bool GetIsNumeric(DependencyObject obj) =>
        (bool)obj.GetValue(IsNumericProperty);

    public static void SetIsNumeric(DependencyObject obj, bool value) =>
        obj.SetValue(IsNumericProperty, value);

    private static void OnIsNumericChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;

        if ((bool)e.NewValue)
        {
            tb.PreviewTextInput += OnPreviewTextInput;
            DataObject.AddPastingHandler(tb, OnPaste);
        }
        else
        {
            tb.PreviewTextInput -= OnPreviewTextInput;
            DataObject.RemovePastingHandler(tb, OnPaste);
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var tb = (TextBox)sender;
        e.Handled = !IsValid(BuildProspectiveText(tb, e.Text));
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string)))
        { e.CancelCommand(); return; }

        var pasted = (string)e.DataObject.GetData(typeof(string));
        var tb = (TextBox)sender;
        if (!IsValid(BuildProspectiveText(tb, pasted)))
            e.CancelCommand();
    }

    private static string BuildProspectiveText(TextBox tb, string input)
    {
        var text = tb.Text;
        if (tb.SelectionLength > 0)
            text = text.Remove(tb.SelectionStart, tb.SelectionLength);
        return text.Insert(tb.SelectionStart, input);
    }

    // Empty OR digits-only OR digits with exactly one separator (. or ,) followed by optional digits
    private static readonly Regex ValidPattern =
        new(@"^[\d]*([.,][\d]*)?$", RegexOptions.Compiled);

    private static bool IsValid(string text) =>
        string.IsNullOrEmpty(text) || ValidPattern.IsMatch(text);
}
