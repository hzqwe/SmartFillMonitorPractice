using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SmartFillMonitorPractice.ViewModels;

namespace SmartFillMonitorPractice.Views
{
    public partial class SimulationView : UserControl
    {
        public SimulationView()
        {
            InitializeComponent();

            var app = Application.Current as App;
            if (app?.ServiceProvider != null)
            {
                DataContext = app.ServiceProvider.GetRequiredService<SimulationViewModel>();
            }
        }
    }
}
