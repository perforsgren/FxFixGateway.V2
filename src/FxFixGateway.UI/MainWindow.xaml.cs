using System.Windows;
using FxFixGateway.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FxFixGateway.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // DataContext sätts av DI när fönstret skapas
            // (se App.xaml.cs startup)
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Initiera ViewModel när fönstret laddats
            if (DataContext is MainViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        }
    }
}
