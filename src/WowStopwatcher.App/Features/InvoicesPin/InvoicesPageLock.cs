using WowStopwatcher.App.Navigation;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.InvoicesPin;

/// <summary>Locks the Invoices page while the Invoices PIN is active and the page is locked.</summary>
public sealed class InvoicesPageLock : IPageLock
{
    private readonly InvoicesPinService _service;
    private readonly InvoicesLockViewModel _content;

    public InvoicesPageLock(InvoicesPinService service, InvoicesLockViewModel content)
    {
        _service = service;
        _content = content;
        service.Changed += OnChanged;
        service.LockState.Changed += OnChanged;
        content.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InvoicesLockViewModel.IsLocked))
            {
                OnChanged(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? Changed;

    public bool IsLocked(string pageKey) => pageKey == PageKeys.Invoicing && _content.IsLocked;

    public object? GetLockContent(string pageKey) => pageKey == PageKeys.Invoicing ? _content : null;

    public void RecordActivity(string pageKey)
    {
        if (pageKey == PageKeys.Invoicing)
        {
            _content.RecordActivity();
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
