using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Eario;

public sealed class AudioDeviceModel : INotifyPropertyChanged
{
    private float _volume;
    private bool _isDefault;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public float Volume { get => _volume; set { _volume = value; Changed(); } }
    public bool IsDefault { get => _isDefault; set { _isDefault = value; Changed(); } }
    public ObservableCollection<AppModel> Apps { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class AppModel
{
    public required int ProcessId { get; init; }
    public required string Name { get; init; }
    public required string ExecutablePath { get; init; }
    public required ImageSource Icon { get; init; }
}
