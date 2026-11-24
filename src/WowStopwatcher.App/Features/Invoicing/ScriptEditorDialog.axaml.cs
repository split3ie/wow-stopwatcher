using Avalonia.Controls;
using Avalonia.Input;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Modal popup with a large editor for one placeholder script.</summary>
public partial class ScriptEditorDialog : Window
{
    public ScriptEditorDialog() => InitializeComponent();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }
}
