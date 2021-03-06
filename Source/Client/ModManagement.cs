using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RestSharp;
using Steamworks;
using Verse;
using Verse.Steam;

namespace Multiplayer.Client
{
    public static class ModManagement
    {
        public static void UpdateModCompatibilityDb()
        {
            if (!MultiplayerMod.settings.showModCompatibility) {
                return;
            }

            Task.Run(() => {
                var client = new RestClient("https://bot.rimworldmultiplayer.com/mod-compatibility?version=1.1");
                try {
                    var rawResponse = client.Get(new RestRequest($"", DataFormat.Json));
                    Multiplayer.modsCompatibility = SimpleJson.DeserializeObject<Dictionary<string, int>>(rawResponse.Content);
                    Log.Message($"MP: successfully fetched {Multiplayer.modsCompatibility.Count} mods compatibility info");
                }
                catch (Exception e) {
                    Log.Warning($"MP: updating mod compatibility list failed {e.Message} {e.StackTrace}");
                }
            });
        }

        public static List<ulong> GetEnabledWorkshopMods() {
            var enabledModIds = LoadedModManager.RunningModsListForReading.Select(m => m.PackageId).ToArray();
            var allWorkshopItems =
                WorkshopItems.AllSubscribedItems.Where<WorkshopItem>(
                    (Func<WorkshopItem, bool>) (it => it is WorkshopItem_Mod)
                );
            var workshopModIds = new List<ulong>();
            foreach (WorkshopItem workshopItem in allWorkshopItems) {
                ModMetaData mod = new ModMetaData(workshopItem);

                if (enabledModIds.Contains(mod.PackageIdNonUnique)) {
                    workshopModIds.Add(workshopItem.PublishedFileId.m_PublishedFileId);
                }
            }

            return workshopModIds;
        }

        public static void DownloadWorkshopMods(ulong[] workshopModIds) {
            try {
                var downloadInProgress = new List<PublishedFileId_t>();
                foreach (var workshopModId in workshopModIds) {
                    var publishedFileId = new PublishedFileId_t(workshopModId);
                    var itemState = (EItemState) SteamUGC.GetItemState(publishedFileId);
                    if (!itemState.HasFlag(EItemState.k_EItemStateInstalled | EItemState.k_EItemStateSubscribed)) {
                        Log.Message($"Starting workshop download {publishedFileId}");
                        SteamUGC.SubscribeItem(publishedFileId);
                        downloadInProgress.Add(publishedFileId);
                    }
                }

                // wait for all workshop downloads to complete
                while (downloadInProgress.Count > 0) {
                    var publishedFileId = downloadInProgress.First();
                    var itemState = (EItemState) SteamUGC.GetItemState(publishedFileId);
                    if (itemState.HasFlag(EItemState.k_EItemStateInstalled | EItemState.k_EItemStateSubscribed)) {
                        downloadInProgress.RemoveAt(0);
                    }
                    else {
                        Log.Message($"Waiting for workshop download {publishedFileId} status {itemState}");
                        Thread.Sleep(200);
                    }
                }
            }
            catch (InvalidOperationException e) {
                Log.Error($"MP Workshop mod sync error: {e.Message}");
            }
        }

        /// Calls the private <see cref="WorkshopItems.RebuildItemsList"/>) to manually detect newly downloaded Workshop mods
        public static void RebuildModsList() {
            // ReSharper disable once PossibleNullReferenceException
            typeof(WorkshopItems)
                .GetMethod("RebuildItemsList", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(obj: null, parameters: new object[] { });
        }

        /// Extension of <see cref="ModsConfig.RestartFromChangedMods"/>) with -connect
        public static void PromptRestartAndReconnect(string address, int port)
        {
            // todo: clear -connect after launching? hmm, or patch ModsConfig.Restart to exclude it?
            // todo: if launched normally, prompt to return to backed up configs (and enabled mods?)
            Find.WindowStack.Add((Window) new Dialog_MessageBox("ModsChanged".Translate(), (string) null,
                (Action) (() => {
                    string[] commandLineArgs = Environment.GetCommandLineArgs();
                    string processFilename = commandLineArgs[0];
                    string arguments = "";
                    for (int index = 1; index < commandLineArgs.Length; ++index) {
                        if (string.Compare(commandLineArgs[index], "-connect", true) == 0) {
                            continue; // skip any existing -connect command
                        }
                        if (!arguments.NullOrEmpty()) {
                            arguments += " ";
                        }
                        arguments += "\"" + commandLineArgs[index].Replace("\"", "\\\"") + "\"";
                    }
                    if (address != null) {
                        arguments += $" -connect={address}:{port}";
                    }

                    new Process()
                    {
                        StartInfo = new ProcessStartInfo(processFilename, arguments)
                    }.Start();
                    Root.Shutdown();
                    LongEventHandler.QueueLongEvent((Action) (() => Thread.Sleep(10000)), "Restarting", true, (Action<Exception>) null, true);
                }), (string) null, (Action) null, (string) null, false, (Action) null, (Action) null));
        }

        public static void PromptRestart()
        {
            PromptRestartAndReconnect(null, 0);
        }
    }
}
