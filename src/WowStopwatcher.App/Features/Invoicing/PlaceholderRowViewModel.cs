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
        _isScript = definition.Kind == PlaceholderKind.Script;
        _value = definition.Value;
        _hidden = definition.Hidden;
    }

    /// <summary>The database id; 0 for a row that was never saved.</summary>
    internal long Id { get; }

    [ObservableProperty]
    private string _alias;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatic))]
    [NotifyPropertyChangedFor(nameof(KindIndex))]
    private bool _isScript;

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

    public bool IsStatic => !IsScript;

    public bool HasAliasError => AliasError.Length > 0;

    /// <summary>0 for static text, 1 for a script (the index in the source list).</summary>
    public int KindIndex
    {
        get => IsScript ? 1 : 0;
        set => IsScript = value == 1;
    }

    /// <summary>The result, or the error text; the mask when the row is hidden with amounts and amounts are hidden.</summary>
    public string ResultText => !ResultIsError && Hidden && _owner.AmountsHidden ? MoneyMask.Mask : _resultRaw;

    internal void SetResult(string text, bool isError)
    {
        _resultRaw = text;
        ResultIsError = isError;
        OnPropertyChanged(nameof(ResultText));
    }

    internal void RefreshMask() => OnPropertyChanged(nameof(ResultText));

    internal PlaceholderDefinition ToDefinition(string companyId, int order) =>
        new(Id, companyId, Alias.Trim(), IsScript ? PlaceholderKind.Script : PlaceholderKind.Static, Value, Hidden, order);

    partial void OnAliasChanged(string value) => _owner.RowEdited();

    partial void OnIsScriptChanged(bool value) => _owner.RowEdited();

    partial void OnValueChanged(string value) => _owner.RowEdited();

    partial void OnHiddenChanged(bool value) => _owner.RowEdited();

    [RelayCommand]
    private Task RunAsync() => _owner.RunAsync();

    [RelayCommand]
    private void Delete() => _owner.Remove(this);
}
