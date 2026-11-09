#if UNITY_EDITOR
// DEVELOPMENT HELPER (safe to delete): watches Temp/gc_cmd.txt for commands
//   test  - runs the expression test-suite in C# (edit mode) -> Temp/gc_out.txt
//   build - runs Tools > Graphing Calculator > Build Calculator In Scene
//   play / stop - enter / exit play mode
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GraphCalc.EditorTools
{
    [InitializeOnLoad]
    static class GCDevCommands
    {
        const string CmdPath = "Temp/gc_cmd.txt";
        const string OutPath = "Temp/gc_out.txt";

        static GCDevCommands()
        {
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (!File.Exists(CmdPath)) return;
            string cmd;
            try { cmd = File.ReadAllText(CmdPath).Trim(); File.Delete(CmdPath); }
            catch { return; }
            try
            {
                if (cmd == "test") File.WriteAllText(OutPath, RunTests());
                else if (cmd == "build") { GraphCalculatorBuilder.BuildInScene(); File.WriteAllText(OutPath, "built"); }
                else if (cmd == "play") { EditorApplication.isPlaying = true; File.WriteAllText(OutPath, "playing"); }
                else if (cmd == "stop") { EditorApplication.isPlaying = false; File.WriteAllText(OutPath, "stopped"); }
                else if (cmd.StartsWith("shot")) { Shot(cmd); File.WriteAllText(OutPath, "shot"); }
                else if (cmd.StartsWith("key ")) { PressKeys(cmd.Substring(4)); File.WriteAllText(OutPath, "keys"); }
            }
            catch (System.Exception e)
            {
                File.WriteAllText(OutPath, "EXCEPTION: " + e);
            }
        }

        // "shot" renders the whole calculator; "shot x y w" renders a region (root-local centre x,y and width w in metres)
        static void Shot(string cmd)
        {
            GameObject root = GameObject.Find("GraphingCalculator");
            if (root == null) return;
            string[] p = cmd.Split(' ');
            float cx = 0.02f, cy = 1.55f, w = 3.2f;
            if (p.Length >= 4) { cx = float.Parse(p[1]); cy = float.Parse(p[2]); w = float.Parse(p[3]); }
            int W = 1920, H = (int)(1920 * 0.62f);
            GameObject camGo = new GameObject("GC_ShotCam");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = w * 0.5f * H / W;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.gray;
            camGo.transform.position = root.transform.TransformPoint(new Vector3(cx, cy, -2f));
            camGo.transform.rotation = root.transform.rotation;
            RenderTexture rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new UnityEngine.Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            File.WriteAllBytes("Temp/gc_shot.png", tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGo);
        }

        // presses keys on the running calculator: "key i:x|i:^2|enter"
        static void PressKeys(string keys)
        {
            GraphCalculator calc = Object.FindObjectOfType<GraphCalculator>();
            if (calc == null) return;
            VRC.Udon.UdonBehaviour ub = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(calc);
            foreach (string k in keys.Split('|'))
            {
                ub.SetProgramVariable("devKey", k);
                ub.SendCustomEvent("DevPress");
            }
        }

        static GCCompiler comp;
        static GCEngine eng;

        static string RunTests()
        {
            GameObject go = new GameObject("GC_Test");
            go.hideFlags = HideFlags.HideAndDontSave;
            comp = go.AddComponent<GCCompiler>();
            eng = go.AddComponent<GCEngine>();
            eng.compiler = comp;
            comp.Init();
            eng.Init();
            StringBuilder sb = new StringBuilder();
            double t0 = EditorApplication.timeSinceStartup;
            try
            {
                Run(sb, false, false, "1+2", "2^10", "sin(π/2)", "-2^2", "2(3+4)", "1/2x", "√16", "3!", "mod(-1,3)", "(-8)^(1/3)", "e^{-1}");
                Run(sb, false, false, "a=3", "b=2a+1", "c=b^2", "d=[1,2,3]a");
                Run(sb, false, false, "f(x)=x^2+1", "f(3)", "f'(3)", "f''(3)", "g(x,y)=xy", "g(2,5)");
                Run(sb, false, false, "Σ_{n=1}^{10} n", "sum(n=1,100,n^2)", "Π_{n=1}^{5} n", "∫_0^1 x^2 dx", "∫_0^∞ e^{-x} dx", "∫_{-∞}^{∞} e^{-x^2} dx", "d/dx(x^3)");
                Run(sb, false, false, "[1,2,3]", "total([1,2,3])", "mean([1...10])", "L=[1,2,3]", "L^2", "L[2]", "length(L)", "median([5,1,3,2])", "stdev([2,4,4,4,5,5,7,9])", "[1,3,...,11]");
                Run(sb, false, false, "max(3,7,2)", "min(3,7,2)", "gcd(12,18)", "lcm(4,6)", "nCr(5,2)", "nPr(5,2)", "round(2.567,1)", "log_2(8)", "log(100)", "ln(e^2)", "arctan(1,1)", "floor(-2.5)", "abs(-3)", "sign(-2)", "erf(1)");
                Run(sb, false, false, "{1<2: 5, 6}", "{1>2: 5, 6}", "{1>2: 5}", "sin^2(π/4)", "cos^{-1}(0)", "sqrt(-1)", "1/0");
                Run(sb, true, false, "sqrt(-1)", "(1+2i)(3-i)", "e^(iπ)", "ln(-1)", "i^2", "abs(3+4i)", "arg(i)", "(1+i)/(1-i)", "z=2+3i", "real(z)", "conj(z)");
                Run(sb, false, true, "sin(90)", "arcsin(1)", "cos(180)");
                Run(sb, false, false, "y=x^2", "y=sin(x)", "x^2", "x=3", "y=2", "y<x", "y>=x^2-2", "y=tan(x)", "y=1/x", "y=sqrt(x)", "y=ax {x>0}", "a=2");
                Run(sb, false, false, "x^2+y^2=4", "x^2+y^2<9", "y^2=x", "sin(x)=cos(y)", "0<x<2");
                Run(sb, false, false, "r=1+cos(θ)", "(cos(t),sin(t))", "(cos(t),sin(t)) {0<=t<=π}", "(1,2)", "P=(3,4)", "P.x", "[(1,1),(2,4),(3,9)]", "r<2");
                Run(sb, false, false, "y=[1,2,3]x", "y=x^L", "L=[1,2,3]", "(L, L^2)");
                Run(sb, false, false, "\u0002x_1\u001Fy_1\u001D1\u001F2.1\u001D2\u001F3.9\u001D3\u001F6.2\u001D4\u001F7.8", "y_1~m x_1+b", "m", "y_1~a e^{k x_1}");
                Run(sb, false, false, "y=", "a+", "q+1", "y=undefinedfn(x)", "f(x)=f(x)+1", "a=b", "b=a", "(1,2)+3", "[[1]]", "1=2");
                Run(sb, false, false, "f(x)=∫_0^x t^2 dt", "y=f(x)", "f(3)", "y=d/dx(sin(x))", "y=Σ_{n=0}^{5} x^n/n!");
            }
            catch (System.Exception e)
            {
                sb.AppendLine("EXCEPTION: " + e);
            }
            sb.AppendLine("total time: " + (EditorApplication.timeSinceStartup - t0).ToString("F2") + " s");
            Object.DestroyImmediate(go);
            return sb.ToString();
        }

        static string[] KindNames = { "EMPTY", "ERROR", "NOTE", "VALUE", "VAR", "FUNC", "PLOT", "REGRESSION", "TABLE", "POINTDEF" };

        static void Run(StringBuilder sb, bool cplx, bool deg, params string[] exprs)
        {
            sb.AppendLine("=== complex=" + cplx + " degrees=" + deg);
            comp.Build(exprs, exprs.Length, cplx, deg);
            eng.Bind();
            eng.EvaluateDefinitions();
            eng.SetView(0, 0, 10);
            eng.MarkAllDirty();
            double t0 = EditorApplication.timeSinceStartup;
            int guard = 0;
            while (eng.Work(50f) && guard < 5000) guard++;
            double dt = EditorApplication.timeSinceStartup - t0;
            for (int s = 0; s < exprs.Length; s++)
            {
                int k = comp.slotKind[s];
                string kn = k >= 0 && k < KindNames.Length ? KindNames[k] : k.ToString();
                string line = "  [" + s + "] " + Show(exprs[s]) + "  ->  " + kn;
                if (k == GCCompiler.K_ERROR) line += "  ERR: " + comp.slotError[s] + (string.IsNullOrEmpty(comp.slotUndef[s]) ? "" : " (undef " + comp.slotUndef[s] + ")");
                if (!string.IsNullOrEmpty(eng.slotDisplay[s])) line += "  " + eng.slotDisplay[s];
                if (comp.slotPlottable[s]) line += "  {" + eng.dbgSlotInfo[s] + "}";
                if (comp.slotIsSlider[s]) line += "  [slider " + comp.slotSliderValue[s] + "]";
                sb.AppendLine(line);
            }
            sb.AppendLine("  sampling: " + (dt * 1000).ToString("F0") + " ms (C#)");
        }

        static string Show(string s)
        {
            return s.Replace("\u0002", "<TABLE>").Replace("\u001F", "|").Replace("\u001D", " / ");
        }
    }
}
#endif
