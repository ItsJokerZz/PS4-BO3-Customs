using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FFPorter.Desktop.Models;

namespace FFPorter.Desktop.Controls;

public partial class FidelityPanel : UserControl
{
    private static readonly string[] Grades = ["exact", "strong", "good", "approximate", "weak"];

    public FidelityPanel()
    {
        InitializeComponent();
        Legend.ItemsSource = Grades;
        EmptyLegend.ItemsSource = Grades;
        StateDot.SetBinding(TagProperty, new Binding(nameof(FidelityView.State)) { Converter = new StateToDot() });
        DataContextChanged += (_, _) => Refresh();
        Refresh();
    }

    public string EmptyMessage
    {
        get => EmptyText.Text;
        set => EmptyText.Text = value;
    }

    private void Refresh()
    {
        bool hasReport = DataContext is FidelityView;
        Report.Visibility = hasReport ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasReport ? Visibility.Collapsed : Visibility.Visible;
    }

    private sealed class StateToDot : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value switch
        {
            "running" => RunStates.Running,
            "done" => RunStates.Done,
            "cancelled" => RunStates.Cancelled,
            _ => RunStates.Failed,
        };

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
