using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>One row of the placeholders table: alias, source (static or script), value, result and the hide-with-amounts switch.</summary>
public sealed partial class PlaceholderRowViewModel : ObservableObject
{
    private readonly PlaceholdersViewModel _owner;
    private string _resultRaw = string.Empty;

    internal PlaceholderRowViewModel(PlaceholdersViewModel owner, PlaceholderDefinition definition)
    {
        _owner = owner;
        Id = definition.Id;
        _alias = definition.Alias;
        _kind = definition.Kind;
        _value = definition.Value;
        _hidden = definition.Hidden;
    }

    /// <summary>The database id; 0 for a row that was never saved.</summary>
    internal long Id { get; }

    [ObservableProperty]
    private string _alias;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatic))]
    [NotifyPropertyChangedFor(nameof(IsScript))]
    [NotifyPropertyChangedFor(nameof(IsAutomatic))]
    [NotifyPropertyChangedFor(nameof(KindIndex))]
    [NotifyPropertyChangedFor(nameof(IsAliasEditable))]
    [NotifyPropertyChangedFor(nameof(ResultText))]
    private PlaceholderKind _kind;

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultText))]
    private bool _hidden;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAliasError))]
    private string _aliasError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultText))]
    private bool _resultIsError;

    public bool IsStatic => Kind == PlaceholderKind.Static;

    public bool IsScript => Kind == PlaceholderKind.Script;

    public bool IsAutomatic => Kind == PlaceholderKind.Automatic;

    /// <summary>True when the alias is a built-in name that is not a per-line marker, so the row can use the built-in value.</summary>
    public bool CanBeAutomatic => IsInvoiceBuiltin(Alias);

    /// <summary>The alias of an automatic row is fixed.</summary>
    public bool IsAliasEditable => !IsAutomatic;

    /// <summary>True when the result is a money value that stays masked while amounts are hidden.</summary>
    private bool IsMoney => InvoiceBuiltins.Canonical(Alias.Trim()) is { } name && InvoiceBuiltins.MoneyNames.Contains(name);

    public bool HasAliasError => AliasError.Length > 0;

    /// <summary>0 for static text, 1 for a script, 2 for automatic (the index in the source list).</summary>
    public int KindIndex
    {
        get => (int)Kind;
        set
        {
            // The list can send -1 while an item is hidden or the selection is being replaced.
            if (value is >= 0 and <= 2)
            {
                Kind = (PlaceholderKind)value;
            }
        }
    }

    /// <summary>The result, or the error text; the mask when the row is hidden with amounts and amounts are hidden.</summary>
    public string ResultText => !ResultIsError && (Hidden || IsMoney) && _owner.AmountsHidden ? MoneyMask.Mask : _resultRaw;

    internal void SetResult(string text, bool isError)
    {
        _resultRaw = text;
        ResultIsError = isError;
        OnPropertyChanged(nameof(ResultText));
    }

    internal void RefreshMask() => OnPropertyChanged(nameof(ResultText));

    internal PlaceholderDefinition ToDefinition(string companyId, int order) =>
        new(Id, companyId, InvoiceBuiltins.Canonical(Alias.Trim()) ?? Alias.Trim(), Kind, IsAutomatic ? string.Empty : Value, Hidden, order);

    /// <summary>The alias with a built-in name in its canonical spelling.</summary>
    internal string NormalizedAlias => InvoiceBuiltins.Canonical(Alias.Trim()) ?? Alias.Trim();

    internal static bool IsInvoiceBuiltin(string alias) =>
        InvoiceBuiltins.Canonical(alias.Trim()) is { } name && !InvoiceBuiltins.IsLineMarker(name);

    partial void OnAliasChanged(string value)
    {
        OnPropertyChanged(nameof(CanBeAutomatic));
        OnPropertyChanged(nameof(ResultText));
        if (IsAutomatic && !CanBeAutomatic)
        {
            Kind = PlaceholderKind.Static;
        }

        _owner.RowEdited();
    }

    partial void OnKindChanged(PlaceholderKind oldValue, PlaceholderKind newValue)
    {
        if (oldValue == PlaceholderKind.Automatic && newValue == PlaceholderKind.Static && Value.Length == 0 && !ResultIsError)
        {
            // Start from the value the built-in has now.
            Value = _resultRaw;
        }

        _owner.RowEdited();
    }

    partial void OnValueChanged(string value) => _owner.RowEdited();

    partial void OnHiddenChanged(bool value) => _owner.RowEdited();

    [RelayCommand]
    private Task RunAsync() => _owner.RunAsync();

    [RelayCommand]
    private void Delete() => _owner.Remove(this);
}
