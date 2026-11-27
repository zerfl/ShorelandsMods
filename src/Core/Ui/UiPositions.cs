using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShorelandsMods.Core.Areas;

namespace ShorelandsMods.Core.Ui
{
    // Where the player dragged a mod's HUD widgets and windows: mods_data/<id>/ui.json, kept by the host (the mod cannot
    // read it). HUD positions are inward distances from the widget's anchor; window positions are the offset of the
    // window's top-left corner from the canvas centre.
    internal sealed class UiPositions
    {
        readonly string root,
            file;
        JObject data;

        public UiPositions(string modDataDir)
        {
            root = Path.GetDirectoryName(Path.GetFullPath(modDataDir));
            file = Path.Combine(modDataDir, "ui.json");
        }

        JObject Data
        {
            get
            {
                if (data != null)
                    return data;
                string text = File.Exists(file) ? ModFiles.ReadText(root, file, Limits.DataFileBytes, out _) : null;
                try
                {
                    data = text != null ? JObject.Parse(text) : new JObject();
                }
                catch (JsonException)
                {
                    data = new JObject();
                }
                return data;
            }
        }

        public bool TryGet(string kind, string id, out int x, out int y)
        {
            x = y = 0;
            if (!(Data[kind] is JObject group) || !(group[id] is JArray a) || a.Count != 2)
                return false;
            if (a[0].Type != JTokenType.Integer || a[1].Type != JTokenType.Integer)
                return false;
            x = (int)a[0];
            y = (int)a[1];
            return true;
        }

        public void Set(string kind, string id, int x, int y)
        {
            if (!(Data[kind] is JObject group))
                Data[kind] = group = new JObject();
            group[id] = new JArray(x, y);
            Save();
        }

        public void Forget(string kind, string id)
        {
            if (Data[kind] is JObject group && group.Remove(id))
                Save();
        }

        // Not saved (kept for this session) when mods_data cannot be written or has a link in the way.
        void Save()
        {
            try
            {
                ModFiles.CheckWrite(root, file);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                AtomicFile.Write(file, Data.ToString(Formatting.Indented));
            }
            catch (Exception e) when (ModFiles.IsFileError(e)) { }
        }
    }
}
