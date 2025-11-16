using System;
using BepInEx.Configuration;
using SpinCore.Translation;
using SpinCore.UI;
using UnityEngine;

namespace SpinShareUpdater;

public partial class Plugin
{
    private const string TRANSLATION_PREFIX = $"{nameof(SpinShareUpdater)}_";
    
    internal static ConfigEntry<bool> DeleteOldMapFiles = null!;

    private void RegisterConfigEntries()
    {
        TranslationHelper.AddTranslation($"{TRANSLATION_PREFIX}Name", nameof(SpinShareUpdater));
        TranslationHelper.AddTranslation($"{TRANSLATION_PREFIX}GitHubButtonText", $"{nameof(SpinShareUpdater)} Releases (GitHub)");
        
        DeleteOldMapFiles = Config.Bind("General", "DeleteOldMapFiles", false,
            "Delete old map files when downloading updated maps");
        TranslationHelper.AddTranslation($"{TRANSLATION_PREFIX}DeleteOldMapFiles", "Delete old map files when downloading updated maps");
    }

    private static void CreateModPage()
    {
        CustomPage rootModPage = UIHelper.CreateCustomPage("ModSettings");
        rootModPage.OnPageLoad += RootModPageOnOnPageLoad;
        
        UIHelper.RegisterMenuInModSettingsRoot($"{TRANSLATION_PREFIX}Name", rootModPage);
    }

    private static void RootModPageOnOnPageLoad(Transform rootModPageTransform)
    {
        CustomGroup modGroup = UIHelper.CreateGroup(rootModPageTransform, nameof(SpinShareUpdater));
        UIHelper.CreateSectionHeader(modGroup, "ModGroupHeader", $"{TRANSLATION_PREFIX}Name", false);
        
        #region DeleteOldMapFiles
        CustomGroup deleteOldMapFilesGroup = UIHelper.CreateGroup(modGroup, "DeleteOldMapFilesGroup");
        deleteOldMapFilesGroup.LayoutDirection = Axis.Horizontal;
        UIHelper.CreateSmallToggle(deleteOldMapFilesGroup, nameof(DeleteOldMapFiles),
            $"{TRANSLATION_PREFIX}DeleteOldMapFiles", DeleteOldMapFiles.Value, value =>
            {
                DeleteOldMapFiles.Value = value;
            });
        #endregion

        UIHelper.CreateButton(modGroup, $"Open{nameof(SpinShareUpdater)}RepositoryButton", $"{TRANSLATION_PREFIX}GitHubButtonText", () =>
        {
            Application.OpenURL($"https://github.com/TheBlackParrot/{nameof(SpinShareUpdater)}/releases/latest");
        });
    }
}