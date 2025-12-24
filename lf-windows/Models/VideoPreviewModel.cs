using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;

namespace LfWindows.Models;

public partial class VideoPreviewModel : ObservableObject, IDisposable
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;
    private WriteableBitmap? _videoBuffer;
    private IntPtr _bufferPtr;
    private uint _currentWidth, _currentHeight, _currentPitch;
    // Flag to indicate we are auto-playing to capture the first frame
    private bool _isPreloading = false;
    private bool _suppressFrames = false;
    private readonly object _bufferLock = new();

    // Keep delegates alive
    private readonly MediaPlayer.LibVLCVideoLockCb _lockCb;
    private readonly MediaPlayer.LibVLCVideoUnlockCb _unlockCb;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _displayCb;
    private readonly MediaPlayer.LibVLCVideoFormatCb _formatCb;
    private readonly MediaPlayer.LibVLCVideoCleanupCb _cleanupCb;
    
    private volatile bool _isDisposed = false;

    [ObservableProperty]
    private WriteableBitmap? _videoFrame;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSoundOn))]
    private bool _isMuted;

    public bool IsSoundOn => !IsMuted;

    [ObservableProperty]
    private uint _videoWidth;

    [ObservableProperty]
    private uint _videoHeight;

    [ObservableProperty]
    private bool _showPlayButton = true;

    [ObservableProperty]
    private TimeSpan _totalDuration;

    [ObservableProperty]
    private TimeSpan _currentTime;

    [ObservableProperty]
    private double _progress;

    private bool _isDragging;
    public bool IsDragging
    {
        get => _isDragging;
        set => _isDragging = value;
    }

    public ObservableCollection<KeyValuePair<string, string>> Metadata { get; } = new();

    private void UpdateMetadata(string key, string value)
    {
        Dispatcher.UIThread.InvokeAsync(() => 
        {
            var existing = Metadata.FirstOrDefault(x => x.Key == key);
            if (!string.IsNullOrEmpty(existing.Key))
            {
                Metadata.Remove(existing);
            }
            Metadata.Add(new KeyValuePair<string, string>(key, value));
        });
    }

    private void ExtractMediaInfo(Media media)
    {
        if (media.Duration > 0)
        {
            var duration = TimeSpan.FromMilliseconds(media.Duration);
            UpdateMetadata("时长", duration.ToString(@"hh\:mm\:ss"));
        }

        foreach (var track in media.Tracks)
        {
            if (track.TrackType == TrackType.Video)
            {
                var width = track.Data.Video.Width;
                var height = track.Data.Video.Height;
                
                // Handle SAR (Sample Aspect Ratio)
                if (track.Data.Video.SarNum > 0 && track.Data.Video.SarDen > 0)
                {
                    width = width * track.Data.Video.SarNum / track.Data.Video.SarDen;
                }

                VideoWidth = width;
                VideoHeight = height;
                
                UpdateMetadata("分辨率", $"{track.Data.Video.Width} × {track.Data.Video.Height}");
                if (track.Data.Video.FrameRateDen > 0)
                {
                    UpdateMetadata("帧率", $"{track.Data.Video.FrameRateNum / (double)track.Data.Video.FrameRateDen:F2} fps");
                }
                UpdateMetadata("编码", track.Codec.ToString());
                break;
            }
        }
    }

    public VideoPreviewModel(string filePath)
    {
        _libVLC = new LibVLC(
            "--no-video-title-show",
            "--no-osd",
            "--no-video-on-top",
            "--quiet",
            "--verbose=-1", // Silence
            "--no-stats",
            "--avcodec-hw=any" // Try to use hardware acceleration to potentially bypass swscaler
        );
        // Redirect logs to empty handler to prevent console output
        _libVLC.Log += (s, e) => { };
        _mediaPlayer = new MediaPlayer(_libVLC);

        // Setup callbacks
        _lockCb = Lock;
        _unlockCb = Unlock;
        _displayCb = Display;
        _formatCb = VideoFormat;
        _cleanupCb = VideoCleanup;

        _mediaPlayer.LengthChanged += (s, e) => 
        {
            Dispatcher.UIThread.Post(() => TotalDuration = TimeSpan.FromMilliseconds(e.Length));
        };

        _mediaPlayer.TimeChanged += (s, e) =>
        {
            Dispatcher.UIThread.Post(() => CurrentTime = TimeSpan.FromMilliseconds(e.Time));
        };

        _mediaPlayer.PositionChanged += (s, e) =>
        {
            // Check on background thread to avoid spamming dispatcher
            if (!IsDragging)
            {
                Dispatcher.UIThread.Post(() => 
                {
                    // Check again on UI thread to avoid race conditions
                    if (!IsDragging)
                    {
                        Progress = e.Position;
                    }
                });
            }
        };

        _mediaPlayer.Playing += (s, e) => Dispatcher.UIThread.Post(() => 
        {
            IsPlaying = true;
            ShowPlayButton = false;
        });

        _mediaPlayer.Paused += (s, e) => Dispatcher.UIThread.Post(() => 
        {
            IsPlaying = false;
            ShowPlayButton = true;
        });

        _mediaPlayer.Stopped += (s, e) => Dispatcher.UIThread.Post(() => 
        {
            IsPlaying = false;
            ShowPlayButton = true;
            Progress = 0;
            CurrentTime = TimeSpan.Zero;
        });

        var media = new Media(_libVLC, filePath, FromType.FromPath);
        
        // Extract file info immediately
        try 
        {
            var fileInfo = new FileInfo(filePath);
            UpdateMetadata("文件大小", FormatFileSize(fileInfo.Length));
            UpdateMetadata("创建时间", fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        catch { /* Ignore file info errors */ }

        // Handle media parsing for metadata
        media.ParsedChanged += (s, e) => 
        {
            if (e.ParsedStatus == MediaParsedStatus.Done)
            {
                ExtractMediaInfo(media);
            }
        };

        media.Parse(MediaParseOptions.ParseLocal);
        
        // Use SetVideoFormatCallbacks instead of SetVideoFormat to handle dynamic resolution
        _mediaPlayer.SetVideoFormatCallbacks(_formatCb, _cleanupCb);
        _mediaPlayer.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);
        
        _mediaPlayer.Media = media;
        
        // Default to Muted as requested
        _mediaPlayer.Mute = true;
        IsMuted = true;

        // Event handlers
        // Note: first-frame capture is handled via _isPreloading and Initialize(),
        // so Playing handler just updates UI state if not preloading.
        _mediaPlayer.Playing += (s, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_isPreloading)
                {
                    IsPlaying = true;
                    ShowPlayButton = false;
                }
            });
        };
        
        _mediaPlayer.Paused += (s, e) => 
        {
            Dispatcher.UIThread.Post(() => 
            {
                IsPlaying = false;
                ShowPlayButton = true;
            });
        };

        _mediaPlayer.Stopped += (s, e) => 
        {
            Dispatcher.UIThread.Post(() => 
            {
                IsPlaying = false;
                ShowPlayButton = true;
            });
        };
        
        _mediaPlayer.EndReached += (s, e) =>
        {
            Dispatcher.UIThread.Post(() => 
            {
                IsPlaying = false;
                ShowPlayButton = true;
            });
            
            Task.Run(() => _mediaPlayer.Stop());
        };
    }

    private uint VideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        // Set chroma to RV32 (BGRA)
        Marshal.Copy(new byte[] { (byte)'R', (byte)'V', (byte)'3', (byte)'2' }, 0, chroma, 4);

        // Align width and height to 16 bytes (standard macroblock size) to improve compatibility and performance
        width = (width + 15) & ~15u;
        height = (height + 15) & ~15u;

        // Limit resolution to 1080p to save memory
        if (width > 1920 || height > 1080)
        {
            // Calculate aspect ratio
            double ratio = (double)width / height;
            if (width > 1920)
            {
                width = 1920;
                height = (uint)(1920 / ratio);
            }
            if (height > 1080)
            {
                height = 1080;
                width = (uint)(1080 * ratio);
            }
        }

        // Align width and height to 2 to ensure even dimensions (good practice for video)
        // We do NOT align to 16 anymore to avoid black borders/padding issues
        width = (width + 1) & ~1u;
        height = (height + 1) & ~1u;

        // Calculate pitch (stride) aligned to 32 bytes as required by LibVLC
        // pitches = width * 4; // Old simple calculation
        uint stride = width * 4;
        pitches = (stride + 31) & ~31u;
        lines = height;

        lock (_bufferLock)
        {
            _currentWidth = width;
            _currentHeight = height;
            _currentPitch = pitches;

            // Re-allocate buffer if needed
            if (_bufferPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_bufferPtr);
            }
            _bufferPtr = Marshal.AllocHGlobal((int)(pitches * lines));
        }

        // Create WriteableBitmap on UI thread
        var w = width;
        var h = height;
        Dispatcher.UIThread.Post(() =>
        {
            _videoBuffer?.Dispose();
            _videoBuffer = new WriteableBitmap(new PixelSize((int)w, (int)h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            VideoFrame = _videoBuffer;
        });

        return 1;
    }

    private void VideoCleanup(ref IntPtr opaque)
    {
        // Cleanup handled in Dispose usually, but we can free buffer here if VLC is done with it
    }

    private IntPtr Lock(IntPtr opaque, IntPtr planes)
    {
        lock (_bufferLock)
        {
            if (_bufferPtr != IntPtr.Zero)
            {
                Marshal.WriteIntPtr(planes, _bufferPtr);
            }
        }
        return IntPtr.Zero;
    }

    private void Unlock(IntPtr opaque, IntPtr picture, IntPtr planes)
    {
        // No-op
    }

    private void Display(IntPtr opaque, IntPtr picture)
    {
        // Schedule copy on UI thread at Render priority for timely update
        Dispatcher.UIThread.Post(() =>
        {
            if (_isDisposed) return;
            if (_videoBuffer == null) return;
            if (_suppressFrames) return;

            try
            {
                lock (_bufferLock)
                {
                    if (_bufferPtr == IntPtr.Zero) return;

                    // Verify dimensions match to avoid mismatch
                    if (_videoBuffer.PixelSize.Width != _currentWidth || _videoBuffer.PixelSize.Height != _currentHeight) return;

                    using (var locked = _videoBuffer.Lock())
                    {
                        unsafe
                        {
                            var destStride = locked.RowBytes;
                            var srcStride = (int)_currentPitch;
                            var height = (int)_currentHeight;
                            
                            if (destStride == srcStride)
                            {
                                var size = (long)srcStride * height;
                                Buffer.MemoryCopy((void*)_bufferPtr, (void*)locked.Address, (ulong)size, (ulong)size);
                            }
                            else
                            {
                                // Row by row copy to handle stride mismatch safely
                                var copyBytes = Math.Min(destStride, srcStride);
                                byte* src = (byte*)_bufferPtr;
                                byte* dst = (byte*)locked.Address;
                                for (int y = 0; y < height; y++)
                                {
                                    Buffer.MemoryCopy(src, dst, destStride, copyBytes);
                                    src += srcStride;
                                    dst += destStride;
                                }
                            }
                        }
                    }
                }

                // Notify frame updated
                OnPropertyChanged(nameof(VideoFrame));

                // Handle preloading completion
                if (_isPreloading)
                {
                    if (_isDisposed) return;
                    _isPreloading = false;
                    _suppressFrames = true;
                    // Pause playback to show static first frame
                    // And restore mute state
                    try 
                    { 
                        if (!_isDisposed)
                        {
                            _mediaPlayer.Pause(); 
                            _mediaPlayer.Mute = IsMuted;
                        }
                        // Do NOT seek back to 0, as it causes a re-render/jump and swscaler warnings
                        // _mediaPlayer.Time = 0; 
                    } 
                    catch { }
                }
            }
            catch
            {
                // swallow any copy errors to avoid crashing VLC thread
            }
        }, DispatcherPriority.Render);
    }

    public void Initialize()
    {
        if (_mediaPlayer.Media != null)
        {
            // Start preloading for first frame
            _isPreloading = true;
            _suppressFrames = false;
            _mediaPlayer.Mute = true; // Mute during preload
            _mediaPlayer.Play();
        }
    }

    [RelayCommand]
    private void TogglePlay()
    {
        _suppressFrames = false;

        if (_isPreloading)
        {
            // User interrupted preloading, switch to normal play
            _isPreloading = false;
            _mediaPlayer.Mute = IsMuted;
            if (!_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Play();
            }
            // Force UI update
            IsPlaying = true;
            ShowPlayButton = false;
            return;
        }

        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
        }
        else
        {
            _mediaPlayer.Play();
        }
    }

    [RelayCommand]
    private void ToggleMute()
    {
        // Toggle based on current ViewModel state to ensure UI sync
        var newState = !IsMuted;
        _mediaPlayer.Mute = newState;
        IsMuted = newState;
    }

    public void Seek(double position)
    {
        if (position < 0) position = 0;
        if (position > 1) position = 1;
        _mediaPlayer.Position = (float)position;
    }

    public void Stop()
    {
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Stop();
        }
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        // Stop player first to prevent new callbacks
        try { _mediaPlayer.Stop(); } catch { }

        _mediaPlayer.Dispose();
        _libVLC.Dispose();
        
        if (_bufferPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_bufferPtr);
            _bufferPtr = IntPtr.Zero;
        }
        
        _videoBuffer?.Dispose();
        _videoBuffer = null;
    }
}
