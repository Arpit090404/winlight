using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinLight.App.ViewModels;

internal sealed class ResultTemplateSelector(DataTemplate appTemplate, DataTemplate calculatorTemplate,
    DataTemplate fileTemplate)
    : DataTemplateSelector
{
    protected override DataTemplate SelectTemplateCore(object item) =>
        item is AppResultViewModel { IsCalculator: true } ? calculatorTemplate :
        item is AppResultViewModel { IsFile: true } ? fileTemplate : appTemplate;

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
