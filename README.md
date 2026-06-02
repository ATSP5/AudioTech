# AudioAnalyser

A real-time audio analysis and visualization tool built with Avalonia UI and NAudio, supporting multi-channel capture, FFT/waterfall spectrograms, parametric EQ, noise filtering, and data export.

---

## Features

### Audio Input

**Microphone Capture**
- Up to 4 simultaneous channels, each with independent device selection
- WASAPI (Windows Audio Session API) with WMME fallback
- Configurable sample rates: 22050 Hz, 44100 Hz, 48000 Hz
- Configurable FFT sizes: 512, 1024, 2048, 4096, 8192 bins
- Per-channel gain adjustment (dB), applied before filtering
- Per-channel passthrough monitoring with master toggle

**Audio File Playback**
- Supported formats: WAV, MP3, FLAC, AIFF, OGG
- Real-time FFT analysis during playback
- Seek/scrub via progress slider with live position display
- Live filter and EQ switching during playback

**Recording**
- Record from Channel 0 microphone input
- Save to WAV or MP3 after stopping
- Immediate playback of recorded audio

---

### Visualization

**FFT Spectrum Graph**
- Real-time frequency spectrum per channel with distinct colors (red, gold, cyan, green)
- Logarithmic frequency axis: 20 Hz – 20 kHz (configurable)
- Logarithmic dB magnitude axis: −120 to 0 dB (configurable)
- Phase difference display (Ch2 − Ch1) on right axis in radians with exponential moving average smoothing
- Dominant frequency marker with labeled vertical line
- Frequency grid lines: 50, 100, 200, 500 Hz, 1k, 2k, 5k, 10k, 20k Hz
- dB grid lines: −120, −100, −80, −60, −40, −20, 0 dB
- Legend box with per-channel color codes and phase indicator

**Waterfall Spectrogram**
- Rolling time–frequency history with downward scroll
- Plasma-inspired colormap (dark purple → magenta → white)
- 1024 × 512 bitmap resolution with frequency-based vertical scaling
- Configurable frequency and dB ranges, independent reset control

---

### Signal Processing

**Time-Domain Filters** (strength 0–100%)
- Low-Pass Filter — single-pole IIR smoothing
- High-Pass Filter — rumble / low-frequency removal
- Noise Gate — hard gate with attack/release envelope
- Moving Average — temporal smoothing

**Spectral Filters** (applied in frequency domain, 0–60 dB)
- White Noise Subtraction — flat noise floor removal
- Purple Noise Subtraction — +6 dB/octave characteristic noise removal
- Brown Noise Subtraction — −6 dB/octave characteristic noise removal

**Parametric Equalizer**
- 12-band EQ with center frequencies: 10, 30, 50, 75, 100, 125 Hz, 1k, 2k, 4k, 10k, 16k, 20k Hz
- Per-band gain and Q factor, rebuilt live as BiQuad coefficients
- L/R balance control: −20 to +20 dB
- Reset to flat (0 dB all bands) button

---

### Export

| Output | Formats |
|--------|---------|
| FFT graph image | PNG, JPEG, BMP |
| Waterfall image | PNG, JPEG, BMP |
| FFT spectrum data | CSV (Frequency Hz, Magnitude dB per channel, phase difference) |
| Waterfall spectrum data | CSV (Frequency Hz, Magnitude dB) |

CSV exports honor the configured min/max frequency display range and use full floating-point precision.

---

### Display Configuration

- dB range: adjustable min/max within −120 to 0 dB
- Frequency range: adjustable min/max within 20 Hz to 20 kHz
- Per-channel enable/disable toggle with color-coded labels
- Live magnitude readout per channel in the channel list

---

## Technology Stack

| Component | Library / Version |
|-----------|------------------|
| UI framework | Avalonia UI |
| Audio I/O | NAudio 2.2.1 (WASAPI, WMME, MediaFoundation) |
| Image encoding | SkiaSharp 2.88.7 |
| MVVM | CommunityToolkit.MVVM 8.2.0 |
| Runtime | .NET 8.0, AnyCPU / x64 |
| Architecture | Clean Architecture + DDD + CQRS |

---

## Getting Started

### Prerequisites

- Windows 10/11 (WASAPI required for low-latency capture)
- .NET 8.0 SDK
- A microphone or audio interface for live capture

### Build & Run

```bash
git clone https://github.com/AdamPrukala/AudioAnalyser.git
cd AudioAnalyser
dotnet build
dotnet run --project src/AudioAnalyser
```

---

## Usage

1. **Settings** — select capture devices, sample rate, FFT size, and enable the desired channels.
2. **Start** — click *Start Capture* to begin real-time analysis. The FFT and waterfall graphs update live.
3. **Filters** — choose a time-domain or spectral filter and adjust its strength slider.
4. **Equalizer** — open the EQ panel and drag the 12-band sliders to shape the signal.
5. **Playback** — use *Browse* to load an audio file, then *Play* to analyse it through the same pipeline.
6. **Record** — click *Record* to capture from the microphone; on stop, choose to save as WAV or MP3.
7. **Export** — use the export buttons to save the FFT or waterfall as an image or CSV file.
