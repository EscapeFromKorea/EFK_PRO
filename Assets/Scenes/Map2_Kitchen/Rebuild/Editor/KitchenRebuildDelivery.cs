using System;
using System.IO;
using UnityEngine;

namespace Map2KitchenDraft.Editor
{
    /// <summary>Builds only this isolated review scene and its review player.</summary>
    public static class KitchenRebuildDelivery
    {
        [Serializable] private sealed class DeliveryState
        {
            public string unityVersion;
            public string phase;
            public bool success;
            public string error;
        }

        public static void Run()
        {
            var state = new DeliveryState { unityVersion = Application.unityVersion };
            try
            {
                if (Application.unityVersion != "6000.3.28f1")
                    throw new InvalidOperationException("Exact approved Unity version required.");
                state.phase = "build scene";
                KitchenRebuildBuilder.BuildAndValidate();
                state.phase = "capture previews";
                KitchenRebuildBuilder.CapturePreviews();
                state.phase = "verify saved scene";
                KitchenRebuildVerification.Run();
                state.phase = "build Windows player";
                KitchenRebuildBuilder.BuildWindows();
                state.phase = "complete";
                state.success = true;
            }
            catch (Exception e)
            {
                state.error = e.ToString();
                throw;
            }
            finally
            {
                var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
                    "검증", "delivery_state.json"));
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output, JsonUtility.ToJson(state, true));
            }
        }
    }
}
