using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NavigatorEditor.ViewModels;

namespace NavigatorEditor.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // 不再自动初始化或连接，由用户手动连接
            this.Closed += async (_, __) =>
            {
                if (DataContext is MainWindowViewModel vm)
                    await vm.DisposeAsync();
            };
        }
    }
}
