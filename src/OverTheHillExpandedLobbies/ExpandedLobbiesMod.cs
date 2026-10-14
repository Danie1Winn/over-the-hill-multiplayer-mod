using System;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using Il2CppFusion;

[assembly: MelonInfo(typeof(OverTheHillExpandedLobbies.ExpandedLobbiesMod), "Expanded Lobbies", "1.0.1", "Daniel Winn")]
[assembly: MelonGame("Funselektor", "over the hill")]

namespace OverTheHillExpandedLobbies
{
    public class ExpandedLobbiesMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("==========================================================");
            LoggerInstance.Msg(" Over The Hill: Expanded Lobbies Mod Loaded! (v1.0.1)     ");
            LoggerInstance.Msg(" Player limit selectable from 2 to 16 in single steps.    ");
            LoggerInstance.Msg("==========================================================");
        }
    }

    /// <summary>
    /// Injects custom player limits (2 through 16 in steps of 1) into Create Server UI.
    /// Keeps options as pure integer strings so vanilla int.Parse succeeds without FormatException.
    /// </summary>
    [HarmonyPatch(typeof(ContextUICreateServer), nameof(ContextUICreateServer.Start))]
    public static class ContextUICreateServer_Start_Patch
    {
        public static CarouselOption MaxPlayersCarousel { get; private set; }

        public static void Postfix(ContextUICreateServer __instance)
        {
            try
            {
                if (__instance == null || __instance.MaxPlayersOptionSelector == null)
                    return;

                MelonLogger.Msg("[ExpandedLobbies] Injecting custom player limits (2-16) into Create Server UI...");
                var customOptions = new Il2CppSystem.Collections.Generic.List<string>();
                for (int i = 2; i <= 16; i++)
                {
                    customOptions.Add(i.ToString());
                }

                __instance.maxPlayersOptionsRange = customOptions;
                __instance.MaxPlayersOptionSelector.Initialize(customOptions, 2); // Default to index 2 ("4")
                MaxPlayersCarousel = __instance.MaxPlayersOptionSelector.CarouselOption;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in ContextUICreateServer.Start patch: {ex}");
            }
        }
    }

    /// <summary>
    /// When the player selector carousel updates its display, append '(Unstable)' if player count >= 8.
    /// This gives visual feedback in the UI without modifying the underlying option string.
    /// </summary>
    [HarmonyPatch(typeof(CarouselOption), nameof(CarouselOption.InternalSelected))]
    public static class CarouselOption_InternalSelected_Patch
    {
        public static void Postfix(CarouselOption __instance)
        {
            try
            {
                if (__instance != null && __instance.Option != null && __instance == ContextUICreateServer_Start_Patch.MaxPlayersCarousel)
                {
                    string selected = __instance.GetSelectedOption();
                    if (int.TryParse(selected, out int count) && count >= 8)
                    {
                        __instance.Option.text = $"{count} (Unstable)";
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in CarouselOption.InternalSelected patch: {ex}");
            }
        }
    }

    /// <summary>
    /// Also hook OnMaxPlayersChanged callback to ensure the '(Unstable)' tag updates immediately
    /// whenever the host navigates left or right.
    /// </summary>
    [HarmonyPatch(typeof(ContextUICreateServer), nameof(ContextUICreateServer.OnMaxPlayersChanged))]
    public static class ContextUICreateServer_OnMaxPlayersChanged_Patch
    {
        public static void Postfix(ContextUICreateServer __instance, int option)
        {
            try
            {
                if (__instance?.MaxPlayersOptionSelector?.CarouselOption?.Option != null)
                {
                    var options = __instance.maxPlayersOptionsRange;
                    if (options != null && option >= 0 && option < options.Count)
                    {
                        string selected = options[option];
                        if (int.TryParse(selected, out int count) && count >= 8)
                        {
                            __instance.MaxPlayersOptionSelector.CarouselOption.Option.text = $"{count} (Unstable)";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in OnMaxPlayersChanged patch: {ex}");
            }
        }
    }

    /// <summary>
    /// Safe spawn point wrapping: modulo wraps player indices over available lobby spawn points
    /// to prevent IndexOutOfRangeException when lobby contains more players than spawn points.
    /// </summary>
    [HarmonyPatch(typeof(Lobby), nameof(Lobby.GetSpawnPoint))]
    public static class Lobby_GetSpawnPoint_Patch
    {
        public static bool Prefix(Lobby __instance, PlayerRef playerRef, out int spawnPointIndex, ref SpawnPoint __result)
        {
            try
            {
                var points = __instance?.spawnPoints;
                if (points != null && points.Count > 0)
                {
                    int rawIndex = playerRef.RawEncoded;
                    spawnPointIndex = Math.Abs(rawIndex) % points.Count;
                    __result = points[spawnPointIndex];
                    return false; // Skip vanilla method
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in Lobby.GetSpawnPoint patch: {ex}");
            }

            spawnPointIndex = 0;
            return true;
        }
    }

    /// <summary>
    /// Guards OverlandSessionPlayers.AddPlayer against IndexOutOfRangeException or
    /// AssertException (NetworkArray / NetworkDictionary capacity limits) when 8+ players connect.
    /// </summary>
    [HarmonyPatch(typeof(OverlandSessionPlayers), nameof(OverlandSessionPlayers.AddPlayer))]
    public static class OverlandSessionPlayers_AddPlayer_Patch
    {
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, PlayerRef playerRef, string runtimePlayerPlayerName)
        {
            if (__exception != null)
            {
                MelonLogger.Warning($"[ExpandedLobbies] Guarded AddPlayer for {runtimePlayerPlayerName} (ID: {playerRef.RawEncoded}): {__exception.GetType().Name} - {__exception.Message}");
                return null; // Suppress exception
            }
            return null;
        }
    }

    /// <summary>
    /// Guards OverlandSessionPlayers.RemovePlayer against exceptions during disconnects with expanded lobbies.
    /// </summary>
    [HarmonyPatch(typeof(OverlandSessionPlayers), nameof(OverlandSessionPlayers.RemovePlayer))]
    public static class OverlandSessionPlayers_RemovePlayer_Patch
    {
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, PlayerRef playerRef)
        {
            if (__exception != null)
            {
                MelonLogger.Warning($"[ExpandedLobbies] Guarded RemovePlayer for player ID {playerRef.RawEncoded}: {__exception.GetType().Name} - {__exception.Message}");
                return null; // Suppress exception
            }
            return null;
        }
    }

    /// <summary>
    /// Guards RPC_ConnectedPlayer against unhandled network state exceptions.
    /// </summary>
    [HarmonyPatch(typeof(OverlandSessionPlayers), nameof(OverlandSessionPlayers.RPC_ConnectedPlayer))]
    public static class OverlandSessionPlayers_RPC_ConnectedPlayer_Patch
    {
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, PlayerRef playerRef)
        {
            if (__exception != null)
            {
                MelonLogger.Warning($"[ExpandedLobbies] Guarded RPC_ConnectedPlayer for player ID {playerRef.RawEncoded}: {__exception.GetType().Name} - {__exception.Message}");
                return null; // Suppress exception
            }
            return null;
        }
    }

    /// <summary>
    /// Guards GetPlayerName to provide a fallback name if dictionary lookup fails for extra players.
    /// </summary>
    [HarmonyPatch(typeof(OverlandSessionPlayers), nameof(OverlandSessionPlayers.GetPlayerName))]
    public static class OverlandSessionPlayers_GetPlayerName_Patch
    {
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, PlayerRef playerRef, ref string __result)
        {
            if (__exception != null)
            {
                __result = $"Player_{playerRef.RawEncoded}";
                return null; // Suppress exception and provide fallback
            }
            return null;
        }
    }
}
