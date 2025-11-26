using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using NLayer;

namespace SiMP3.Services
{
    /// <summary>
    /// Reads audio samples from disk and raises spectrum frames for visualization.
    /// </summary>
    internal sealed class AudioVisualizationSampler : IDisposable
    {
        private readonly string _path;
        private CancellationTokenSource? _cts;
        private bool _isPaused;

        public event Action<float[]>? SpectrumAvailable;

        public AudioVisualizationSampler(string path)
        {
            _path = path;
        }

        public void Start()
        {
            DisposeSampler();
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => PumpAsync(_cts.Token));
        }

        public void Pause(bool isPaused)
        {
            _isPaused = isPaused;
        }

        private async Task PumpAsync(CancellationToken token)
        {
            try
            {
                using var fileStream = File.OpenRead(_path);
                using var mpeg = new MpegFile(fileStream);

                var buffer = new float[2048 * mpeg.Channels];
                while (!token.IsCancellationRequested)
                {
                    if (_isPaused)
                    {
                        await Task.Delay(50, token);
                        continue;
                    }

                    int read = mpeg.ReadSamples(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        mpeg.Position = 0;
                        continue;
                    }

                    int samplesPerChannel = read / mpeg.Channels;
                    var mono = new float[samplesPerChannel];
                    for (int i = 0; i < samplesPerChannel; i++)
                    {
                        float sum = 0f;
                        for (int c = 0; c < mpeg.Channels; c++)
                            sum += buffer[i * mpeg.Channels + c];
                        mono[i] = sum / mpeg.Channels;
                    }

                    var spectrum = CalculateSpectrum(mono);
                    SpectrumAvailable?.Invoke(spectrum);

                    await Task.Delay(30, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Ignore decoding errors, visualization is non-critical
            }
        }

        private static float[] CalculateSpectrum(float[] samples)
        {
            int fftSize = 1024;
            if (samples.Length < fftSize)
            {
                Array.Resize(ref samples, fftSize);
            }

            var complex = new Complex[fftSize];
            var windowed = ApplyHannWindow(samples, fftSize);
            for (int i = 0; i < fftSize; i++)
                complex[i] = new Complex(windowed[i], 0);

            FFT(complex);

            int half = fftSize / 2;
            var magnitudes = new float[half];
            for (int i = 0; i < half; i++)
            {
                magnitudes[i] = (float)(complex[i].Magnitude);
            }

            Normalize(magnitudes);
            return magnitudes;
        }

        private static float[] ApplyHannWindow(float[] samples, int size)
        {
            var windowed = new float[size];
            for (int i = 0; i < size; i++)
            {
                float w = 0.5f * (1f - (float)Math.Cos(2 * Math.PI * i / (size - 1)));
                windowed[i] = i < samples.Length ? samples[i] * w : 0;
            }

            return windowed;
        }

        private static void Normalize(IList<float> values)
        {
            float max = 0f;
            for (int i = 0; i < values.Count; i++)
                max = Math.Max(max, values[i]);

            float scale = max <= 0 ? 1f : 1f / max;
            for (int i = 0; i < values.Count; i++)
                values[i] = Math.Clamp(values[i] * scale, 0f, 1.5f);
        }

        private static void FFT(Complex[] buffer)
        {
            int n = buffer.Length;
            int bits = (int)Math.Log(n, 2);

            for (int j = 1, i = 0; j < n; j++)
            {
                int bit = n >> 1;
                for (; (i & bit) != 0; bit >>= 1)
                    i &= ~bit;
                i |= bit;

                if (j < i)
                    (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2 * Math.PI / len;
                var wlen = new Complex(Math.Cos(angle), Math.Sin(angle));
                for (int i = 0; i < n; i += len)
                {
                    var w = Complex.One;
                    for (int j = 0; j < len / 2; j++)
                    {
                        var u = buffer[i + j];
                        var v = buffer[i + j + len / 2] * w;
                        buffer[i + j] = u + v;
                        buffer[i + j + len / 2] = u - v;
                        w *= wlen;
                    }
                }
            }
        }

        private void DisposeSampler()
        {
            if (_cts != null)
            {
                try
                {
                    _cts.Cancel();
                    _cts.Dispose();
                }
                catch
                {
                }

                _cts = null;
            }
        }

        public void Dispose()
        {
            DisposeSampler();
        }
    }
}