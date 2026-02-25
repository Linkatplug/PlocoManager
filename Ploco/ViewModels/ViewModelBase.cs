using CommunityToolkit.Mvvm.ComponentModel;

namespace Ploco.ViewModels
{
    /// <summary>
    /// Base class for all ViewModels in the application.
    /// Inherits from ObservableObject to provide INotifyPropertyChanged implementation.
    /// </summary>
    public abstract class ViewModelBase : ObservableObject
    {
    }
}
