using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>A JavaScript editor for placeholder scripts. <see cref="Value"/> is bindable (TextEditor.Text is not) and highlighting comes from TextMate.</summary>
public sealed class ScriptEditor : TextEditor
{
    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<ScriptEditor, string>(nameof(Value), string.Empty, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private static readonly RegistryOptions Registry = new(ThemeName.LightPlus);

    private TextMate.Installation? _textMate;
    private bool _syncing;

    public ScriptEditor()
    {
        ShowLineNumbers = false;
        WordWrap = false;
        Options.ConvertTabsToSpaces = true;
        Options.IndentationSize = 2;
        TextChanged += OnEditorTextChanged;
    }

    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    // Reuse the stock TextEditor template and styles.
    protected override Type StyleKeyOverride => typeof(TextEditor);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _textMate = this.InstallTextMate(Registry);
        _textMate.SetGrammar(Registry.GetScopeByExtension(".js"));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _textMate?.Dispose();
        _textMate = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty && !_syncing)
        {
            var value = change.GetNewValue<string?>() ?? string.Empty;
            if (Text != value)
            {
                _syncing = true;
                Text = value;
                _syncing = false;
            }
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        Value = Text ?? string.Empty;
        _syncing = false;
    }
}
