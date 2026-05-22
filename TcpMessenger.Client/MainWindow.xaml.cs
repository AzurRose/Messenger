using System.Windows;
using TcpMessenger.Client.ViewModels;

namespace TcpMessenger.Client;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
