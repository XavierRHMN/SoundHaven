namespace SoundHaven.Models;

/// <summary>A track the user never wants recommended again.</summary>
public sealed record DislikedSong(string? VideoId, string Title, string? Artist);
