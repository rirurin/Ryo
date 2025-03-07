﻿using Ryo.Interfaces;
using Ryo.Reloaded.Audio.Models;
using Ryo.Reloaded.Audio.Models.Containers;
using Ryo.Reloaded.CRI.CriAtomEx;
using static Ryo.Definitions.Functions.CriAtomExFunctions;

namespace Ryo.Reloaded.Audio.Services;

internal unsafe class RyoService
{
    private readonly ICriAtomEx criAtomEx;
    private readonly ICriAtomRegistry criAtomRegistry;
    private readonly Dictionary<int, float> modifiedCategories = new();
    private readonly HashSet<nint> modifiedPlayers = new();
    private readonly bool useSetFile;

    public RyoService(string game, ICriAtomEx criAtomEx, ICriAtomRegistry criAtomRegistry)
    {
        this.criAtomEx = criAtomEx;
        this.criAtomRegistry = criAtomRegistry;

        var patterns = CriAtomExGames.GetGamePatterns(game);
        if (patterns.criAtomExPlayer_SetFile != null)
        {
            Log.Debug($"New audio uses: {nameof(criAtomExPlayer_SetFile)}");
            this.useSetFile = true;
        }
        else
        {
            Log.Debug($"New audio uses: {nameof(criAtomExPlayer_SetData)}");
        }
    }

    public void SetAudio(Player player, AudioContainer container, int[]? categories)
    {
        var currentPlayer = player;

        var manualStart = false;
        if (container.PlayerId != -1
            && currentPlayer.Id != container.PlayerId
            && this.criAtomRegistry.GetPlayerById(container.PlayerId) is Player newPlayer)
        {
            currentPlayer = newPlayer;
            manualStart = true;
        }

        var newAudio = container.GetAudio();

        if (this.useSetFile)
        {
            this.criAtomEx.Player_SetFile(currentPlayer.Handle, IntPtr.Zero, (byte*)StringsCache.GetStringPtr(newAudio.FilePath));
        }
        else
        {
            var audioData = AudioCache.GetAudioData(newAudio.FilePath);
            this.criAtomEx.Player_SetData(currentPlayer.Handle, (byte*)audioData.Address, audioData.Size);
        }

        this.SetAudioVolume(currentPlayer, newAudio, categories);

        this.criAtomEx.Player_SetFormat(currentPlayer.Handle, newAudio.Format);
        this.criAtomEx.Player_SetSamplingRate(currentPlayer.Handle, newAudio.SampleRate);
        this.criAtomEx.Player_SetNumChannels(currentPlayer.Handle, newAudio.NumChannels);

        // Apply categories.
        if (categories?.Length > 0)
        {
            foreach (var id in categories)
            {
                this.criAtomEx.Player_SetCategoryById(player.Handle, (uint)id);
            }
        }

        if (manualStart)
        {
            this.criAtomEx.Player_Start(currentPlayer.Handle);
            Log.Debug($"Manually started player with ID: {currentPlayer.Id}");
        }

        Log.Debug($"Redirected {container.Name}\nFile: {newAudio.FilePath}");
    }

    private void SetAudioVolume(Player player, RyoAudio audio, int[]? categories)
    {
        if (audio.Volume < 0)
        {
            Log.Verbose("No custom volume set for file.");
            return;
        }

        // Set volume by player
        if (audio.UsePlayerVolume)
        {
            this.criAtomEx.Player_SetVolume(player.Handle, audio.Volume);
            this.modifiedPlayers.Add(player.Handle);
            Log.Debug($"Modified player volume. Player ID: {player.Id} || Volume: {audio.Volume}");
        }

        // Set volume by category.
        else if (categories?.Length > 0)
        {
            // Use first category for setting custom volume.
            int volumeCategory = audio.VolumeCategoryId != -1 ? audio.VolumeCategoryId : categories[0];

            // Save original category volume.
            if (!this.modifiedCategories.ContainsKey(volumeCategory))
            {
                var currentVolume = this.criAtomEx.Category_GetVolumeById((uint)volumeCategory);
                this.modifiedCategories[volumeCategory] = currentVolume;
            }

            this.criAtomEx.Category_SetVolumeById((uint)volumeCategory, audio.Volume);
            Log.Debug($"Modified volume. Category ID: {volumeCategory} || Volume: {audio.Volume}");
        }
    }

    public void ResetCustomVolumes(Player player, IEnumerable<int> categoryIds)
    {
        foreach (var id in categoryIds)
        {
            if (this.modifiedCategories.TryGetValue(id, out var ogVolume))
            {
                this.criAtomEx.Category_SetVolumeById((uint)id, ogVolume);
                this.modifiedCategories.Remove(id);
                Log.Debug($"Reset volume for Category ID: {id}");
            }
        }

        if (this.modifiedPlayers.Contains(player.Handle))
        {
            this.criAtomEx.Player_ResetParameters(player.Handle);
            Log.Debug($"Reset volume for Player ID: {player.Id}");
        }
    }

    private record CategoryVolume(int CategoryId, float OriginalVolume);
}
