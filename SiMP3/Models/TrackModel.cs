using System;
using Microsoft.Maui.Controls;

namespace SiMP3.Models
{
    /// <summary>
    /// Модель одного аудіотреку для прив'язки до UI.
    /// </summary>
    public class TrackModel
    {
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

        /// <summary>
        /// Дата/час, коли трек був доданий у бібліотеку (для сортування).
        /// Зберігаємо в UTC.
        /// </summary>
        public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    }
}
