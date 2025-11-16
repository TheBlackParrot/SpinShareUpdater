using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SpinShareLib;
using SpinShareLib.Types;
using SpinShareUpdater.Patches;
using UnityEngine;
using XDMenuPlay.Customise;
using Image = UnityEngine.UI.Image;

namespace SpinShareUpdater;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log = null!;
    private static readonly Harmony HarmonyInstance = new(MyPluginInfo.PLUGIN_GUID);
    private static readonly SSAPI SpinShareAPI = new();

    private static string CustomsPath => CustomAssetLoadingHelper.CUSTOM_DATA_PATH;

    private static readonly Dictionary<string, DateTime> LastChecked = new();
    private static readonly Dictionary<string, IsUpdated> LastCheckedResult = new();

    private void Awake()
    {
        Log = Logger;

        RegisterConfigEntries();
        CreateModPage();
        
        Logger.LogInfo("Plugin loaded");
    }

    private void OnEnable()
    {
        HarmonyInstance.PatchAll();
        
        Task.Run(async () =>
        {
            try
            {
                await FindTheMutePreviewButton();
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        });
    }

    private void OnDisable()
    {
        HarmonyInstance.UnpatchSelf();
    }
    
    private static string GetFileReference(MetadataHandle metadataHandle)
    {
        string? reference = metadataHandle.UniqueName;
        if (string.IsNullOrEmpty(reference))
        {
            return reference;
        }
        
        if (reference.LastIndexOf('_') != -1)
        {
            reference = reference.Remove(metadataHandle.UniqueName.LastIndexOf('_')).Replace("CUSTOM_", string.Empty);
        }
        
        return reference;
    }

    private enum IsUpdated
    {
        OutOfDate = 0,
        UpToDate = 1,
        Loading = 2,
        Error = 3,
    }

    internal static GameObject? _updateButton;
    private static IsUpdated _isUpdated = IsUpdated.Loading;
    private static void SetUpdateStatus(IsUpdated status)
    {
        _isUpdated = status;
        string spriteName = _isUpdated switch
        {
            IsUpdated.OutOfDate => "Default",
            IsUpdated.UpToDate => "checkmark",
            IsUpdated.Loading => "LoadingSpinner",
            IsUpdated.Error => "Close X",
            _ => string.Empty
        };
        
        if (_updateButton != null)
        {
            _updateButton.transform.Find("IconContainer/Icon").GetComponent<Image>().sprite =
                Resources.FindObjectsOfTypeAll<Sprite>().First(x => x.name == spriteName);
        }

        if (CheckSelectionListPatches.PreviousMetadataHandle == null)
        {
            return;
        }
        
        // ReSharper disable once InvertIf
        if (_isUpdated is IsUpdated.OutOfDate or IsUpdated.UpToDate)
        {
            LastChecked[GetFileReference(CheckSelectionListPatches.PreviousMetadataHandle)] = DateTime.Now.AddSeconds(0);
            LastCheckedResult[GetFileReference(CheckSelectionListPatches.PreviousMetadataHandle)] = _isUpdated;
        }

        _updateButton!.GetComponent<XDNavigableButton>().interactable = _isUpdated is not IsUpdated.Loading;
    }
    
    private static async Task FindTheMutePreviewButton()
    {
        Transform? parentTransform = null;
        while (parentTransform == null)
        {
            await Awaitable.MainThreadAsync();

            try
            {
                parentTransform =
                    FindObjectsByType<XDSelectionListItemDisplay_Track>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                        .First(x => x.gameObject.name == "MainSelectionNavigation_PlayAndPreview(Clone)")?.transform;
            }
            catch (InvalidOperationException)
            {
                // ignored
            }

            await Awaitable.EndOfFrameAsync();
        }

        Transform? muteButtonTransform = null;
        while (muteButtonTransform == null)
        {
            await Awaitable.MainThreadAsync();
            muteButtonTransform = parentTransform.Find("TogglePreviewAudio");
            await Awaitable.EndOfFrameAsync();
        }
        
        _updateButton = Instantiate(muteButtonTransform.gameObject, parentTransform);
        _updateButton.name = "UpdateCheckerButton";
        _updateButton.transform.localPosition = _updateButton.transform.localPosition with { y = 85f };
        
        Destroy(_updateButton.GetComponent<ToggleMusicPreviewButton>());
        
        XDNavigableButton button = _updateButton.GetComponent<XDNavigableButton>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(async void () =>
        {
            try
            {
                await OnButtonPress();
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        });

        SetUpdateStatus(IsUpdated.Loading);
    }

    private static async Task OnButtonPress()
    {
        MetadataHandle? metadataHandle = CheckSelectionListPatches.PreviousMetadataHandle;
        if (metadataHandle == null || _updateButton == null)
        {
            return;
        }
        
        string fileReference = GetFileReference(metadataHandle);
        _updateButton.GetComponent<XDNavigableButton>().interactable = false;
        
        switch (_isUpdated)
        {
            case IsUpdated.Error:
            case IsUpdated.UpToDate:
                CancellationTokenSource tokenSource = new();
                CheckSelectionListPatches._previousTokenSource?.Cancel();
                CheckSelectionListPatches._previousTokenSource = tokenSource;
            
                await CheckForMapUpdate(metadataHandle, tokenSource.Token, true);
                break;
            
            case IsUpdated.OutOfDate:
                NotificationSystemGUI.AddMessage($"Updating map {fileReference}...", 5f);

                string srtbFilename = Path.Combine(CustomsPath, $"{fileReference}.srtb");
                string artFilename = Path.Combine(CustomsPath, $"AlbumArt/{fileReference}.png");
                long unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (File.Exists(srtbFilename))
                {
                    if (DeleteOldMapFiles.Value)
                    {
                        File.Delete(srtbFilename);
                    }
                    else
                    {
                        File.Move(srtbFilename,
                            Path.Combine(CustomsPath, $"{fileReference}old_{unixTimestamp}.srtb"));
                    }
                }
                if (File.Exists(artFilename))
                {
                    if (DeleteOldMapFiles.Value)
                    {
                        File.Delete(artFilename);
                    }
                    else
                    {
                        File.Move(artFilename,
                            Path.Combine(CustomsPath, $"AlbumArt/{fileReference}old_{unixTimestamp}.png"));
                    }
                }

                try
                {
                    await SpinShareAPI.downloadSongAndUnzip(fileReference, CustomsPath);
                    
                    LastChecked[fileReference] = DateTime.Now.AddSeconds(0);
                    LastCheckedResult[fileReference] = IsUpdated.UpToDate;
                    NotificationSystemGUI.AddMessage("Successfully updated map!");
                    
                    XDSelectionListMenu.Instance.FireRapidTrackDataChange();
                    await JumpToMap(fileReference);
                } catch (Exception e)
                {
                    Log.LogWarning(e);
                }
                break;

            case IsUpdated.Loading:
            default:
                break;
        }
        
        _updateButton.GetComponent<XDNavigableButton>().interactable = true;
    }

    internal static async Task CheckForMapUpdate(MetadataHandle metadataHandle, CancellationToken token = default, bool forced = false)
    {
        try
        {
            if (_updateButton == null)
            {
                return;
            }

            if (!metadataHandle.IsCustom)
            {
                SetUpdateStatus(IsUpdated.UpToDate);
                return;
            }

            if (metadataHandle.UniqueName.Contains("old_"))
            {
                SetUpdateStatus(IsUpdated.Error);
                return;
            }
            
            string fileReference = GetFileReference(metadataHandle);
            if (LastChecked.TryGetValue(fileReference, out DateTime previousCheckTime) && !forced)
            {
                if (DateTime.Now <= previousCheckTime.AddMinutes(30))
                {
                    SetUpdateStatus(LastCheckedResult[fileReference]);
                    return;
                }   
            }

            SetUpdateStatus(IsUpdated.Loading);

            // wait a bit so that we don't send needless update requests
            await Task.Delay(1000, token);
            
            Content<SongDetail> content;
            try
            {
                content = await SpinShareAPI.getSongDetail(fileReference);
            }
            catch (Exception e)
            {
                Log.LogWarning(e);
                SetUpdateStatus(IsUpdated.Error);
                return;
            }
            
            if (content.status != 200)
            {
                SetUpdateStatus(IsUpdated.Error);
                return;
            }
            SongDetail details = content.data;
            
            // details.uploadDate.stimezone is null (erm), but SpinShare stores time in Europe/Berlin
            // https://github.com/unicode-org/cldr/blob/59dfe3ad9720e304957658bd991df8b0dba3519a/common/supplemental/windowsZones.xml#L307
            DateTime updateDateTime;
            if (details.updateDate != null)
            {
                updateDateTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(details.updateDate.date, "W. Europe Standard Time", TimeZoneInfo.Local.Id);
            }
            else
            {
                // null values seem to indicate no updates were ever done
                SetUpdateStatus(IsUpdated.UpToDate);
                return;
            }
            
            string path = Path.Combine(CustomsPath, $"{fileReference}.srtb");
            SetUpdateStatus(File.GetLastWriteTime(path) >= updateDateTime ? IsUpdated.UpToDate : IsUpdated.OutOfDate);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // ignored
        }
        catch (Exception e)
        {
            Log.LogError(e);
        }
    }

    private static async Task JumpToMap(string fileReference)
    {
        int attempts = 0;
        MetadataHandle metadataHandle;
        
        keepTrying:
        try
        {
                
            metadataHandle = XDSelectionListMenu.Instance._sortedTrackList.First(handle =>
            {
                if (string.IsNullOrEmpty(handle.UniqueName))
                {
                    return false;
                }

                if (handle.UniqueName.Contains("old_"))
                {
                    return false;
                }
                    
                string reference = handle.UniqueName;
                if (reference.LastIndexOf('_') != -1)
                {
                    reference = reference.Remove(handle.UniqueName.LastIndexOf('_'));
                }

                return fileReference == reference.Replace("CUSTOM_", string.Empty);
            });
        }
        catch (Exception innerException)
        {
            if (innerException is not InvalidOperationException)
            {
                throw;
            }
                
            attempts++;
            if (attempts >= 12)
            {
                NotificationSystemGUI.AddMessage("Failed to find updated map (erm...)");
                throw;
            }
            
            await Task.Delay(250);
            goto keepTrying;
        }
        
        XDSelectionListMenu.Instance.ScrollToTrack(metadataHandle);
    }
}