using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinLight.App.ViewModels;

internal sealed class ResultTemplateSelector(DataTemplate appTemplate, DataTemplate calculatorTemplate)
    : DataTemplateSelector
{
    protected override DataTemplate SelectTemplateCore(object item) =>
        item is AppResultViewModel { IsCalculator: true } ? calculatorTemplate : appTemplate;

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
