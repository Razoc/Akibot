using AkiBot.App.ViewModels;
using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace AkiBot.App.Views
{
    public partial class AdvancedSettingsView : UserControl
    {
        public AdvancedSettingsView()
        {
            // Built before the controls, so every binding sees this page's view model at once.
            if (MainWindowViewModel.Current is MainWindowViewModel main)
            {
                DataContext = new AdvancedSettingsViewModel(main);
            }
            InitializeComponent();
        }
    }
}