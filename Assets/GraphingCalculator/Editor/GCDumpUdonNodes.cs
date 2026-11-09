#if UNITY_EDITOR
// TEMPORARY development helper: dumps the Udon node registry (exposed externs) to Temp/udon_nodes.txt.
// Safe to delete.
using System.IO;
using System.Linq;
using UnityEditor;

namespace GraphCalc.EditorTools
{
    [InitializeOnLoad]
    static class GCDumpUdonNodes
    {
        static GCDumpUdonNodes()
        {
            EditorApplication.delayCall += Dump;
        }

        static void Dump()
        {
            const string path = "Temp/udon_nodes.txt";
            if (File.Exists(path)) return;
            try
            {
                var defs = VRC.Udon.Editor.UdonEditorManager.Instance.GetNodeDefinitions();
                File.WriteAllLines(path, defs.Select(d => d.fullName).ToArray());
            }
            catch (System.Exception e)
            {
                File.WriteAllText(path + ".err", e.ToString());
            }
        }
    }
}
#endif
