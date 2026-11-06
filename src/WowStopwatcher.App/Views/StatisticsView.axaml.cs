using Avalonia;
using Avalonia.Controls;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Views;

public partial class StatisticsView : UserControl
{
    public StatisticsView() => InitializeComponent();

    // The view model refreshes itself only while this view is on screen.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as StatisticsViewModel)?.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as StatisticsViewModel)?.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
