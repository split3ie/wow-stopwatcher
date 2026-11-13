using WowStopwatcher.App.Shell;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Tests;

public sealed class PromptDialogViewModelTests
{
    private static PromptDialogRequest ThreeFields(Func<string, string, string, Task<string?>>? validateAll = null) =>
        new("Title", "OK")
        {
            First = new PromptDialogField("a", Masked: true, Mono: true),
            Second = new PromptDialogField("b", Masked: true, Mono: true),
            Third = new PromptDialogField("c", Masked: true, Mono: true),
            ValidateAll = validateAll,
        };

    [Fact]
    public void ShowAsync_configures_second_and_third_fields()
    {
        var vm = new PromptDialogViewModel();

        _ = vm.ShowAsync(ThreeFields());

        Assert.True(vm.HasThird);
        Assert.Equal('●', vm.SecondMaskChar);
        Assert.Equal('●', vm.ThirdMaskChar);
        Assert.True(vm.IsSecondMono);
        Assert.True(vm.IsThirdMono);
    }

    [Fact]
    public async Task Confirm_passes_the_third_value_to_ValidateAll()
    {
        string? received = null;
        var vm = new PromptDialogViewModel();
        var task = vm.ShowAsync(ThreeFields((_, _, third) =>
        {
            received = third;
            return Task.FromResult<string?>(null);
        }));

        vm.First = "1";
        vm.Second = "2";
        vm.Third = "3";
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("3", received);
        Assert.True(await task);
    }
}
