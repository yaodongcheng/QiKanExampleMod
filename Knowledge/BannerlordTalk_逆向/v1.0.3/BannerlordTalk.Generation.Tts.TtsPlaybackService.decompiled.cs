using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BannerlordTalk.Generation.Tts;

internal sealed class TtsPlaybackService : IDisposable
{
	private readonly struct MciPlaybackStartResult
	{
		internal static MciPlaybackStartResult NotStarted => new MciPlaybackStartResult(started: false, 0u);

		internal bool Started { get; }

		internal uint NativeErrorCode { get; }

		internal MciPlaybackStartResult(bool started, uint nativeErrorCode)
		{
			Started = started;
			NativeErrorCode = nativeErrorCode;
		}
	}

	private sealed class MciWavePlayback : IDisposable
	{
		private const int PollMilliseconds = 25;

		private const int PlaybackGraceMilliseconds = 5000;

		private const int MciResultCapacity = 64;

		internal const string PlaybackDirectoryName = "BannerlordTalk-TTS";

		private readonly object _gate = new object();

		private string _alias;

		private string _filePath;

		private bool _opened;

		private bool _stopRequested;

		private int _disposed;

		internal bool PlayWholeClip(byte[] wave, PcmWaveInfo waveInfo, CancellationToken cancellationToken, Func<Func<uint>, MciPlaybackStartResult> commitPlaybackStart)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string alias = "bltalk_tts_" + Guid.NewGuid().ToString("N");
			string text = PreparePlaybackDirectory();
			string text2 = Path.Combine(text, alias + ".wav");
			lock (_gate)
			{
				if (_stopRequested || Volatile.Read(ref _disposed) != 0)
				{
					return false;
				}
				_alias = alias;
				_filePath = text2;
			}
			try
			{
				using (FileStream fileStream = new FileStream(text2, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 8192, FileOptions.SequentialScan))
				{
					fileStream.Write(wave, 0, wave.Length);
					fileStream.Flush();
				}
				cancellationToken.ThrowIfCancellationRequested();
				if (IsStopRequested())
				{
					return false;
				}
				uint num = SendCommand("open \"" + text2 + "\" type waveaudio alias " + alias, null);
				if (num != 0)
				{
					throw new MciWavePlaybackException("open", num);
				}
				lock (_gate)
				{
					_opened = true;
					if (_stopRequested)
					{
						SendCommand("stop " + alias, null);
						return false;
					}
					MciPlaybackStartResult mciPlaybackStartResult = commitPlaybackStart(() => SendCommand("play " + alias + " from 0", null));
					if (!mciPlaybackStartResult.Started)
					{
						if (mciPlaybackStartResult.NativeErrorCode == 0)
						{
							return false;
						}
						throw new MciWavePlaybackException("start", mciPlaybackStartResult.NativeErrorCode);
					}
				}
				int num2 = Math.Max(5000, waveInfo.DurationMilliseconds + 5000);
				Stopwatch stopwatch = Stopwatch.StartNew();
				do
				{
					if (cancellationToken.IsCancellationRequested || IsStopRequested())
					{
						RequestStop();
						return false;
					}
					StringBuilder stringBuilder = new StringBuilder(64);
					uint num3 = SendCommand("status " + alias + " mode", stringBuilder);
					if (num3 != 0)
					{
						if (cancellationToken.IsCancellationRequested || IsStopRequested())
						{
							return false;
						}
						throw new MciWavePlaybackException("status", num3);
					}
					if (string.Equals(stringBuilder.ToString().Trim(), "stopped", StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
					if (stopwatch.ElapsedMilliseconds > num2)
					{
						RequestStop();
						throw new MciWavePlaybackException("timeout", 0u);
					}
				}
				while (!cancellationToken.WaitHandle.WaitOne(25));
				RequestStop();
				return false;
			}
			finally
			{
				CloseAndDelete(text);
			}
		}

		internal void RequestStop()
		{
			lock (_gate)
			{
				_stopRequested = true;
				if (_opened && !string.IsNullOrWhiteSpace(_alias))
				{
					SendCommand("stop " + _alias, null);
				}
			}
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				RequestStop();
				CloseAndDelete(GetPlaybackDirectory());
			}
		}

		private bool IsStopRequested()
		{
			lock (_gate)
			{
				return _stopRequested || Volatile.Read(ref _disposed) != 0;
			}
		}

		private void CloseAndDelete(string playbackDirectory)
		{
			string filePath;
			lock (_gate)
			{
				if (_opened && !string.IsNullOrWhiteSpace(_alias))
				{
					SendCommand("close " + _alias, null);
				}
				_opened = false;
				_alias = null;
				filePath = _filePath;
				_filePath = null;
			}
			if (!string.IsNullOrWhiteSpace(filePath))
			{
				DeleteOwnedTempFile(playbackDirectory, filePath);
			}
		}

		private static uint SendCommand(string command, StringBuilder result)
		{
			return mciSendStringW(command, result, result?.Capacity ?? 0, IntPtr.Zero);
		}

		[DllImport("winmm.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
		private static extern uint mciSendStringW(string command, StringBuilder returnValue, int returnLength, IntPtr callbackWindow);
	}

	private sealed class MciWavePlaybackException : Exception
	{
		internal string Stage { get; }

		internal uint NativeErrorCode { get; }

		internal MciWavePlaybackException(string stage, uint nativeErrorCode)
			: base("MCI wave playback failed.")
		{
			Stage = stage ?? "unknown";
			NativeErrorCode = nativeErrorCode;
		}
	}

	private sealed class QueuedJob
	{
		internal TtsPlaybackJob Job { get; }

		internal long Generation { get; }

		internal QueuedJob(TtsPlaybackJob job, long generation)
		{
			Job = job;
			Generation = generation;
		}
	}

	internal readonly struct PcmWaveInfo
	{
		internal int DataLength { get; }

		internal int DurationMilliseconds { get; }

		internal PcmWaveInfo(int dataLength, int durationMilliseconds)
		{
			DataLength = dataLength;
			DurationMilliseconds = durationMilliseconds;
		}
	}

	internal const int MaximumFishResponseBytes = 8388608;

	internal const int FishPcmSampleRate = 44100;

	internal const short FishPcmChannels = 1;

	internal const short FishPcmBitsPerSample = 16;

	internal const int MaximumFishPcmDurationSeconds = 90;

	private const int ReadBufferBytes = 8192;

	private const int ShutdownWaitMilliseconds = 250;

	private readonly BlockingCollection<QueuedJob> _queue;

	private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

	private readonly HttpClient _httpClient;

	private readonly Stopwatch _clock = Stopwatch.StartNew();

	private readonly Action<string> _safeDiagnostic;

	private readonly object _stateLock = new object();

	private readonly Task _consumer;

	private CancellationTokenSource _activeCancellation;

	private MciWavePlayback _activePlayback;

	private long _generation;

	private long _lastRequestStartedMilliseconds = long.MinValue;

	private int _disposed;

	private int _resourcesDisposed;

	internal TtsPlaybackService(int capacity = 16, Action<string> safeDiagnostic = null)
	{
		int boundedCapacity = Math.Max(1, Math.Min(64, capacity));
		_safeDiagnostic = safeDiagnostic;
		_queue = new BlockingCollection<QueuedJob>(new ConcurrentQueue<QueuedJob>(), boundedCapacity);
		CleanupOwnedStaleTempFiles();
		_httpClient = CreateHttpClient();
		_consumer = Task.Factory.StartNew(Consume, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
	}

	internal bool Enqueue(TtsPlaybackJob job)
	{
		if (!IsConfiguredJob(job))
		{
			Report("tts_job_rejected");
			return false;
		}
		bool flag;
		lock (_stateLock)
		{
			if (Volatile.Read(ref _disposed) != 0 || _queue.IsAddingCompleted)
			{
				return false;
			}
			flag = _queue.TryAdd(new QueuedJob(job, _generation));
		}
		Report(flag ? "tts_queued" : "tts_queue_full");
		return flag;
	}

	internal void CancelPending()
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			return;
		}
		MciWavePlayback activePlayback;
		lock (_stateLock)
		{
			_generation++;
			try
			{
				QueuedJob item;
				while (_queue.TryTake(out item))
				{
				}
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			SafeCancel(_activeCancellation);
			activePlayback = _activePlayback;
		}
		SafeRequestStop(activePlayback);
		Report("tts_pending_cancelled");
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}
		MciWavePlayback activePlayback;
		lock (_stateLock)
		{
			_generation++;
			try
			{
				QueuedJob item;
				while (_queue.TryTake(out item))
				{
				}
				_queue.CompleteAdding();
			}
			catch (ObjectDisposedException)
			{
			}
			SafeCancel(_activeCancellation);
			activePlayback = _activePlayback;
			SafeCancel(_lifetime);
		}
		SafeRequestStop(activePlayback);
		if (WaitForConsumer())
		{
			DisposeOwnedResources();
			return;
		}
		try
		{
			_consumer.ContinueWith(delegate
			{
				DisposeOwnedResources();
			}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
		}
		catch
		{
		}
	}

	private void Consume()
	{
		try
		{
			foreach (QueuedJob item in _queue.GetConsumingEnumerable(_lifetime.Token))
			{
				CancellationTokenSource cancellationTokenSource = null;
				try
				{
					lock (_stateLock)
					{
						if (item.Generation != _generation || Volatile.Read(ref _disposed) != 0)
						{
							continue;
						}
						cancellationTokenSource = (_activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token));
						goto IL_008e;
					}
					IL_008e:
					ProcessOneAsync(item.Job, cancellationTokenSource.Token).GetAwaiter().GetResult();
				}
				catch (OperationCanceledException)
				{
					Report("tts_item_cancelled");
				}
				catch
				{
					Report("tts_item_failed");
				}
				finally
				{
					lock (_stateLock)
					{
						if (_activeCancellation == cancellationTokenSource)
						{
							_activeCancellation = null;
						}
					}
					cancellationTokenSource?.Dispose();
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (ObjectDisposedException)
		{
		}
		catch
		{
			Report("tts_consumer_failed");
		}
	}

	private async Task ProcessOneAsync(TtsPlaybackJob job, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		byte[] array = await TryGenerateFishWaveAsync(job, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (array == null || array.Length == 0)
		{
			return;
		}
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (TryPlayWave(array, cancellationToken))
			{
				Report("tts_playback_completed");
			}
		}
		finally
		{
			Array.Clear(array, 0, array.Length);
		}
	}

	private async Task<byte[]> TryGenerateFishWaveAsync(TtsPlaybackJob job, CancellationToken cancellationToken)
	{
		FishTtsOptions options = job.Options;
		if (!options.TryGetHttpsEndpoint(out var endpoint))
		{
			Report("fish_endpoint_rejected");
			return null;
		}
		await ApplyThrottleAsync(options.ThrottleMilliseconds, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		cancellationToken.ThrowIfCancellationRequested();
		_lastRequestStartedMilliseconds = _clock.ElapsedMilliseconds;
		object obj = new
		{
			text = job.Text,
			reference_id = job.ReferenceId,
			temperature = options.Temperature,
			top_p = options.TopP,
			prosody = new
			{
				speed = options.Speed,
				volume = options.Volume,
				normalize_loudness = true
			},
			chunk_length = 300,
			normalize = true,
			format = "pcm",
			sample_rate = 44100,
			latency = "normal",
			max_new_tokens = 1024,
			repetition_penalty = 1.2,
			min_chunk_length = 50,
			condition_on_previous_chunks = true,
			early_stop_threshold = 1.0
		};
		HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint);
		try
		{
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
			try
			{
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
				if (!((HttpHeaders)request.Headers).TryAddWithoutValidation("model", options.Model))
				{
					Report("fish_model_header_rejected");
					return null;
				}
				request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
				request.Content = (HttpContent)new StringContent(JsonConvert.SerializeObject(obj), Encoding.UTF8, "application/json");
				HttpResponseMessage response = await _httpClient.SendAsync(request, (HttpCompletionOption)1, timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					if (!response.IsSuccessStatusCode)
					{
						Report("fish_http_" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
						return null;
					}
					if (!IsAllowedFishContentType(response.Content))
					{
						Report("fish_content_type_rejected");
						return null;
					}
					byte[] array;
					try
					{
						array = await ReadLimitedBytesAsync(response.Content, 8388608, timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
					}
					catch (InvalidDataException)
					{
						Report("fish_response_too_large");
						return null;
					}
					try
					{
						if (!TryBuildFishPcmWave(array, out var wave))
						{
							Report("fish_invalid_pcm");
							return null;
						}
						Report("fish_response_valid");
						return wave;
					}
					finally
					{
						Array.Clear(array, 0, array.Length);
					}
				}
				finally
				{
					((IDisposable)response)?.Dispose();
				}
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				Report("fish_timeout");
				return null;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch
			{
				Report("fish_request_failed");
				return null;
			}
		}
		finally
		{
			((IDisposable)request)?.Dispose();
		}
	}

	private async Task ApplyThrottleAsync(int throttleMilliseconds, CancellationToken cancellationToken)
	{
		if (_lastRequestStartedMilliseconds != long.MinValue && throttleMilliseconds > 0)
		{
			long num = _clock.ElapsedMilliseconds - _lastRequestStartedMilliseconds;
			long num2 = throttleMilliseconds - num;
			if (num2 > 0)
			{
				await Task.Delay((int)Math.Min(2147483647L, num2), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
	}

	private bool TryPlayWave(byte[] wave, CancellationToken cancellationToken)
	{
		if (!TryValidatePcmWave(wave, out var info))
		{
			Report("playback_wave_rejected");
			return false;
		}
		MciWavePlayback playback = new MciWavePlayback();
		bool result;
		try
		{
			lock (_stateLock)
			{
				cancellationToken.ThrowIfCancellationRequested();
				_activePlayback = playback;
			}
			result = playback.PlayWholeClip(wave, info, cancellationToken, (Func<uint> startCommand) => TryCommitPlaybackStart(playback, cancellationToken, startCommand));
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (MciWavePlaybackException ex2)
		{
			Report("playback_" + ex2.Stage + "_failed");
			result = false;
		}
		catch
		{
			Report("playback_failed");
			result = false;
		}
		finally
		{
			lock (_stateLock)
			{
				if (_activePlayback == playback)
				{
					_activePlayback = null;
				}
			}
			playback.Dispose();
		}
		return result;
	}

	private MciPlaybackStartResult TryCommitPlaybackStart(MciWavePlayback playback, CancellationToken cancellationToken, Func<uint> startCommand)
	{
		lock (_stateLock)
		{
			if (Volatile.Read(ref _disposed) != 0 || cancellationToken.IsCancellationRequested || _activePlayback != playback || startCommand == null)
			{
				return MciPlaybackStartResult.NotStarted;
			}
			uint num = startCommand();
			return (num == 0) ? new MciPlaybackStartResult(started: true, 0u) : new MciPlaybackStartResult(started: false, num);
		}
	}

	private static async Task<byte[]> ReadLimitedBytesAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
	{
		if (content == null)
		{
			return Array.Empty<byte>();
		}
		long? declared = content.Headers.ContentLength;
		if (declared.HasValue && (declared.Value < 0 || declared.Value > maximumBytes))
		{
			throw new InvalidDataException("Response body exceeds the configured cap.");
		}
		using Stream input = await content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false);
		using MemoryStream output = new MemoryStream((int)(declared.HasValue ? declared.Value : Math.Min(65536, maximumBytes)));
		byte[] buffer = new byte[8192];
		try
		{
			while (true)
			{
				int num = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				if (num <= 0)
				{
					break;
				}
				if (output.Length + num > maximumBytes)
				{
					throw new InvalidDataException("Response body exceeds the configured cap.");
				}
				output.Write(buffer, 0, num);
			}
			return output.ToArray();
		}
		finally
		{
			Array.Clear(buffer, 0, buffer.Length);
		}
	}

	internal static bool TryBuildFishPcmWave(byte[] pcm, out byte[] wave)
	{
		wave = null;
		if (!IsValidFishPcmLength((pcm != null) ? pcm.Length : 0))
		{
			return false;
		}
		byte[] array = new byte[44 + pcm.Length];
		WriteAscii(array, 0, "RIFF");
		WriteUInt32(array, 4, (uint)(array.Length - 8));
		WriteAscii(array, 8, "WAVE");
		WriteAscii(array, 12, "fmt ");
		WriteUInt32(array, 16, 16u);
		WriteUInt16(array, 20, 1);
		WriteUInt16(array, 22, 1);
		WriteUInt32(array, 24, 44100u);
		int num = 2;
		WriteUInt32(array, 28, (uint)(44100 * num));
		WriteUInt16(array, 32, (ushort)num);
		WriteUInt16(array, 34, 16);
		WriteAscii(array, 36, "data");
		WriteUInt32(array, 40, (uint)pcm.Length);
		Buffer.BlockCopy(pcm, 0, array, 44, pcm.Length);
		if (!TryValidatePcmWave(array, out var _))
		{
			Array.Clear(array, 0, array.Length);
			return false;
		}
		wave = array;
		return true;
	}

	internal static bool IsValidFishPcmLength(int byteLength)
	{
		int num = 88200 * 90;
		if (byteLength > 0 && (byteLength & 1) == 0 && byteLength <= 8388608)
		{
			return byteLength <= num;
		}
		return false;
	}

	internal static bool TryValidatePcmWave(byte[] data, out PcmWaveInfo info)
	{
		info = default(PcmWaveInfo);
		if (data == null || data.Length < 44 || !MatchesAscii(data, 0, "RIFF") || !MatchesAscii(data, 8, "WAVE"))
		{
			return false;
		}
		if (8L + (long)ReadUInt32(data, 4) != data.Length)
		{
			return false;
		}
		bool flag = false;
		bool flag2 = false;
		int num = 0;
		long num2 = data.Length;
		int num3 = 12;
		while (num3 < num2)
		{
			if (num3 > num2 - 8)
			{
				return false;
			}
			uint num4 = ReadUInt32(data, num3 + 4);
			long num5 = (long)num3 + 8L;
			long num6 = num5 + num4;
			if (num6 > num2)
			{
				return false;
			}
			if (MatchesAscii(data, num3, "fmt "))
			{
				if (flag || num4 < 16 || !ValidateFormat(data, (int)num5, num4))
				{
					return false;
				}
				flag = true;
			}
			else if (MatchesAscii(data, num3, "data"))
			{
				if (flag2 || num4 > int.MaxValue)
				{
					return false;
				}
				flag2 = true;
				num = (int)num4;
			}
			long num7 = num6 + (num4 & 1);
			if (num7 > num2)
			{
				return false;
			}
			num3 = (int)num7;
		}
		if (num3 != num2 || !flag || !flag2 || !IsValidFishPcmLength(num))
		{
			return false;
		}
		int num8 = 88200;
		info = new PcmWaveInfo(num, (int)Math.Ceiling((double)num * 1000.0 / (double)num8));
		return true;
	}

	private static bool ValidateFormat(byte[] data, int offset, uint length)
	{
		if (offset < 0 || length < 16 || offset > data.Length - length)
		{
			return false;
		}
		int num = 2;
		if (ReadUInt16(data, offset) == 1 && ReadUInt16(data, offset + 2) == 1 && ReadUInt32(data, offset + 4) == 44100 && ReadUInt32(data, offset + 8) == 44100 * num && ReadUInt16(data, offset + 12) == num)
		{
			return ReadUInt16(data, offset + 14) == 16;
		}
		return false;
	}

	private static HttpClient CreateHttpClient()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected O, but got Unknown
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Expected O, but got Unknown
		HttpClient val = new HttpClient((HttpMessageHandler)new HttpClientHandler
		{
			AllowAutoRedirect = false,
			UseCookies = false,
			UseDefaultCredentials = false,
			AutomaticDecompression = DecompressionMethods.None
		}, true)
		{
			Timeout = Timeout.InfiniteTimeSpan,
			MaxResponseContentBufferSize = 8388608L
		};
		val.DefaultRequestHeaders.ExpectContinue = false;
		return val;
	}

	private static bool IsAllowedFishContentType(HttpContent content)
	{
		object obj;
		if (content == null)
		{
			obj = null;
		}
		else
		{
			HttpContentHeaders headers = content.Headers;
			if (headers == null)
			{
				obj = null;
			}
			else
			{
				MediaTypeHeaderValue contentType = headers.ContentType;
				obj = ((contentType != null) ? contentType.MediaType : null);
			}
		}
		string text = (string)obj;
		if (!string.Equals(text, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}
		return true;
	}

	private static bool IsConfiguredJob(TtsPlaybackJob job)
	{
		if (job != null && !string.IsNullOrWhiteSpace(job.Text) && !string.IsNullOrWhiteSpace(job.ReferenceId) && job.Options != null && job.Options.TryGetHttpsEndpoint(out var _) && !string.IsNullOrWhiteSpace(job.Options.Model))
		{
			return !string.IsNullOrWhiteSpace(job.Options.ApiKey);
		}
		return false;
	}

	private bool WaitForConsumer()
	{
		if (_consumer == null || _consumer.IsCompleted)
		{
			return true;
		}
		if (Task.CurrentId.HasValue && Task.CurrentId.Value == _consumer.Id)
		{
			return false;
		}
		try
		{
			return _consumer.Wait(250);
		}
		catch (AggregateException)
		{
			return true;
		}
		catch (ObjectDisposedException)
		{
			return true;
		}
	}

	private void DisposeOwnedResources()
	{
		if (Interlocked.Exchange(ref _resourcesDisposed, 1) == 0)
		{
			try
			{
				((HttpMessageInvoker)_httpClient).Dispose();
			}
			catch
			{
			}
			try
			{
				_queue.Dispose();
			}
			catch
			{
			}
			try
			{
				_lifetime.Dispose();
			}
			catch
			{
			}
			Report("tts_disposed");
		}
	}

	private void Report(string code)
	{
		Action<string> safeDiagnostic = _safeDiagnostic;
		if (safeDiagnostic == null)
		{
			return;
		}
		string obj = CleanDiagnosticCode(code);
		try
		{
			safeDiagnostic(obj);
		}
		catch
		{
		}
	}

	private static string CleanDiagnosticCode(string value)
	{
		StringBuilder stringBuilder = new StringBuilder(64);
		string text = (value ?? string.Empty).ToLowerInvariant();
		foreach (char c in text)
		{
			if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')
			{
				stringBuilder.Append(c);
				if (stringBuilder.Length == 64)
				{
					break;
				}
			}
		}
		if (stringBuilder.Length != 0)
		{
			return stringBuilder.ToString();
		}
		return "tts_unknown";
	}

	private static void SafeCancel(CancellationTokenSource source)
	{
		if (source == null || source.IsCancellationRequested)
		{
			return;
		}
		try
		{
			source.Cancel();
		}
		catch
		{
		}
	}

	private static void SafeRequestStop(MciWavePlayback playback)
	{
		try
		{
			playback?.RequestStop();
		}
		catch
		{
		}
	}

	private static void CleanupOwnedStaleTempFiles()
	{
		try
		{
			string playbackDirectory = GetPlaybackDirectory();
			if (!Directory.Exists(playbackDirectory) || (File.GetAttributes(playbackDirectory) & FileAttributes.ReparsePoint) != 0)
			{
				return;
			}
			foreach (string item in Directory.EnumerateFiles(playbackDirectory, "bltalk_tts_*.wav", SearchOption.TopDirectoryOnly))
			{
				DeleteOwnedTempFile(playbackDirectory, item);
			}
		}
		catch
		{
		}
	}

	private static string GetPlaybackDirectory()
	{
		return Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BannerlordTalk-TTS"));
	}

	private static string PreparePlaybackDirectory()
	{
		string playbackDirectory = GetPlaybackDirectory();
		Directory.CreateDirectory(playbackDirectory);
		if ((File.GetAttributes(playbackDirectory) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
		{
			throw new IOException("TTS playback directory is not a trusted directory.");
		}
		return playbackDirectory;
	}

	private static void DeleteOwnedTempFile(string playbackDirectory, string filePath)
	{
		try
		{
			string fullPath = Path.GetFullPath(playbackDirectory);
			string fullPath2 = Path.GetFullPath(filePath);
			if (string.Equals(Path.GetDirectoryName(fullPath2), fullPath, StringComparison.OrdinalIgnoreCase) && IsOwnedWaveFileName(Path.GetFileName(fullPath2)) && (File.GetAttributes(fullPath2) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0)
			{
				File.Delete(fullPath2);
			}
		}
		catch
		{
		}
	}

	private static bool IsOwnedWaveFileName(string fileName)
	{
		if (string.IsNullOrEmpty(fileName) || fileName.Length != "bltalk_tts_".Length + 32 + ".wav".Length || !fileName.StartsWith("bltalk_tts_", StringComparison.Ordinal) || !fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		for (int i = "bltalk_tts_".Length; i < "bltalk_tts_".Length + 32; i++)
		{
			char c = fileName[i];
			if ((c < '0' || c > '9') && (c < 'a' || c > 'f'))
			{
				return false;
			}
		}
		return true;
	}

	private static bool MatchesAscii(byte[] data, int offset, string expected)
	{
		if (data == null || expected == null || offset < 0 || offset > data.Length - expected.Length)
		{
			return false;
		}
		for (int i = 0; i < expected.Length; i++)
		{
			if (data[offset + i] != (byte)expected[i])
			{
				return false;
			}
		}
		return true;
	}

	private static void WriteAscii(byte[] data, int offset, string value)
	{
		for (int i = 0; i < value.Length; i++)
		{
			data[offset + i] = (byte)value[i];
		}
	}

	private static ushort ReadUInt16(byte[] data, int offset)
	{
		return (ushort)(data[offset] | (data[offset + 1] << 8));
	}

	private static uint ReadUInt32(byte[] data, int offset)
	{
		return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
	}

	private static void WriteUInt16(byte[] data, int offset, ushort value)
	{
		data[offset] = (byte)value;
		data[offset + 1] = (byte)(value >> 8);
	}

	private static void WriteUInt32(byte[] data, int offset, uint value)
	{
		data[offset] = (byte)value;
		data[offset + 1] = (byte)(value >> 8);
		data[offset + 2] = (byte)(value >> 16);
		data[offset + 3] = (byte)(value >> 24);
	}
}
