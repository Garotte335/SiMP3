using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace SiMP3.Models
{
    public class TrackModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propName = "")
        {
            if (Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        public string Path { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string Year { get; set; } = string.Empty;
        public uint TrackNumber { get; set; }
        public TimeSpan Duration { get; set; }
        public string DurationString { get; set; } = "0:00";
        public ImageSource Cover { get; set; } = "default_cover.png";
        public DateTime DateAdded { get; set; } = DateTime.UtcNow;

        private bool _isCurrent;
        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetProperty(ref _isCurrent, value);
        }
    }
}