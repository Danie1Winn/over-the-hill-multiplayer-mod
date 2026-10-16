using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using Il2CppFusion;

[assembly: MelonInfo(typeof(OverTheHillExpandedLobbies.ExpandedLobbiesMod), "Expanded Lobbies", "1.0.4", "Daniel Winn")]
[assembly: MelonGame("Funselektor", "over the hill")]

namespace OverTheHillExpandedLobbies
{
    public class ExpandedLobbiesMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("==========================================================");
            LoggerInstance.Msg(" Over The Hill: Expanded Lobbies Mod Loaded! (v1.0.4)     ");
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

                for (int s = 0; s < numSections; s++)
                {
                    byte* sec = sectPtr + (s * 40);
                    uint virtSize = *(uint*)(sec + 8);
                    uint virtAddr = *(uint*)(sec + 12);
                    uint characteristics = *(uint*)(sec + 36);

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
    /// Helper for managing up to 16 player slots, UI indicators, and colors across MapView, Compass, and Session.
    /// Vanilla OrderedPlayerRefs and PlayerColors are weaved Photon Fusion NetworkArrays of fixed capacity 4.
    /// Slots 0..3 mirror OrderedPlayerRefs[0..3], while slots 4..15 deterministically assign 5th-16th players.
    /// </summary>
    internal static class ExpandedPlayerHelper
    {
        public const int MaxExpandedPlayers = 16;

        public static readonly Dictionary<int, string> CachedPlayerNames = new Dictionary<int, string>();

        // 12 distinct, vibrant colors for players 5 through 16
        private static readonly Color[] ExtraPlayerColors = new Color[]
        {
            new Color(0.00f, 0.85f, 0.95f, 1f), // 5: Cyan
            new Color(1.00f, 0.55f, 0.10f, 1f), // 6: Orange
            new Color(0.95f, 0.25f, 0.80f, 1f), // 7: Magenta / Pink
            new Color(0.55f, 0.95f, 0.20f, 1f), // 8: Lime Green
            new Color(0.25f, 0.55f, 1.00f, 1f), // 9: Royal Blue
            new Color(1.00f, 0.75f, 0.20f, 1f), // 10: Gold / Amber
            new Color(1.00f, 0.40f, 0.45f, 1f), // 11: Coral
            new Color(0.20f, 0.95f, 0.65f, 1f), // 12: Mint
            new Color(0.65f, 0.40f, 1.00f, 1f), // 13: Lavender / Violet
            new Color(0.90f, 0.95f, 0.45f, 1f), // 14: Chartreuse
            new Color(0.00f, 0.65f, 0.65f, 1f), // 15: Teal
            new Color(0.95f, 0.50f, 0.70f, 1f), // 16: Rose
        };

        public static Color GetExtraPlayerColor(PlayerRef playerRef, int slotIndex)
        {
            int idx = slotIndex >= 4 ? (slotIndex - 4) : Math.Abs(playerRef.PlayerId);
            return ExtraPlayerColors[idx % ExtraPlayerColors.Length];
        }

        /// <summary>
        /// Returns a deterministic 16-element array of PlayerRefs:
        /// - Indices 0..3 match OverlandSessionPlayers.Instance.OrderedPlayerRefs[0..3].
        /// - Indices 4..15 contain any additional active players in ascending PlayerId order.
        /// </summary>
        public static PlayerRef[] GetOrderedPlayerSlots()
        {
            PlayerRef[] slots = new PlayerRef[MaxExpandedPlayers];
            for (int i = 0; i < MaxExpandedPlayers; i++)
            {
                slots[i] = PlayerRef.None;
            }

            var sessionPlayers = OverlandSessionPlayers.Instance;
            if (sessionPlayers == null || sessionPlayers.Object == null)
                return slots;

            try
            {
                var ordered = sessionPlayers.OrderedPlayerRefs;
                int baseLen = Math.Min(ordered.Length, 4);
                for (int i = 0; i < baseLen; i++)
                {
                    slots[i] = ordered[i];
                }
            }
            catch
            {
                // Ignore if network state is not yet ready
            }

            var runnerInstance = OverlandNetworkRunner.Instance;
            var netRunner = runnerInstance != null ? runnerInstance.NetworkRunner : null;
            var mapNet = MapNetworkManager.Instance;
            PlayerRef localPlayer = OverlandNetworkRunner.LocalPlayerRef;

            int nextSlot = 4;
            // Scan candidate PlayerRefs by index (0..31) in deterministic order
            for (int idx = 0; idx < 32 && nextSlot < MaxExpandedPlayers; idx++)
            {
                PlayerRef candidate = PlayerRef.FromIndex(idx);
                if (candidate == PlayerRef.None)
                    continue;

                // Skip if already in slots 0..3
                bool alreadyAssigned = false;
                for (int j = 0; j < 4; j++)
                {
                    if (slots[j] == candidate)
                    {
                        alreadyAssigned = true;
                        break;
                    }
                }
                if (alreadyAssigned)
                    continue;

                bool isActive = false;

                // Check 1: Is it the local player?
                if (localPlayer != PlayerRef.None && candidate == localPlayer)
                {
                    isActive = true;
                }

                // Check 2: Does NetworkRunner have a spawned player object for this PlayerRef?
                if (!isActive && netRunner != null)
                {
                    try
                    {
                        if (netRunner.TryGetPlayerObject(candidate, out NetworkObject playerObj) && playerObj != null)
                        {
                            isActive = true;
                        }
                    }
                    catch { }
                }

                // Check 3: Is this player in OverlandSessionPlayers.PlayerRefToNameDictionary?
                if (!isActive)
                {
                    try
                    {
                        if (sessionPlayers.PlayerRefToNameDictionary.ContainsKey(candidate.PlayerId))
                        {
                            isActive = true;
                        }
                    }
                    catch { }
                }

                // Check 4: Does MapNetworkManager have a pinpoint for this player?
                if (!isActive && mapNet != null && mapNet.Object != null)
                {
                    try
                    {
                        if (mapNet.PlayerRefToPinpointPosition.ContainsKey(candidate))
                        {
                            isActive = true;
                        }
                    }
                    catch { }
                }

                if (isActive)
                {
                    slots[nextSlot++] = candidate;
                }
            }

            return slots;
        }

        public static int GetPlayerSlotIndex(PlayerRef playerRef)
        {
            if (playerRef == PlayerRef.None)
                return -1;

            PlayerRef[] slots = GetOrderedPlayerSlots();
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == playerRef)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Dynamically clones MapView's playerIndicators and pinpointIndicators from 4 up to 16,
        /// and keeps pinpointIndicatorsRectTransforms synchronized with all 16 PinpointMapIcons.
        /// </summary>
        public static void EnsureExpandedMapIndicators(MapView mapView)
        {
            if (mapView == null)
                return;

            try
            {
                // 1. Expand playerIndicators (List<MapPlayerImage>) to 16
                var playerIndicators = mapView.playerIndicators;
                if (playerIndicators != null && playerIndicators.Count > 0 && playerIndicators.Count < MaxExpandedPlayers)
                {
                    MapPlayerImage template = playerIndicators[0];
                    if (template != null)
                    {
                        GameObject templateRoot = template.MovingParent != null ? template.MovingParent.gameObject : template.gameObject;
                        Transform parentTransform = templateRoot.transform.parent;

                        while (playerIndicators.Count < MaxExpandedPlayers)
                        {
                            int newIdx = playerIndicators.Count;
                            GameObject cloneObj = UnityEngine.Object.Instantiate(templateRoot, parentTransform);
                            cloneObj.name = $"{templateRoot.name}_Expanded_{newIdx + 1}";
                            cloneObj.SetActive(false);

                            MapPlayerImage cloneImg = cloneObj.GetComponentInChildren<MapPlayerImage>(true);
                            if (cloneImg != null)
                            {
                                playerIndicators.Add(cloneImg);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }

                // 2. Expand pinpointIndicators (List<PinpointMapIcon>) to 16
                var pinpointIndicators = mapView.pinpointIndicators;
                bool clonedPinpoints = false;
                if (pinpointIndicators != null && pinpointIndicators.Count > 0 && pinpointIndicators.Count < MaxExpandedPlayers)
                {
                    PinpointMapIcon pinTemplate = pinpointIndicators[0];
                    if (pinTemplate != null)
                    {
                        GameObject templateObj = pinTemplate.gameObject;
                        Transform parentTransform = templateObj.transform.parent;

                        while (pinpointIndicators.Count < MaxExpandedPlayers)
                        {
                            int newIdx = pinpointIndicators.Count;
                            GameObject cloneObj = UnityEngine.Object.Instantiate(templateObj, parentTransform);
                            cloneObj.name = $"{templateObj.name}_Expanded_{newIdx + 1}";
                            cloneObj.SetActive(false);

                            PinpointMapIcon clonePin = cloneObj.GetComponent<PinpointMapIcon>();
                            if (clonePin != null)
                            {
                                pinpointIndicators.Add(clonePin);
                                clonedPinpoints = true;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }

                // 3. Ensure pinpointIndicatorsRectTransforms has all 16 RectTransforms
                // (Note: MapView.DelayedEnable recreates pinpointIndicatorsRectTransforms with only 4 items on each OnEnable)
                var rectTransforms = mapView.pinpointIndicatorsRectTransforms;
                if (rectTransforms != null && pinpointIndicators != null && rectTransforms.Count < pinpointIndicators.Count)
                {
                    for (int i = rectTransforms.Count; i < pinpointIndicators.Count; i++)
                    {
                        PinpointMapIcon pin = pinpointIndicators[i];
                        RectTransform rt = pin != null ? pin.GetComponent<RectTransform>() : null;
                        rectTransforms.Add(rt);
                    }
                }

                // 4. Rebuild cursor snap targets if new pinpoint icons were instantiated
                if (clonedPinpoints && mapView.mapCursorSnapper != null)
                {
                    mapView.mapCursorSnapper.RebuildTargets();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in EnsureExpandedMapIndicators: {ex}");
            }
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
                    int slotIdx = ExpandedPlayerHelper.GetPlayerSlotIndex(playerRef);
                    if (slotIdx < 0)
                    {
                        slotIdx = Math.Abs(playerRef.PlayerId);
                    }
                    spawnPointIndex = slotIdx % points.Count;
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
    /// Assigns distinct colors to players 5..16 who are not in the 4-element OverlandSessionPlayers.PlayerColors array.
    /// </summary>
    [HarmonyPatch(typeof(UIManager), nameof(UIManager.GetPlayerColor))]
    public static class UIManager_GetPlayerColor_Patch
    {
        public static void Postfix(PlayerRef playerRef, ref Color __result)
        {
            try
            {
                if (playerRef == PlayerRef.None)
                    return;

                var sessionPlayers = OverlandSessionPlayers.Instance;
                if (sessionPlayers != null && sessionPlayers.Object != null)
                {
                    var colors = sessionPlayers.PlayerColors;
                    bool inBaseColors = false;
                    int len = Math.Min(colors.Length, 4);
                    for (int i = 0; i < len; i++)
                    {
                        if (colors[i] == playerRef)
                        {
                            inBaseColors = true;
                            break;
                        }
                    }

                    if (!inBaseColors)
                    {
                        int slotIdx = ExpandedPlayerHelper.GetPlayerSlotIndex(playerRef);
                        __result = ExpandedPlayerHelper.GetExtraPlayerColor(playerRef, slotIdx);
                    }
                }
            }
            catch
            {
                // Ignore if network state is not ready
            }
        }
    }

    /// <summary>
    /// Updates MapView player names and pinpoint icon colors/labels for up to 16 players.
    /// </summary>
    [HarmonyPatch(typeof(MapView), nameof(MapView.SetupPlayerNames))]
    public static class MapView_SetupPlayerNames_Patch
    {
        public static bool Prefix(MapView __instance)
        {
            try
            {
                if (__instance == null || OverlandSessionPlayers.Instance == null || OverlandSessionPlayers.Instance.Object == null)
                    return false;

                ExpandedPlayerHelper.EnsureExpandedMapIndicators(__instance);

                PlayerRef[] slots = ExpandedPlayerHelper.GetOrderedPlayerSlots();
                var playerIndicators = __instance.playerIndicators;
                var pinpointIndicators = __instance.pinpointIndicators;
                if (playerIndicators == null || pinpointIndicators == null)
                    return false;

                int count = Math.Min(slots.Length, Math.Min(playerIndicators.Count, pinpointIndicators.Count));
                for (int i = 0; i < count; i++)
                {
                    PlayerRef playerRef = slots[i];
                    MapPlayerImage playerImg = playerIndicators[i];
                    PinpointMapIcon pinIcon = pinpointIndicators[i];

                    if (playerRef != PlayerRef.None)
                    {
                        string playerName = OverlandSessionPlayers.Instance.GetPlayerName(playerRef);
                        bool ownsSupporter = OverlandSessionPlayers.Instance.OwnsSupporterEdition(playerRef);
                        bool isDev = OverlandSessionPlayers.Instance.IsDevTeamMember(playerRef);
                        Color playerColor = UIManager.GetPlayerColor(playerRef);

                        if (playerImg != null)
                        {
                            playerImg.SetPlayerName(playerName);
                            playerImg.SetPlayerBadges(ownsSupporter, isDev);
                            playerImg.ShowPlayerName(!string.IsNullOrEmpty(playerName));
                            playerImg.SetInnerColor(playerColor);
                            playerImg.SetActive(true);
                        }

                        if (pinIcon != null)
                        {
                            pinIcon.SetColor(playerColor);
                            pinIcon.SetLabelText(playerName);
                        }
                    }
                    else
                    {
                        if (playerImg != null)
                        {
                            playerImg.SetActive(false);
                        }
                    }
                }

                return false; // Skip vanilla 4-player loop
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in MapView.SetupPlayerNames patch: {ex}");
                return false;
            }
        }
    }

    /// <summary>
    /// Updates MapView player position & rotation indicators every frame for up to 16 players.
    /// </summary>
    [HarmonyPatch(typeof(MapView), nameof(MapView.UpdatePlayerIndicators))]
    public static class MapView_UpdatePlayerIndicators_Patch
    {
        public static bool Prefix(MapView __instance)
        {
            try
            {
                if (__instance == null || OverlandSessionPlayers.Instance == null || OverlandSessionPlayers.Instance.Object == null)
                    return false;

                var runner = OverlandNetworkRunner.Instance;
                if (runner == null)
                    return false;

                ExpandedPlayerHelper.EnsureExpandedMapIndicators(__instance);

                PlayerRef[] slots = ExpandedPlayerHelper.GetOrderedPlayerSlots();
                var playerIndicators = __instance.playerIndicators;
                if (playerIndicators == null)
                    return false;

                int count = Math.Min(slots.Length, playerIndicators.Count);
                for (int i = 0; i < count; i++)
                {
                    PlayerRef playerRef = slots[i];
                    MapPlayerImage indicator = playerIndicators[i];
                    if (indicator == null)
                        continue;

                    if (playerRef != PlayerRef.None &&
                        OverlandSessionPlayers.Instance.SpawnedInMap(playerRef) &&
                        runner.GetPlayerPositionAndRotation(playerRef, out Vector3 pos, out Quaternion rot))
                    {
                        indicator.SetActive(true);
                        if (indicator.RotatingParent != null)
                        {
                            indicator.RotatingParent.rotation = Quaternion.Euler(0f, 0f, -rot.eulerAngles.y);
                        }
                        if (indicator.MovingParent != null)
                        {
                            indicator.MovingParent.anchoredPosition = __instance.WorldToUIPosition(pos);
                        }
                    }
                    else
                    {
                        indicator.SetActive(false);
                    }
                }

                return false; // Skip vanilla 4-player loop
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in MapView.UpdatePlayerIndicators patch: {ex}");
                return false;
            }
        }
    }

    /// <summary>
    /// Updates MapView player name zoom scale for up to 16 players.
    /// </summary>
    [HarmonyPatch(typeof(MapView), nameof(MapView.UpdatePlayersNameZoom))]
    public static class MapView_UpdatePlayersNameZoom_Patch
    {
        public static bool Prefix(MapView __instance, float currentZoom)
        {
            try
            {
                if (__instance == null || OverlandSessionPlayers.Instance == null || OverlandSessionPlayers.Instance.Object == null)
                    return false;

                ExpandedPlayerHelper.EnsureExpandedMapIndicators(__instance);

                PlayerRef[] slots = ExpandedPlayerHelper.GetOrderedPlayerSlots();
                var playerIndicators = __instance.playerIndicators;
                if (playerIndicators == null)
                    return false;

                Vector3 scale = Vector3.one * (0.7f / currentZoom);
                int count = Math.Min(slots.Length, playerIndicators.Count);
                for (int i = 0; i < count; i++)
                {
                    if (slots[i] != PlayerRef.None && playerIndicators[i] != null)
                    {
                        playerIndicators[i].SetPlayerNameScale(scale);
                    }
                }

                return false; // Skip vanilla 4-player loop
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in MapView.UpdatePlayersNameZoom patch: {ex}");
                return false;
            }
        }
    }

    /// <summary>
    /// Updates MapView pinpoint (ping) icons for up to 16 players.
    /// </summary>
    [HarmonyPatch(typeof(MapView), nameof(MapView.UpdatePlayersPinpoints))]
    public static class MapView_UpdatePlayersPinpoints_Patch
    {
        public static bool Prefix(MapView __instance)
        {
            try
            {
                if (__instance == null || OverlandSessionPlayers.Instance == null || OverlandSessionPlayers.Instance.Object == null)
                    return false;

                ExpandedPlayerHelper.EnsureExpandedMapIndicators(__instance);
                __instance.SetupPlayerNames();

                PlayerRef[] slots = ExpandedPlayerHelper.GetOrderedPlayerSlots();
                var rectTransforms = __instance.pinpointIndicatorsRectTransforms;
                if (rectTransforms == null)
                    return false;

                int count = Math.Min(slots.Length, rectTransforms.Count);
                for (int i = 0; i < count; i++)
                {
                    __instance.UpdatePlayerPinpoint(slots[i], i);
                }

                return false; // Skip vanilla 4-player loop
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in MapView.UpdatePlayersPinpoints patch: {ex}");
                return false;
            }
        }
    }

    /// <summary>
    /// Fixes MapView.IsPlayerPinpoint for 5th-16th players so clicking on their own pinpoint removes it.
    /// </summary>
    [HarmonyPatch(typeof(MapView), nameof(MapView.IsPlayerPinpoint))]
    public static class MapView_IsPlayerPinpoint_Patch
    {
        public static bool Prefix(MapView __instance, PlayerRef playerRef, PinpointMapIcon pinpointIcon, ref bool __result)
        {
            try
            {
                if (__instance == null || pinpointIcon == null || playerRef == PlayerRef.None)
                {
                    __result = false;
                    return false;
                }

                ExpandedPlayerHelper.EnsureExpandedMapIndicators(__instance);

                int slotIdx = ExpandedPlayerHelper.GetPlayerSlotIndex(playerRef);
                var pinpointIndicators = __instance.pinpointIndicators;
                int iconIdx = pinpointIndicators != null ? pinpointIndicators.IndexOf(pinpointIcon) : -1;

                __result = (slotIdx >= 0 && slotIdx == iconIdx);
                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in MapView.IsPlayerPinpoint patch: {ex}");
                __result = false;
                return false;
            }
        }
    }

    /// <summary>
    /// Ensures 5th-16th players' cars also appear on the HUD Compass bar.
    /// </summary>
    [HarmonyPatch(typeof(CompassController), nameof(CompassController.UpdateOtherCarsCompassIcon))]
    public static class CompassController_UpdateOtherCarsCompassIcon_Patch
    {
        public static void Postfix(CompassController __instance)
        {
            try
            {
                if (__instance == null || OverlandSessionPlayers.Instance == null || OverlandSessionPlayers.Instance.Object == null)
                    return;

                var runner = OverlandNetworkRunner.Instance;
                if (runner == null || runner.NetworkRunner == null)
                    return;

                var trackedCars = __instance.trackedCars;
                if (trackedCars == null)
                    return;

                PlayerRef[] slots = ExpandedPlayerHelper.GetOrderedPlayerSlots();
                PlayerRef localPlayer = OverlandNetworkRunner.LocalPlayerRef;

                // Slots 0..3 are handled by vanilla UpdateOtherCarsCompassIcon; check slots 4..15
                for (int i = 4; i < slots.Length; i++)
                {
                    PlayerRef playerRef = slots[i];
                    if (playerRef == PlayerRef.None || playerRef == localPlayer)
                        continue;

                    if (!OverlandSessionPlayers.Instance.SpawnedInMap(playerRef))
                        continue;

                    if (!runner.NetworkRunner.TryGetPlayerObject(playerRef, out NetworkObject playerObj) || playerObj == null)
                        continue;

                    Transform targetTransform = playerObj.transform;
                    if (targetTransform == null)
                        continue;

                    bool alreadyTracked = false;
                    for (int j = 0; j < trackedCars.Count; j++)
                    {
                        CompassIcon existing = trackedCars[j];
                        if (existing != null && existing.Target == targetTransform)
                        {
                            alreadyTracked = true;
                            break;
                        }
                    }

                    if (alreadyTracked)
                        continue;

                    // Obtain car compass sprite from an existing tracked car if available
                    Sprite carSprite = null;
                    for (int j = 0; j < trackedCars.Count; j++)
                    {
                        if (trackedCars[j]?.image?.sprite != null)
                        {
                            carSprite = trackedCars[j].image.sprite;
                            break;
                        }
                    }

                    if (carSprite != null)
                    {
                        Color playerColor = UIManager.GetPlayerColor(playerRef);
                        CompassIcon newIcon = __instance.RegisterPersistentIcon(
                            targetTransform,
                            carSprite,
                            playerColor,
                            Color.clear,
                            CompassIcon.Properties.None);

                        if (newIcon != null)
                        {
                            newIcon.SetProperties((CompassIcon.Properties)3); // HasPriority | SkipCulling
                            trackedCars.Add(newIcon);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ExpandedLobbies] Error in CompassController.UpdateOtherCarsCompassIcon patch: {ex}");
            }
        }
    }

    /// <summary>
    /// On the Host, MapNetworkManager.PlayerRefToPinpointPosition has a weaved prime capacity of 7 (max 6 simultaneous pinpoints).
    /// If 6 pinpoints are already active and a 7th player places a pinpoint, remove the oldest pinpoint first so insertion succeeds.
    /// </summary>
    [HarmonyPatch(typeof(MapNetworkManager), nameof(MapNetworkManager.RPC_InstantiatePinpoint))]
    public static class MapNetworkManager_RPC_InstantiatePinpoint_Patch
    {
        public static void Prefix(MapNetworkManager __instance, PlayerRef playerRef)
        {
            try
            {
                if (__instance == null || __instance.Object == null || !__instance.HasStateAuthority)
                    return;

                var posDict = __instance.PlayerRefToPinpointPosition;
                if (posDict.Count >= 6 && !posDict.ContainsKey(playerRef))
                {
                    PlayerRef[] slots = ExpandedPlayerHelper.GetOrderedPlayerSlots();
                    for (int i = 0; i < slots.Length; i++)
                    {
                        PlayerRef candidate = slots[i];
                        if (candidate != PlayerRef.None && candidate != playerRef && posDict.ContainsKey(candidate))
                        {
                            __instance.RPC_DestroyPinpoint(candidate);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ExpandedLobbies] Notice in RPC_InstantiatePinpoint prefix: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Guards OverlandSessionPlayers.AddPlayer against IndexOutOfRangeException when 5+ players connect
    /// (since OrderedPlayerRefs and PlayerColors have fixed capacity 4), and ensures RPC_ShowPlayerJoinedNotification
    /// and RPC_ConnectedPlayer are still invoked for 5th-16th players!
    /// </summary>
    [HarmonyPatch(typeof(OverlandSessionPlayers), nameof(OverlandSessionPlayers.AddPlayer))]
    public static class OverlandSessionPlayers_AddPlayer_Patch
    {
        public static void Prefix(PlayerRef playerRef, string runtimePlayerPlayerName)
        {
            if (playerRef != PlayerRef.None && !string.IsNullOrEmpty(runtimePlayerPlayerName))
            {
                ExpandedPlayerHelper.CachedPlayerNames[playerRef.RawEncoded] = runtimePlayerPlayerName;
            }
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, OverlandSessionPlayers __instance, PlayerRef playerRef, string runtimePlayerPlayerName)
        {
            if (__exception != null)
            {
                MelonLogger.Msg($"[ExpandedLobbies] Completing AddPlayer for 5th+ player '{runtimePlayerPlayerName}' (ID: {playerRef.RawEncoded}).");
                try
                {
                    if (__instance != null && __instance.Object != null)
                    {
                        __instance.RPC_ShowPlayerJoinedNotification(playerRef);
                        __instance.RPC_ConnectedPlayer(playerRef);
                    }
                }
                catch (Exception innerEx)
                {
                    MelonLogger.Warning($"[ExpandedLobbies] Notice during post-AddPlayer RPCs: {innerEx.Message}");
                }
                return null; // Suppress IndexOutOfRangeException from OrderedPlayerRefs[-1]
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
    /// Guards RPC_ConnectedPlayer and triggers PlayerDataUpdated so MapView refreshes player names/icons for 5th+ players.
    /// </summary>
    [HarmonyPatch(typeof(OverlandSessionPlayers), nameof(OverlandSessionPlayers.RPC_ConnectedPlayer))]
    public static class OverlandSessionPlayers_RPC_ConnectedPlayer_Patch
    {
        public static void Postfix()
        {
            try
            {
                OverlandSessionPlayers.InvokePlayerDataUpdated();
            }
            catch { }
        }

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
    /// Triggers PlayerDataUpdated when any player disconnects so MapView refreshes icons for 5th+ players.
    /// </summary>
    [HarmonyPatch(typeof(OverlandSessionPlayers), nameof(OverlandSessionPlayers.RPC_DisconnectedPlayer))]
    public static class OverlandSessionPlayers_RPC_DisconnectedPlayer_Patch
    {
        public static void Postfix()
        {
            try
            {
                OverlandSessionPlayers.InvokePlayerDataUpdated();
            }
            catch { }
        }
    }

    /// <summary>
    /// Ensures GetPlayerCount counts all connected players (up to 16) rather than only the 4 in OrderedPlayerRefs.
    /// </summary>
    [HarmonyPatch(typeof(OverlandSessionPlayers), nameof(OverlandSessionPlayers.GetPlayerCount))]
    public static class OverlandSessionPlayers_GetPlayerCount_Patch
    {
        public static void Postfix(ref int __result)
        {
            try
            {
                PlayerRef[] slots = ExpandedPlayerHelper.GetOrderedPlayerSlots();
                int count = 0;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != PlayerRef.None)
                        count++;
                }
                if (count > __result)
                {
                    __result = count;
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Provides fallback player names if PlayerRefToNameDictionary is full (7th+ players) or lookup fails.
    /// </summary>
    [HarmonyPatch(typeof(OverlandSessionPlayers), nameof(OverlandSessionPlayers.GetPlayerName))]
    public static class OverlandSessionPlayers_GetPlayerName_Patch
    {
        public static void Postfix(PlayerRef playerRef, ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result) && playerRef != PlayerRef.None)
                {
                    if (ExpandedPlayerHelper.CachedPlayerNames.TryGetValue(playerRef.RawEncoded, out string cached) && !string.IsNullOrEmpty(cached))
                    {
                        __result = cached;
                        return;
                    }

                    var runner = OverlandNetworkRunner.Instance;
                    if (runner?.spawnedCharactersInfo != null &&
                        runner.spawnedCharactersInfo.TryGetValue(playerRef, out RuntimePlayer rtPlayer) &&
                        rtPlayer != null && !string.IsNullOrEmpty(rtPlayer.PlayerName))
                    {
                        __result = rtPlayer.PlayerName;
                        return;
                    }

                    __result = $"Player {playerRef.PlayerId + 1}";
                }
            }
            catch { }
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, PlayerRef playerRef, ref string __result)
        {
            if (__exception != null)
            {
                __result = $"Player {playerRef.PlayerId + 1}";
                return null;
            }
            return null;
        }
    }
}
