using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MdViewer.App.Dialogs;

/// <summary>Base de las ventanas auxiliares: icono, fuente, centrado y Esc para cerrar.</summary>
public class DialogBase : Window
{
    protected DialogBase(Window? owner, string title, double width, double height)
    {
        Owner = owner;
        Title = title;
        Width = width;
        Height = height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Icon = owner?.Icon;
        FontSize = 13;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["DocumentBackgroundBrush"];
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResultSafe(false);
                Close();
            }
        };
    }

    protected void DialogResultSafe(bool value)
    {
        try
        {
            DialogResult = value;
        }
        catch (InvalidOperationException)
        {
            // La ventana no se abrió como diálogo modal.
        }
    }

    protected static TextBlock Label(string text, double top = 0) => new() { Text = text, Margin = new Thickness(0, top, 0, 4) };

    protected static StackPanel Buttons(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var b in buttons)
        {
            b.MinWidth = 90;
            b.Margin = new Thickness(8, 0, 0, 0);
            b.Padding = new Thickness(12, 4, 12, 4);
            panel.Children.Add(b);
        }

        return panel;
    }
}
