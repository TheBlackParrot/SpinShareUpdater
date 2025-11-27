using System;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;

namespace SpinShareUpdater.Patches;

[HarmonyPatch]
internal class CheckSelectionListPatches
{
    private static string _lastUniqueName = string.Empty;
    internal static MetadataHandle? PreviousMetadataHandle;

    internal static CancellationTokenSource? PreviousTokenSource;
    
    [HarmonyPatch(typeof(XDSelectionListMenu), nameof(XDSelectionListMenu.UpdatePreviewHandle))]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void XDSelectionListMenu_UpdatePreviewHandlePatch(XDSelectionListMenu __instance)
    {
        if (__instance._previewTrackDataSetup.Item1 == null)
        {
            return;
        }
        if (_lastUniqueName == __instance._previewTrackDataSetup.Item1.UniqueName)
        {
            return;
        }
        
        PreviousMetadataHandle = __instance._previewTrackDataSetup.Item1;
        
        _lastUniqueName = __instance._previewTrackDataSetup.Item1.UniqueName;

        if (!_lastUniqueName.Contains("spinshare_"))
        {
            Plugin.UpdateButton?.SetActive(false);
            return;
        }

        Plugin.UpdateButton?.SetActive(true);
        
        CancellationTokenSource tokenSource = new();
        PreviousTokenSource?.Cancel();
        PreviousTokenSource = tokenSource;

        Task.Run(async () =>
        {
            try
            {
                await Plugin.CheckForMapUpdate(__instance._previewTrackDataSetup.Item1, tokenSource.Token);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }, tokenSource.Token);
    }
}