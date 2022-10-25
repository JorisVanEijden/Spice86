﻿namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Sound;
using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;
using Spice86.Core.Emulator.VM;

using System;

/// <summary>
/// Virtual device which emulates OPL3 FM sound.
/// </summary>
public sealed class OPL3FM : DefaultIOPortHandler, IDisposable {
    private const byte Timer1Mask = 0xC0;
    private const byte Timer2Mask = 0xA0;

    private readonly AudioPlayer? _audioPlayer;
    private readonly FmSynthesizer? _synth;
    private int _currentAddress;
    private volatile bool _endThread;
    private readonly Thread _playbackThread;
    private bool _initialized;
    private bool _paused;
    private byte _statusByte;
    private byte _timer1Data;
    private byte _timer2Data;
    private byte _timerControlByte;

    private bool _disposed = false;

    public OPL3FM(Machine machine, Configuration configuration) : base(machine, configuration) {
        _audioPlayer = Audio.CreatePlayer(48000, 2048);
        if (_audioPlayer is not null) {
            _synth = new FmSynthesizer(_audioPlayer.Format.SampleRate);
        }
        _playbackThread = new Thread(GenerateWaveforms) {
            Name = "OPL3FMAudio"
        };
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(OPLConsts.FM_MUSIC_STATUS_PORT_NUMBER_2, this);
        ioPortDispatcher.AddIOPortHandler(OPLConsts.FM_MUSIC_DATA_PORT_NUMBER_2, this);
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if(!_disposed) {
            if(disposing) {
                if (!_paused) {
                    _endThread = true;
                    if (_playbackThread.IsAlive) {
                        _playbackThread.Join();
                    }
                }
                _audioPlayer?.Dispose();
                _initialized = false;
            }
            _disposed = true;
        }
    }

    public void Pause() {
        if (_initialized && !_paused && _playbackThread.IsAlive) {
            _endThread = true;
            _playbackThread.Join();
            _paused = true;
        }
    }

    public override byte ReadByte(int port) {
        if ((_timerControlByte & 0x01) != 0x00 && (_statusByte & Timer1Mask) == 0) {
            _timer1Data++;
            if (_timer1Data == 0) {
                _statusByte |= Timer1Mask;
            }
        }

        if ((_timerControlByte & 0x02) != 0x00 && (_statusByte & Timer2Mask) == 0) {
            _timer2Data++;
            if (_timer2Data == 0) {
                _statusByte |= Timer2Mask;
            }
        }

        return _statusByte;
    }

    public override ushort ReadWord(int port) {
        return _statusByte;
    }

    public void Resume() {
        if (_paused) {
            _endThread = false;
            StartPlaybackThread();
            _paused = false;
        }
    }

    public override void WriteByte(int port, byte value) {
        if (port == 0x388) {
            _currentAddress = value;
        } else if (port == 0x389) {
            if (_currentAddress == 0x02) {
                _timer1Data = value;
            } else if (_currentAddress == 0x03) {
                _timer2Data = value;
            } else if (_currentAddress == 0x04) {
                _timerControlByte = value;
                if ((value & 0x80) == 0x80) {
                    _statusByte = 0;
                }
            } else {
                if (!_initialized) {
                    StartPlaybackThread();
                }

                _synth?.SetRegisterValue(0, _currentAddress, value);
            }
        }
    }

    public override void WriteWord(int port, ushort value) {
        if (port == 0x388) {
            WriteByte(0x388, (byte)value);
            WriteByte(0x389, (byte)(value >> 8));
        }
    }

    /// <summary>
    /// Generates and plays back output waveform data.
    /// </summary>
    private void GenerateWaveforms() {
        if (_audioPlayer is null) {
            return;
        }
        int length = 1024;
        Span<float> buffer = stackalloc float[length];
        bool expandToStereo = _audioPlayer.Format.Channels == 2;
        if (expandToStereo) {
            length *= 2;
        }
        Span<float> playBuffer = stackalloc float[length];
        FillBuffer(buffer, playBuffer, expandToStereo);
        while (!_endThread) {
            Audio.WriteFullBuffer(_audioPlayer, playBuffer);
            FillBuffer(buffer, playBuffer, expandToStereo);
        }
}

    private void FillBuffer(Span<float> buffer, Span<float> playBuffer, bool expandToStereo) {
        _synth?.GetData(buffer);
        if (expandToStereo) {
            ChannelAdapter.MonoToStereo(buffer, playBuffer);
        }
    }

    private void StartPlaybackThread() {
        if(!_endThread) {
            _playbackThread.Start();
            _initialized = true;
        }
    }
}