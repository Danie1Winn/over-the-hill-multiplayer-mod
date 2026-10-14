using System;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using Il2CppFusion;

[assembly: MelonInfo(typeof(OverTheHillExpandedLobbies.ExpandedLobbiesMod), "Expanded Lobbies", "1.0.0", "Daniel Winn")]
[assembly: MelonGame("Funselektor", "over the hill")]

namespace OverTheHillExpandedLobbies
{
    public class ExpandedLobbiesMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("==========================================================");
            LoggerInstance.Msg(" Over The Hill: Expanded Lobbies Mod Loaded!              ");
            LoggerInstance.Msg(" Max player limit selectable up to 16 players.            ");
            LoggerInstance.Msg("==========================================================");
        }
    }

    /// <summary>
    /// Injects custom lobby size options into the Create Server UI dropdown,
    /// marking 8+ player options with an '(Unstable)' warning tag.
    /// </summary>
    [HarmonyPatch(typeof(ContextUICreateServer), nameof(ContextUICreateServer.Start))]
    public static class ContextUICreateServer_Start_Patch
    {
        public static void Postfix(ContextUICreateServer __instance)
        {
            try
            {
                if (__instance == null || __instance.MaxPlayersOptionSelector == null)
                    return;

                MelonLogger.Msg("[ExpandedLobbies] Injecting custom player limits into Create Server UI...");
                var customOptions = new Il2CppSystem.Collections.Generic.List<string>();
                string[] values = new string[] { "2", "3", "4", "6", "8 (Unstable)", "10 (Unstable)", "12 (Unstable)", "16 (Unstable)" };
                foreach (var v in values)
                {
                    customOptions.Add(v);
                }

                __instance.maxPlayersOptionsRange = customOptions;
                __instance.MaxPlayersOptionSelector.Initialize(customOptions, 2); // Default to "4" (index 2)
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in ContextUICreateServer.Start patch: {ex}");
            }
        }
    }

    /// <summary>
    /// Sanitizes player limit text before vanilla parsing (e.g. "8 (Unstable)" -> "8")
    /// and ensures GameSetupContext.MaxPlayers receives the intended integer.
    /// </summary>
    [HarmonyPatch(typeof(ContextUICreateServer), nameof(ContextUICreateServer.SetGameSetupContextValues))]
    public static class ContextUICreateServer_SetGameSetupContextValues_Patch
    {
        private static string _savedOptionText = null;
        private static int _savedIndex = -1;

        public static void Prefix(ContextUICreateServer __instance, ref GameSetupContext gameSetupContext)
        {
            try
            {
                if (__instance != null && __instance.maxPlayersOptionsRange != null)
                {
                    int selectedIdx = __instance.maxPlayersOptionsSelectedIndex;
                    if (selectedIdx >= 0 && selectedIdx < __instance.maxPlayersOptionsRange.Count)
                    {
                        string optionStr = __instance.maxPlayersOptionsRange[selectedIdx];
                        if (optionStr != null && optionStr.Contains(" "))
                        {
                            _savedOptionText = optionStr;
                            _savedIndex = selectedIdx;
                            __instance.maxPlayersOptionsRange[selectedIdx] = optionStr.Split(' ')[0];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in SetGameSetupContextValues Prefix: {ex}");
            }
        }

        public static void Postfix(ContextUICreateServer __instance, ref GameSetupContext gameSetupContext)
        {
            try
            {
                // Restore display text in UI list
                if (_savedOptionText != null && _savedIndex >= 0 && __instance?.maxPlayersOptionsRange != null)
                {
                    __instance.maxPlayersOptionsRange[_savedIndex] = _savedOptionText;
                    _savedOptionText = null;
                    _savedIndex = -1;
                }

                if (gameSetupContext != null && __instance != null)
                {
                    var options = __instance.maxPlayersOptionsRange;
                    int selectedIdx = __instance.maxPlayersOptionsSelectedIndex;

                    if (options != null && selectedIdx >= 0 && selectedIdx < options.Count)
                    {
                        string selectedStr = options[selectedIdx];
                        string numStr = selectedStr.Split(' ')[0];
                        if (int.TryParse(numStr, out int limit))
                        {
                            gameSetupContext.MaxPlayers = limit;
                            MelonLogger.Msg($"[ExpandedLobbies] Host configured room with MaxPlayers = {limit}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in SetGameSetupContextValues Postfix: {ex}");
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
