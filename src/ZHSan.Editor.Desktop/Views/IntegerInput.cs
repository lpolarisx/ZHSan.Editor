using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ZHSan.Editor.Desktop.Views;

/// <summary>
/// Restricts a <see cref="NumericUpDown"/> to whole numbers: only digits and a leading minus
/// can be typed, text that is not an integer is marked while editing, and it is reverted to
/// the current value when the box loses focus or Enter is pressed.
/// </summary>
public static class IntegerInput
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, bool>("IsEnabled", typeof(IntegerInput));

    private const string InvalidClass = "invalid-input";

    static IntegerInput()
    {
        IsEnabledProperty.Changed.AddClassHandler<NumericUpDown>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(NumericUpDown element) => element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(NumericUpDown element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(NumericUpDown box, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            box.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
            box.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            box.LostFocus -= OnLostFocus;
            box.PropertyChanged -= OnPropertyChanged;
            return;
        }

        box.Minimum = Math.Max(box.Minimum, int.MinValue);
        box.Maximum = Math.Min(box.Maximum, int.MaxValue);
        box.FormatString = "0";
        box.ParsingNumberStyle = NumberStyles.Integer;
        box.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        box.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        box.LostFocus += OnLostFocus;
        box.PropertyChanged += OnPropertyChanged;
    }

    public static bool IsInteger(string? text) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Text is { Length: > 0 } text && !text.All(c => char.IsAsciiDigit(c) || c == '-'))
        {
            e.Handled = true;
        }
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is NumericUpDown box)
        {
            Revert(box);
            // Boxes bound with UpdateSourceTrigger=LostFocus also commit on Enter.
            BindingOperations.GetBindingExpressionBase(box, NumericUpDown.ValueProperty)?.UpdateSource();
        }
    }

    private static void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is NumericUpDown box)
        {
            Revert(box);
        }
    }

    private static void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == NumericUpDown.TextProperty && sender is NumericUpDown box)
        {
            box.Classes.Set(InvalidClass, !IsInteger(box.Text));
        }
    }

    private static void Revert(NumericUpDown box)
    {
        // Valid text is left for the control to commit; only text it cannot parse is reverted.
        if (!IsInteger(box.Text))
        {
            // Clearing the text may already have nulled Value, so reload it from the bound record first.
            BindingOperations.GetBindingExpressionBase(box, NumericUpDown.ValueProperty)?.UpdateTarget();
            box.Text = box.Value is { } value ? value.ToString("0", CultureInfo.InvariantCulture) : string.Empty;
        }

        box.Classes.Set(InvalidClass, false);
    }
}
