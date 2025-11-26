using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Graphics;

namespace SiMP3.Views
{
    /// <summary>
    /// Reusable cross-platform audio visualization surface.
    /// </summary>
    public class AudioVisualizationView : ContentView, IDisposable
    {
        private readonly GraphicsView _graphicsView;
        private readonly SpectrumDrawable _drawable;
        private IDispatcherTimer? _timer;
        private bool _isRunning;
        private DateTime _lastTick;

        public AudioVisualizationView()
        {
            _drawable = new SpectrumDrawable();
            _graphicsView = new GraphicsView
            {
                Drawable = _drawable,
                HeightRequest = 410,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            Content = _graphicsView;
        }

        /// <summary>
        /// Start continuous drawing/invalidating loop.
        /// </summary>
        public void Start()
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(33);
            _lastTick = DateTime.UtcNow;
            _timer.Tick += (_, _) =>
            {
                var now = DateTime.UtcNow;
                var delta = (float)(now - _lastTick).TotalSeconds;
                _lastTick = now;
                _drawable.Advance(delta);
                _graphicsView.Invalidate();
            };
            _timer.Start();
        }

        /// <summary>
        /// Stop the rendering loop.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            if (_timer != null)
            {
                _timer.Stop();
                _timer = null;
            }
        }

        /// <summary>
        /// Updates the spectrum data for the next frame.
        /// </summary>
        public void UpdateSpectrum(float[] data)
        {
            if (data == null || data.Length == 0)
                return;

            _drawable.SetSpectrum(data);
        }

        public void Dispose()
        {
            Stop();
        }

        private class SpectrumDrawable : IDrawable
        {
            private readonly object _dataLock = new();
            private readonly float[] _displayBins = new float[96];
            private readonly float[] _decay = new float[96];
            private readonly Random _random = new();
            private float _time;
            private float _pulse;

            public void SetSpectrum(IReadOnlyList<float> data)
            {
                var delta = 1f / 30f;

                var bins = CollapseToBins(data, _displayBins.Length);

                lock (_dataLock)
                {
                    for (int i = 0; i < _displayBins.Length; i++)
                    {
                        float target = bins[i];
                        // Deeper decay to mimic Alchemy trailing bars
                        _decay[i] = Math.Max(_decay[i] - delta * 1.8f, 0f);
                        float blended = Math.Max(target, _decay[i]);
                        _decay[i] = blended;
                        _displayBins[i] = 0.78f * _displayBins[i] + 0.22f * blended;
                    }
                }
            }

            public void Advance(float delta)
            {
                _time += delta;
                // subtle breathing pulse for rings
                _pulse = 0.92f + 0.08f * (float)Math.Sin(_time * 2.2f);
            }

            public void Draw(ICanvas canvas, RectF dirtyRect)
            {
                canvas.SaveState();
                DrawBackdrop(canvas, dirtyRect);

                float width = dirtyRect.Width;
                float height = dirtyRect.Height;
                float barWidth = width / _displayBins.Length;

                float[] snapshot;
                lock (_dataLock)
                {
                    snapshot = _displayBins.ToArray();
                }

                if (snapshot.All(v => v <= 0))
                {
                    // idle animation when no data yet
                    for (int i = 0; i < snapshot.Length; i++)
                    {
                        snapshot[i] = 0.04f + (float)(_random.NextDouble() * 0.03f * Math.Sin(_time * 1.2f + i * 0.6f));
                    }
                }

                DrawWaves(canvas, dirtyRect, snapshot);
                DrawBars(canvas, dirtyRect, snapshot, barWidth);
                DrawPulse(canvas, dirtyRect, snapshot);

                canvas.RestoreState();
            }

            private void DrawBackdrop(ICanvas canvas, RectF rect)
            {
                var center = new PointF(rect.Center.X, rect.Center.Y);

                // Alchemy-like swirling gradient
                var gradient = new RadialGradientPaint
                {
                    Center = center,
                    Radius = Math.Max(rect.Width, rect.Height),
                    GradientStops = new[]
                    {
                        new PaintGradientStop(0f, Color.FromArgb("#0B0F2A")),
                        new PaintGradientStop(0.4f, Color.FromArgb("#101326")),
                        new PaintGradientStop(1f, Color.FromArgb("#080910"))
                    }
                };
                canvas.SetFillPaint(gradient, rect);
                canvas.FillRectangle(rect);

                float orbit = 18f + 10f * (float)Math.Sin(_time * 0.9f);
                var ringColor = Color.FromRgba(0.42f, 0.82f, 0.92f, 0.18f);
                canvas.StrokeColor = ringColor;
                canvas.StrokeSize = 2f;
                canvas.DrawEllipse(center.X - rect.Width * 0.38f, center.Y - rect.Width * 0.38f, rect.Width * 0.76f, rect.Width * 0.76f);
                canvas.StrokeColor = Color.FromRgba(0.61f, 0.46f, 0.93f, 0.12f);
                canvas.DrawEllipse(center.X - rect.Width * 0.28f + orbit, center.Y - rect.Width * 0.28f, rect.Width * 0.56f, rect.Width * 0.56f);
            }

            private void DrawWaves(ICanvas canvas, RectF rect, IReadOnlyList<float> bins)
            {
                int waves = 3;
                float baseAmp = rect.Height * 0.18f;
                float freq = 0.8f;

                for (int w = 0; w < waves; w++)
                {
                    var path = new PathF();
                    float phase = _time * (1.2f + w * 0.25f) + w;
                    float yCenter = rect.Center.Y + (w - 1) * baseAmp * 0.2f;

                    for (int i = 0; i < bins.Count; i++)
                    {
                        float t = i / (float)(bins.Count - 1);
                        float amplitude = baseAmp * (0.4f + bins[i] * 0.8f);
                        float y = yCenter + (float)Math.Sin((t * 6.0 + phase) * freq) * amplitude;
                        float x = rect.X + t * rect.Width;

                        if (i == 0)
                            path.MoveTo(x, y);
                        else
                            path.LineTo(x, y);
                    }

                    canvas.StrokeSize = 2f;
                    var waveColor = Color.FromRgba(0.35f + w * 0.1f, 0.65f, 0.98f - w * 0.1f, 0.45f);
                    canvas.StrokeColor = waveColor;
                    canvas.DrawPath(path);
                }
            }

            private void DrawBars(ICanvas canvas, RectF rect, IReadOnlyList<float> snapshot, float barWidth)
            {
                var startColor = Color.FromArgb("#70D7FF");
                var midColor = Color.FromArgb("#8B6CFF");
                var endColor = Color.FromArgb("#32E0A1");

                for (int i = 0; i < snapshot.Count; i++)
                {
                    float magnitude = snapshot[i];
                    float barHeight = Math.Clamp(magnitude, 0f, 1.25f) * rect.Height;

                    var gradient = new LinearGradientPaint
                    {
                        StartPoint = new PointF(0, rect.Bottom),
                        EndPoint = new PointF(0, rect.Top),
                        GradientStops = new[]
                        {
                            new PaintGradientStop(0f, startColor),
                            new PaintGradientStop(0.45f, midColor),
                            new PaintGradientStop(1f, endColor)
                        }
                    };

                    canvas.SetFillPaint(gradient, rect);

                    float x = i * barWidth + barWidth * 0.2f;
                    float y = rect.Height - barHeight;
                    float bw = barWidth * 0.55f;
                    float radius = 7f;
                    canvas.FillRoundedRectangle(x, y, bw, barHeight + 2, radius);
                }
            }

            private void DrawPulse(ICanvas canvas, RectF rect, IReadOnlyList<float> snapshot)
            {
                float energy = snapshot.Count > 0 ? snapshot.Average() : 0.05f;
                float radius = (rect.Width * 0.12f + energy * rect.Width * 0.18f) * _pulse;
                var center = new PointF(rect.Center.X, rect.Center.Y);

                var glow = new RadialGradientPaint
                {
                    Center = center,
                    Radius = radius * 1.6f,
                    GradientStops = new[]
                    {
                        new PaintGradientStop(0f, Color.FromRgba(0.39f, 0.93f, 0.86f, 0.35f)),
                        new PaintGradientStop(0.6f, Color.FromRgba(0.49f, 0.61f, 0.94f, 0.18f)),
                        new PaintGradientStop(1f, Colors.Transparent)
                    }
                };
                canvas.SetFillPaint(glow, rect);
                canvas.FillCircle(center, radius * 1.4f);

                canvas.FillColor = Color.FromRgba(0.72f, 0.86f, 1f, 0.28f);
                canvas.FillCircle(center, radius * 0.6f);
                canvas.FillColor = Color.FromRgba(0.48f, 0.94f, 0.78f, 0.8f);
                canvas.FillCircle(center, radius * 0.4f);
            }

            private static float[] CollapseToBins(IReadOnlyList<float> data, int bins)
            {
                if (data == null || data.Count == 0)
                    return Enumerable.Repeat(0f, bins).ToArray();

                var result = new float[bins];
                int chunk = Math.Max(1, data.Count / bins);
                for (int i = 0; i < bins; i++)
                {
                    int start = i * chunk;
                    int end = Math.Min(start + chunk, data.Count);
                    float sum = 0f;
                    for (int j = start; j < end; j++)
                        sum += data[j];

                    float avg = sum / Math.Max(1, end - start);
                    result[i] = Math.Clamp(avg, 0f, 1.5f);
                }

                return result;
            }
        }
    }
}