using System.IO;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace SpotifyTaskbarWidget;

public sealed record TrackInfo(string Title, string Artist, bool IsPlaying, bool? IsShuffle,
    TimeSpan Position, TimeSpan Duration, DateTime PositionAtUtc);

/// <summary>
/// Lê a música atual através da API de media do Windows (SMTC).
/// O Spotify desktop publica aqui a faixa em reprodução — não é preciso
/// login nem API do Spotify. Prefere a sessão do Spotify; se não existir,
/// usa a sessão de media ativa.
/// </summary>
public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    private const int ThumbnailRetryCount = 4;
    private const int ThumbnailChunkBytes = 64 * 1024;
    private const int MaxThumbnailBytes = 16 * 1024 * 1024;

    /// <summary>Disparado (em thread de background) quando a faixa ou o estado mudam.</summary>
    public event Action? Changed;

    /// <summary>Disparado só quando a posição/duração mudam — acontece a cada poucos
    /// segundos, por isso o tratamento tem de ser leve.</summary>
    public event Action? TimelineChanged;

    /// <summary>True while the AmazonMusic SMTC Bridge session is available.</summary>
    public bool HasSession => _session != null;

    public async Task InitializeAsync()
    {
        // No arranque com o Windows o WinRT pode ainda não estar pronto — uma
        // falha transitória aqui deixava o widget sem faixa a sessão INTEIRA.
        // Insistir com recuo (4s→64s, ~2 min) antes de desistir de vez.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                break;
            }
            catch (Exception ex)
            {
                if (attempt >= 5)
                {
                    // Sem a API de sessões de media (edições N sem Media Feature
                    // Pack, WinRT avariado) — deixar rasto para o report
                    Diag.Once("smtc-init", "Media session API unavailable (this is why nothing shows as playing): " + ex.Message);
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(4 << attempt));
            }
        }
        _manager.SessionsChanged += (_, _) => PickSession();
        PickSession();
    }

    private readonly object _pickLock = new();

    /// <summary>Dessubscreve tudo — sem isto, uma janela fechada ficava presa
    /// na memória pelos eventos WinRT e continuava a processar sessões.</summary>
    public void Shutdown()
    {
        lock (_pickLock)
        {
            var old = _session;
            _session = null;
            if (old != null)
            {
                try
                {
                    old.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    old.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    old.TimelinePropertiesChanged -= OnTimelineChanged;
                }
                catch { }
            }
        }
    }

    private static bool IsAmazonMusicBridge(GlobalSystemMediaTransportControlsSession session)
    {
        var id = session.SourceAppUserModelId ?? string.Empty;

        // Current identity published by Fuku856/Amazon-Music-SMTC-Bridge:
        // AmazonMusicSmtc_<package id>!App
        // Keep tolerant aliases for older/test package identities.
        return id.Contains("AmazonMusicSmtc", StringComparison.OrdinalIgnoreCase)
            || id.Contains("AmazonMusic.SMTC", StringComparison.OrdinalIgnoreCase)
            || id.Contains("AmazonMusicSMTC", StringComparison.OrdinalIgnoreCase);
    }

    private void PickSession()
    {
        if (_manager == null) return;

        GlobalSystemMediaTransportControlsSession? chosen = null;
        try
        {
            var sessions = _manager.GetSessions();

            // Amazon Music itself publishes another incomplete session
            // (AmazonMobileLLC...). Never select it; use the bridge only.
            chosen = sessions.FirstOrDefault(IsAmazonMusicBridge);

            if (chosen == null)
            {
                Diag.Once("no-amazon-bridge-session",
                    "AmazonMusic SMTC Bridge session not found. Sessions: " +
                    string.Join(", ", sessions.Select(x => x.SourceAppUserModelId ?? "(null)")));
            }
            else
            {
                Diag.Once("amazon-bridge-session",
                    "Using AmazonMusic SMTC Bridge session: " +
                    (chosen.SourceAppUserModelId ?? "(null)"));
            }
        }
        catch (Exception ex)
        {
            Diag.Once("get-sessions", "Reading media sessions failed: " + ex.Message);
        }

        lock (_pickLock)
        {
            var old = _session;
            if (ReferenceEquals(old, chosen))
            {
                if (chosen == null) Changed?.Invoke();
                return;
            }

            if (old != null)
            {
                try
                {
                    old.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    old.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    old.TimelinePropertiesChanged -= OnTimelineChanged;
                }
                catch { }
            }

            _session = chosen;
            if (chosen != null)
            {
                chosen.MediaPropertiesChanged += OnMediaPropertiesChanged;
                chosen.PlaybackInfoChanged += OnPlaybackInfoChanged;
                chosen.TimelinePropertiesChanged += OnTimelineChanged;
            }
        }

        Changed?.Invoke();
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        Changed?.Invoke();

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        Changed?.Invoke();

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        TimelineChanged?.Invoke();

    /// <summary>Leitura rápida da timeline (sem propriedades da faixa nem capa).</summary>
    public (TimeSpan Position, TimeSpan Duration, bool IsPlaying, DateTime PositionAtUtc)? GetTimeline()
    {
        var s = _session;
        if (s == null) return null;
        try
        {
            var tl = s.GetTimelineProperties();
            var pi = s.GetPlaybackInfo();
            return (tl?.Position ?? TimeSpan.Zero,
                    tl != null ? tl.EndTime - tl.StartTime : TimeSpan.Zero,
                    pi?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    tl?.LastUpdatedTime.UtcDateTime ?? DateTime.UtcNow);
        }
        catch
        {
            return null;
        }
    }

    public async Task<TrackInfo?> GetTrackAsync()
    {
        var s = _session;
        if (s == null) return null;
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            var pi = s.GetPlaybackInfo();
            bool playing = pi?.PlaybackStatus ==
                           GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            var tl = s.GetTimelineProperties();
            TimeSpan position = tl?.Position ?? TimeSpan.Zero;
            TimeSpan duration = tl != null ? tl.EndTime - tl.StartTime : TimeSpan.Zero;
            DateTime positionAt = tl?.LastUpdatedTime.UtcDateTime ?? DateTime.UtcNow;

            return new TrackInfo(props?.Title ?? "", props?.Artist ?? "", playing, pi?.IsShuffleActive,
                position, duration, positionAt);
        }
        catch (Exception ex)
        {
            Diag.Once("get-track", "Reading track from AmazonMusic SMTC Bridge failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Reads album art from the bridge's RandomAccessStreamReference.
    ///
    /// The upstream widget trusted stream.Size and asked DataReader to load the
    /// whole stream in one shot. Some cross-process/in-memory SMTC thumbnails can
    /// report a size before every byte is immediately readable; that path then
    /// throws and upstream silently returns null. This version reads until EOF in
    /// chunks and retries transient publication races.
    /// </summary>
    public async Task<byte[]?> GetThumbnailAsync()
    {
        var s = _session;
        if (s == null) return null;

        Exception? lastError = null;

        for (int attempt = 0; attempt < ThumbnailRetryCount; attempt++)
        {
            try
            {
                var props = await s.TryGetMediaPropertiesAsync();
                if (props?.Thumbnail != null)
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    var bytes = await ReadAllThumbnailBytesAsync(stream);
                    if (bytes is { Length: > 0 })
                    {
                        Diag.Once("amazon-art-ok",
                            $"AmazonMusic artwork read successfully ({bytes.Length} bytes).");
                        return bytes;
                    }
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            if (attempt + 1 < ThumbnailRetryCount)
                await Task.Delay(120 + (attempt * 120));
        }

        if (lastError != null)
        {
            Diag.Once("amazon-art-read-failed",
                "AmazonMusic artwork stream could not be read: " +
                lastError.GetType().Name + ": " + lastError.Message);
        }
        else
        {
            Diag.Once("amazon-art-empty",
                "AmazonMusic SMTC Bridge reported no readable artwork after retries.");
        }

        return null;
    }

    private static async Task<byte[]?> ReadAllThumbnailBytesAsync(IRandomAccessStreamWithContentType stream)
    {
        using var input = stream.GetInputStreamAt(0);
        using var reader = new DataReader(input)
        {
            InputStreamOptions = InputStreamOptions.Partial,
        };
        using var output = new MemoryStream();

        while (true)
        {
            uint loaded = await reader.LoadAsync((uint)ThumbnailChunkBytes);
            if (loaded == 0)
                break;

            var chunk = new byte[(int)loaded];
            reader.ReadBytes(chunk);
            output.Write(chunk, 0, chunk.Length);

            if (output.Length > MaxThumbnailBytes)
                throw new InvalidDataException("SMTC thumbnail exceeded the 16 MiB safety limit.");
        }

        return output.Length == 0 ? null : output.ToArray();
    }

    public async Task TogglePlayPauseAsync()
    {
        var s = _session;
        if (s == null) return;
        try { await s.TryTogglePlayPauseAsync(); } catch { }
    }

    public async Task NextAsync()
    {
        var s = _session;
        if (s == null) return;
        try { await s.TrySkipNextAsync(); } catch { }
    }

    public async Task PreviousAsync()
    {
        var s = _session;
        if (s == null) return;
        try { await s.TrySkipPreviousAsync(); } catch { }
    }

    public async Task SeekAsync(TimeSpan position)
    {
        var s = _session;
        if (s == null) return;
        try { await s.TryChangePlaybackPositionAsync(position.Ticks); } catch { }
    }

    public async Task CycleRepeatAsync()
    {
        var s = _session;
        if (s == null) return;
        try
        {
            var current = s.GetPlaybackInfo()?.AutoRepeatMode ?? Windows.Media.MediaPlaybackAutoRepeatMode.None;
            var next = current switch
            {
                Windows.Media.MediaPlaybackAutoRepeatMode.None => Windows.Media.MediaPlaybackAutoRepeatMode.List,
                Windows.Media.MediaPlaybackAutoRepeatMode.List => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
                _ => Windows.Media.MediaPlaybackAutoRepeatMode.None,
            };
            await s.TryChangeAutoRepeatModeAsync(next);
        }
        catch { }
    }

    public async Task ToggleShuffleAsync()
    {
        var s = _session;
        if (s == null) return;
        try
        {
            bool current = s.GetPlaybackInfo()?.IsShuffleActive ?? false;
            await s.TryChangeShuffleActiveAsync(!current);
        }
        catch { }
    }
}
