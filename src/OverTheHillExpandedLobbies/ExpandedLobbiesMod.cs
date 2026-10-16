using System;
using System.Runtime.InteropServices;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using Il2CppFusion;

[assembly: MelonInfo(typeof(OverTheHillExpandedLobbies.ExpandedLobbiesMod), "Expanded Lobbies", "1.0.3", "Daniel Winn")]
[assembly: MelonGame("Funselektor", "over the hill")]

namespace OverTheHillExpandedLobbies
{
    public class ExpandedLobbiesMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("==========================================================");
            LoggerInstance.Msg(" Over The Hill: Expanded Lobbies Mod Loaded! (v1.0.3)     ");
            LoggerInstance.Msg(" Player limit selectable from 2 to 16 in single steps.    ");
            LoggerInstance.Msg("==========================================================");

            NativePatcher.ApplyPatches();
        }
    }

    /// <summary>
    /// Direct native binary patching in GameAssembly.dll.
    /// Overcomes IL2CPP struct pass-by-value limitations and directly patches:
    /// 1. Host StartGameAsync: changes vanilla clamp min(MaxPlayers, 4) to min(MaxPlayers, 16).
    /// 2. Client JoinSession: NOPs the conditional jump that shows the "Game Is Full" popup.
    /// </summary>
    internal static class NativePatcher
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        private const uint PAGE_EXECUTE_READWRITE = 0x40;

        public static void ApplyPatches()
        {
            try
            {
                IntPtr hModule = GetModuleHandle("GameAssembly.dll");
                if (hModule == IntPtr.Zero)
                {
                    MelonLogger.Error("[ExpandedLobbies] Failed to obtain GameAssembly.dll handle.");
                    return;
                }

                // 1. Host StartGameAsync MaxPlayers Clamp
                // Target RVA: 0xCFF269
                // Signature (13 bytes): 41 BF 04 00 00 00 41 3B F7 41 0F 4D F7
                // Instructions: mov r15d, 4; cmp esi, r15d; cmovge esi, r15d
                // Patch: Change 0x04 at RVA 0xCFF26B to 0x10 (16), clamping to 16 instead of 4.
                byte[] hostSig = new byte[] { 0x41, 0xBF, 0x04, 0x00, 0x00, 0x00, 0x41, 0x3B, 0xF7, 0x41, 0x0F, 0x4D, 0xF7 };
                IntPtr hostAddr = IntPtr.Add(hModule, 0xCFF269);
                if (CheckSignature(hostAddr, hostSig))
                {
                    PatchByte(IntPtr.Add(hModule, 0xCFF26B), 0x10, "Host MaxPlayers clamp (RVA 0xCFF26B: 4 -> 16)");
                }
                else
                {
                    IntPtr scanned = FindPattern(hModule, hostSig);
                    if (scanned != IntPtr.Zero)
                    {
                        PatchByte(IntPtr.Add(scanned, 2), 0x10, "Host MaxPlayers clamp (Pattern Scan: 4 -> 16)");
                    }
                    else
                    {
                        MelonLogger.Msg("[ExpandedLobbies] Host MaxPlayers clamp already patched or signature not found.");
                    }
                }

                // 2. Client JoinSession 'Game Is Full' Rejection Bypass
                // Target RVA: 0x9A73FE
                // Signature (9 bytes): 39 4A 28 0F 8D 8C 05 00 00
                // Instructions: cmp [rdx+0x28], ecx; jge 0x1809a7993
                // Patch: Replace 6-byte jump (0F 8D 8C 05 00 00) at RVA 0x9A7401 with NOPs (0x90).
                byte[] clientSig = new byte[] { 0x39, 0x4A, 0x28, 0x0F, 0x8D, 0x8C, 0x05, 0x00, 0x00 };
                IntPtr clientAddr = IntPtr.Add(hModule, 0x9A73FE);
                byte[] nops = new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
                if (CheckSignature(clientAddr, clientSig))
                {
                    PatchBytes(IntPtr.Add(hModule, 0x9A7401), nops, "Client JoinSession rejection bypass (RVA 0x9A7401)");
                }
                else
                {
                    byte[] clientSigShort = new byte[] { 0x39, 0x4A, 0x28, 0x0F, 0x8D };
                    IntPtr scanned = FindPattern(hModule, clientSigShort);
                    if (scanned != IntPtr.Zero)
                    {
                        PatchBytes(IntPtr.Add(scanned, 3), nops, "Client JoinSession rejection bypass (Pattern Scan)");
                    }
                    else
                    {
                        MelonLogger.Msg("[ExpandedLobbies] Client JoinSession check already patched or signature not found.");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in NativePatcher: {ex}");
            }
        }

        private static unsafe bool CheckSignature(IntPtr addr, byte[] sig)
        {
            try
            {
                byte* ptr = (byte*)addr;
                for (int i = 0; i < sig.Length; i++)
                {
                    if (ptr[i] != sig[i]) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static unsafe void PatchByte(IntPtr addr, byte newVal, string desc)
        {
            if (VirtualProtect(addr, (UIntPtr)1, PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                *(byte*)addr = newVal;
                VirtualProtect(addr, (UIntPtr)1, oldProtect, out _);
                MelonLogger.Msg($"[ExpandedLobbies] Native patch applied: {desc}");
            }
            else
            {
                MelonLogger.Error($"[ExpandedLobbies] VirtualProtect failed for {desc}");
            }
        }

        private static unsafe void PatchBytes(IntPtr addr, byte[] newBytes, string desc)
        {
            if (VirtualProtect(addr, (UIntPtr)newBytes.Length, PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                byte* ptr = (byte*)addr;
                for (int i = 0; i < newBytes.Length; i++)
                {
                    ptr[i] = newBytes[i];
                }
                VirtualProtect(addr, (UIntPtr)newBytes.Length, oldProtect, out _);
                MelonLogger.Msg($"[ExpandedLobbies] Native patch applied: {desc}");
            }
            else
            {
                MelonLogger.Error($"[ExpandedLobbies] VirtualProtect failed for {desc}");
            }
        }

        private static unsafe IntPtr FindPattern(IntPtr hModule, byte[] pattern)
        {
            try
            {
                byte* basePtr = (byte*)hModule;
                int peOffset = *(int*)(basePtr + 0x3C);
                short numSections = *(short*)(basePtr + peOffset + 6);
                short optHdrSize = *(short*)(basePtr + peOffset + 20);
                byte* sectPtr = basePtr + peOffset + 24 + optHdrSize;

                // Search inside executable sections (e.g. .text and il2cpp)
                for (int s = 0; s < numSections; s++)
                {
                    byte* sec = sectPtr + (s * 40);
                    uint virtSize = *(uint*)(sec + 8);
                    uint virtAddr = *(uint*)(sec + 12);
                    uint characteristics = *(uint*)(sec + 36);

                    // Check for IMAGE_SCN_MEM_EXECUTE (0x20000000)
                    if ((characteristics & 0x20000000) != 0 && virtSize > pattern.Length)
                    {
                        byte* start = basePtr + virtAddr;
                        byte* end = start + virtSize - pattern.Length;
                        int patLen = pattern.Length;

                        for (byte* curr = start; curr <= end; curr++)
                        {
                            bool match = true;
                            for (int i = 0; i < patLen; i++)
                            {
                                if (curr[i] != pattern[i])
                                {
                                    match = false;
                                    break;
                                }
                            }
                            if (match)
                            {
                                return (IntPtr)curr;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Pattern scan exception: {ex.Message}");
            }
            return IntPtr.Zero;
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
    /// Hook OnMaxPlayersChanged callback to ensure the '(Unstable)' tag updates immediately
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
    /// Client-side logging and backup capacity assignment when connecting to sessions.
    /// </summary>
    [HarmonyPatch(typeof(StateServerBrowser), nameof(StateServerBrowser.JoinSession))]
    public static class StateServerBrowser_JoinSession_Patch
    {
        public static void Prefix(StateServerBrowser __instance, OverlandSessionInfo sessionToJoin, bool joiningByCode)
        {
            try
            {
                string roomName = sessionToJoin?.RoomDisplayName ?? "Unknown";
                int currentPlayers = sessionToJoin?.FusionSessionInfo?.PlayerCount ?? -1;
                int maxPlayers = sessionToJoin?.FusionSessionInfo?.MaxPlayers ?? -1;
                MelonLogger.Msg($"[ExpandedLobbies] Joining session '{roomName}' (Players: {currentPlayers}/{maxPlayers}, ByCode: {joiningByCode})");

                if (sessionToJoin?.FusionSessionInfo != null && sessionToJoin.FusionSessionInfo.MaxPlayers < 16)
                {
                    sessionToJoin.FusionSessionInfo.MaxPlayers = 16;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in StateServerBrowser.JoinSession patch: {ex}");
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
