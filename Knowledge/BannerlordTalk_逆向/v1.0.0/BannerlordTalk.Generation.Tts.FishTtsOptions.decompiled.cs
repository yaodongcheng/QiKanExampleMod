using System;

namespace BannerlordTalk.Generation.Tts;

internal sealed class FishTtsOptions
{
	internal string Endpoint { get; set; } = "https://api.fish.audio/v1/tts";


	internal string Model { get; set; } = "s2.1-pro";


	internal string ApiKey { get; set; } = string.Empty;


	internal float Temperature { get; set; } = 0.7f;


	internal float TopP { get; set; } = 0.7f;


	internal float Speed { get; set; } = 1f;


	internal float Volume { get; set; } = 15.03086f;


	internal int ThrottleMilliseconds { get; set; } = 1250;


	internal int TimeoutSeconds { get; set; } = 60;


	internal FishTtsOptions CloneNormalized()
	{
		return new FishTtsOptions
		{
			Endpoint = TtsTextRules.CleanHeaderValue(Endpoint, 2048),
			Model = TtsTextRules.CleanHeaderValue(Model, 256),
			ApiKey = TtsTextRules.CleanCredential(ApiKey, 4096),
			Temperature = NormalizeFloat(Temperature, 0f, 1f, 0.7f),
			TopP = NormalizeFloat(TopP, 0f, 1f, 0.7f),
			Speed = NormalizeFloat(Speed, 0.5f, 2f, 1f),
			Volume = NormalizeFloat(Volume, -20f, 20f, 0f),
			ThrottleMilliseconds = Math.Max(0, Math.Min(10000, ThrottleMilliseconds)),
			TimeoutSeconds = Math.Max(5, Math.Min(90, TimeoutSeconds))
		};
	}

	internal bool TryGetHttpsEndpoint(out Uri endpoint)
	{
		endpoint = null;
		if (!Uri.TryCreate((Endpoint ?? string.Empty).Trim(), UriKind.Absolute, out var result) || !string.Equals(result.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(result.Host) || !string.IsNullOrEmpty(result.UserInfo) || !string.IsNullOrEmpty(result.Query) || !string.IsNullOrEmpty(result.Fragment))
		{
			return false;
		}
		endpoint = result;
		return true;
	}

	private static float NormalizeFloat(float value, float minimum, float maximum, float fallback)
	{
		if (float.IsNaN(value) || float.IsInfinity(value))
		{
			return fallback;
		}
		return Math.Max(minimum, Math.Min(maximum, value));
	}
}
