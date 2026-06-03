namespace VibeSwitcher.Models;

public record SoundOverrideResult(bool Enabled, string Tone, string? CustomPath, int Volume, bool ShowBanner = false);
