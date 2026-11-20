using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Colossal.PSI.Common;
using Game.Rendering;
using Game.UI.Menu;
using Unity.Mathematics;

namespace ClaudeCityBridge
{
    public partial class Api
    {
        /// <summary>POST /camera {x, z, zoom, yaw, pitch}: point the gameplay camera (for previews/thumbnails).</summary>
        object Camera(Args a)
        {
            var cam = world.GetExistingSystemManaged<CameraUpdateSystem>()?.gamePlayController;
            if (cam == null) return new ApiError("no gameplay camera", 409);
            if (a.Has("x"))
            {
                var p = new float3(a.F("x"), 0, a.F("z"));
                p.y = TerrainHeight(p);
                cam.pivot = new UnityEngine.Vector3(p.x, p.y, p.z);
            }
            if (a.Has("zoom")) cam.zoom = a.F("zoom");
            if (a.Has("yaw") || a.Has("pitch"))
            {
                var r = cam.rotation;
                cam.rotation = new UnityEngine.Vector3(a.F("pitch", r.x), a.F("yaw", r.y), r.z);
            }
            return new Dictionary<string, object>
            {
                { "pivot", new List<object> { Math.Round(cam.pivot.x), Math.Round(cam.pivot.y), Math.Round(cam.pivot.z) } },
                { "zoom", cam.zoom }, { "rotation", new List<object> { cam.rotation.x, cam.rotation.y, cam.rotation.z } },
            };
        }

        /// <summary>
        /// POST /share {saveId|saveName, name, shortDescription, longDescription, changelog, version,
        /// aiTags:[ids], update, submit}: open the game's own "share save" upload panel for a save,
        /// fill the Paradox Mods fields and (only with submit=true) publish. Without submit it
        /// reports what would be published, the available tags and any validation errors.
        /// </summary>
        object Share(Args a)
        {
            var list = SaveList().OrderByDescending(x => x.lastModified).ToList();
            var want = a.Str("saveId") ?? a.Str("saveName") ?? "Claude City";
            var save = list.FirstOrDefault(x => x.id == want) ?? list.FirstOrDefault(x => !x.autoSave && string.Equals(x.displayName, want, StringComparison.OrdinalIgnoreCase));
            if (save == null) return new ApiError("save not found: " + want);
            Menu.ShareSave(save.id);
            return new ShareOperation(this, a, save.displayName);
        }

        public AssetUploadPanelUISystem UploadPanel => world.GetExistingSystemManaged<AssetUploadPanelUISystem>();

        internal static object Field(object o, string name) => GetField(o, name);
        internal static object Call(object o, string name, params object[] args) => Invoke(o, name, args);
    }

    public class ShareOperation : IOperation
    {
        readonly Api api;
        readonly Args a;
        readonly string saveName;
        readonly int started = Environment.TickCount;
        int phase;
        Task submitTask;

        public ShareOperation(Api api, Args a, string saveName) { this.api = api; this.a = a; this.saveName = saveName; }

        public bool Step(out object result)
        {
            result = null;
            var panel = api.UploadPanel;
            var handle = Api.Field(panel, "m_UploadHandle") as PdxAssetUploadHandle;
            int ms = Environment.TickCount - started;
            if (phase == 0)
            {
                // wait until the panel has its handle and finished syncing with Paradox Mods
                var state = Api.Field(panel, "m_State")?.ToString();
                if (handle == null || state == null || state.Contains("Sync") || state.Contains("Loading"))
                {
                    if (ms > 60000) { result = new ApiError("upload panel did not become ready (state=" + state + ")", 504); return true; }
                    return false;
                }
                if (!handle.LoggedIn()) { result = new ApiError("not logged in to a Paradox account", 409); Close(api, panel); return true; }

                var info = handle.modInfo;
                if (a.Has("name")) info.m_DisplayName = a.Str("name");
                if (a.Has("shortDescription")) info.m_ShortDescription = a.Str("shortDescription");
                if (a.Has("longDescription")) info.m_LongDescription = a.Str("longDescription");
                if (a.Has("changelog")) info.m_Changelog = a.Str("changelog");
                if (a.Has("version")) info.m_UserModVersion = a.Str("version");
                if (a.B("update"))
                {
                    var existing = handle.authorMods?.FirstOrDefault(m => m.m_DisplayName == info.m_DisplayName);
                    if (existing != null && existing.Value.m_PublishedID != null) { info.m_PublishedID = existing.Value.m_PublishedID; handle.updateExisting = true; }
                }
                handle.modInfo = info;
                var aiTags = a.List("aiTags");
                if (aiTags != null) foreach (var t in aiTags) Api.Call(panel, "OnAddAiDisclosureTag", t.ToString());

                var report = new Dictionary<string, object>
                {
                    { "save", saveName }, { "state", Api.Field(panel, "m_State")?.ToString() },
                    { "name", handle.modInfo.m_DisplayName }, { "version", handle.modInfo.m_UserModVersion },
                    { "shortDescription", handle.modInfo.m_ShortDescription },
                    { "updateExisting", handle.updateExisting }, { "publishedId", handle.modInfo.m_PublishedID },
                    { "hasPreview", handle.preview != null },
                    { "aiDisclosureTagsAvailable", (handle.availableAiDisclosureTags ?? new IModsUploadSupport.ModTag[0]).Select(t => (object)(t.m_Id + " = " + t.m_DisplayName)).ToList() },
                    { "aiPolicyUrl", handle.aiPolicyUrl },
                    { "authorMods", (handle.authorMods ?? new List<IModsUploadSupport.ModInfo>()).Select(m => (object)(m.m_DisplayName + " #" + m.m_PublishedID)).ToList() },
                    { "errors", Errors(panel) },
                };
                if (!a.B("submit"))
                {
                    report["submitted"] = false;
                    if (!a.B("keepOpen")) Close(api, panel);
                    result = report;
                    return true;
                }
                if (((List<object>)report["errors"]).Count > 0) { report["submitted"] = false; result = report; return true; }
                submitTask = Api.Call(panel, "Submit") as Task;
                phase = 1;
                return false;
            }
            // phase 1: wait for the submit task
            if (submitTask != null && !submitTask.IsCompleted && ms < 600000) return false;
            result = new Dictionary<string, object>
            {
                { "submitted", submitTask != null && submitTask.IsCompleted && !submitTask.IsFaulted },
                { "error", submitTask?.Exception?.GetBaseException().Message },
                { "state", Api.Field(panel, "m_State")?.ToString() },
                { "publishedId", (Api.Field(panel, "m_UploadHandle") as PdxAssetUploadHandle)?.modInfo.m_PublishedID },
            };
            return true;
        }

        static List<object> Errors(AssetUploadPanelUISystem panel)
        {
            var errs = new List<object>();
            foreach (var n in new[] { "nameError", "shortDescriptionError", "longDescriptionError", "changelogError", "versionError", "existingModError", "noInternetConnection", "notLoggedIn", "thumbnailMissingError" })
            {
                try
                {
                    var v = panel.GetType().GetProperty(n, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(panel);
                    if (v is bool b && b) errs.Add(n);
                }
                catch { }
            }
            return errs;
        }

        static void Close(Api api, AssetUploadPanelUISystem panel)
        {
            try { panel.Close(); } catch { }
            try { api.World.GetExistingSystemManaged<StandaloneAssetUploadPanelUISystem>()?.Close(); } catch { }
        }
    }
}
