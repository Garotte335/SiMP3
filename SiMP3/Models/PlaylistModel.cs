using System.Collections.ObjectModel;
using SiMP3.Models;

namespace SiMP3.Models
{
    public class PlaylistModel
    {
        public string Name { get; set; } = string.Empty;

        public ObservableCollection<TrackModel> Tracks { get; set; } = new();
    }
}