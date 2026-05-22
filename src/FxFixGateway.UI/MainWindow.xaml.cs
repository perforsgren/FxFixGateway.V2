using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using FxFixGateway.Domain.Enums;
using FxFixGateway.Domain.Interfaces;
using FxFixGateway.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FxFixGateway.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _shutdownCompleted;

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
