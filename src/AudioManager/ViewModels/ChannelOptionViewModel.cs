using System.Linq;

namespace AudioManager.ViewModels;

public sealed class ChannelOptionViewModel : ObservableObject
{
    private string _name;

    public ChannelOptionViewModel(string id, string name)
    {
        Id = id;
        _name = name;
    }

    public string Id { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(NameSpaced));
            }
        }
    }

    public string NameSpaced => string.Join('\u2009', Name.Select(character => character == ' ' ? "\u2009 \u2009" : character.ToString()));
}
