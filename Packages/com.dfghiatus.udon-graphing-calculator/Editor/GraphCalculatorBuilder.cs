#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.UI;
using VRC.Udon;

namespace GraphCalc.EditorTools
{
    /// <summary>
    /// Builds the complete wall-mounted graphing calculator in the open scene:
    /// graph board (grid, per-expression renderers, axis labels, pan surface),
    /// expression list with sliders + table editor, keypad and settings bar.
    /// Menu: Tools > Graphing Calculator > Build Calculator In Scene
    /// </summary>
    public static class GraphCalculatorBuilder
    {
        public const string PackageDir = "Packages/com.dfghiatus.udon-graphing-calculator";
        const string RootDir = PackageDir + "/Runtime";
        public const string PrefabPath = RootDir + "/GraphingCalculator.prefab";
        public const string ProjectUrl = "https://github.com/dfgHiatus/Udon-Graphing-Calculator";
        const string MatDir = RootDir + "/Materials";
        const int SLOTS = 16;
        const int VROWS = 7;
        const float BOARD_W = 2.0f;
        const float BOARD_H = 1.5f;

        static TMP_FontAsset font;
        static Sprite uiSprite;
        static Sprite knobSprite;
        static Sprite bgSprite;
        static GraphCalculator calc;

        static readonly Color ColPanel = new Color(0.96f, 0.96f, 0.96f);
        static readonly Color ColKey = new Color(1f, 1f, 1f);
        static readonly Color ColKeyDark = new Color(0.86f, 0.87f, 0.89f);
        static readonly Color ColKeyBlue = new Color(0.18f, 0.44f, 0.70f);
        static readonly Color ColText = new Color(0.13f, 0.13f, 0.13f);

        // ------------------------------------------------------------------ menu entry
        [MenuItem("Tools/Graphing Calculator/Build Calculator In Scene")]
        public static void BuildInScene()
        {
            if (!CheckPrerequisites()) return;

            font = TMP_Settings.defaultFontAsset;
            uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            knobSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            bgSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");

            Material matGrid = EnsureMaterial("GC_Grid", "GraphCalc/Grid");
            Material matLine = EnsureMaterial("GC_Line", "GraphCalc/Line");
            Material matFill = EnsureMaterial("GC_Fill", "GraphCalc/Fill");
            Material matPoint = EnsureMaterial("GC_Point", "GraphCalc/Point");
            Material matImplicit = EnsureMaterial("GC_Implicit", "GraphCalc/Implicit");
            Vector4 half = new Vector4(BOARD_W * 0.5f, BOARD_H * 0.5f, 0, 0);
            foreach (Material m in new[] { matLine, matFill, matPoint, matImplicit }) m.SetVector("_BoardHalf", half);
            AssetDatabase.SaveAssets();

            GameObject root = new GameObject("GraphingCalculator");
            Undo.RegisterCreatedObjectUndo(root, "Build Graphing Calculator");
            root.transform.position = new Vector3(0, 0, 3);

            // ---- behaviours
            calc = UdonSharpUndo.AddComponent<GraphCalculator>(root);
            GameObject compGo = Child(root.transform, "Compiler");
            GCCompiler compiler = UdonSharpUndo.AddComponent<GCCompiler>(compGo);
            GameObject engGo = Child(root.transform, "Engine");
            GCEngine engine = UdonSharpUndo.AddComponent<GCEngine>(engGo);
            calc.compiler = compiler;
            calc.engine = engine;
            engine.compiler = compiler;
            engine.boardWidth = BOARD_W;
            engine.boardHeight = BOARD_H;
            calc.boardWidth = BOARD_W;
            calc.boardHeight = BOARD_H;

            // ---- backboard
            GameObject back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "Backboard";
            back.transform.SetParent(root.transform, false);
            back.transform.localPosition = new Vector3(0.02f, 1.55f, 0.03f);
            back.transform.localScale = new Vector3(3.08f, 2.5f, 0.04f);
            Material backMat = EnsureLitMaterial("GC_Backboard", new Color(0.22f, 0.24f, 0.27f));
            back.GetComponent<MeshRenderer>().sharedMaterial = backMat;

            BuildDisplay(root.transform, engine, matGrid, matLine, matFill, matPoint, matImplicit);
            BuildExpressionPanel(root.transform);
            BuildKeypad(root.transform);
            BuildInfoPanels(root.transform);

            EditorUtility.SetDirty(calc);
            EditorUtility.SetDirty(engine);
            EditorUtility.SetDirty(compiler);
            Selection.activeGameObject = root;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[GraphingCalculator] Built calculator. Enter Play mode (ClientSim) or build & test to try it.");
        }

        // ------------------------------------------------------------------ prerequisites
        static bool CheckPrerequisites()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                EditorUtility.DisplayDialog("Graphing Calculator",
                    "TextMesh Pro essentials are missing.\n\nUse Window > TextMeshPro > Import TMP Essential Resources, then run this again.", "OK");
                return false;
            }
            System.Type[] types = { typeof(GraphCalculator), typeof(GCCompiler), typeof(GCEngine), typeof(CalcKey) };
            bool missing = false;
            foreach (System.Type t in types) if (UdonSharpEditorUtility.GetUdonSharpProgramAsset(t) == null) missing = true;
            if (missing)
            {
                // the U# lookup cache may be stale if program assets were just imported
                MethodInfo clear = typeof(UdonSharpProgramAsset).GetMethod("ClearProgramAssetCache", BindingFlags.NonPublic | BindingFlags.Static);
                if (clear != null) clear.Invoke(null, null);
                missing = false;
                foreach (System.Type t in types)
                {
                    if (UdonSharpEditorUtility.GetUdonSharpProgramAsset(t) != null) continue;
                    missing = true;
                    CreateProgramAsset(t);
                }
                if (missing)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    if (clear != null) clear.Invoke(null, null);
                    UdonSharpProgramAsset.CompileAllCsPrograms(true);
                    foreach (System.Type t in types)
                    {
                        if (UdonSharpEditorUtility.GetUdonSharpProgramAsset(t) == null)
                        {
                            EditorUtility.DisplayDialog("Graphing Calculator",
                                "UdonSharp program asset for " + t.Name + " is missing or failed to compile. Check the Console for U# errors, then try again.", "OK");
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        static void CreateProgramAsset(System.Type t)
        {
            string[] guids = AssetDatabase.FindAssets(t.Name + " t:MonoScript");
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                MonoScript ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms == null || ms.GetClass() != t) continue;
                string assetPath = System.IO.Path.ChangeExtension(path, ".asset");
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null) return;
                UdonSharpProgramAsset a = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                a.sourceCsScript = ms;
                AssetDatabase.CreateAsset(a, assetPath);
                return;
            }
        }

        static Material EnsureMaterial(string name, string shaderName)
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(RootDir, "Materials");
            string path = MatDir + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader sh = Shader.Find(shaderName);
            if (sh == null) { Debug.LogError("[GraphingCalculator] Shader not found: " + shaderName); sh = Shader.Find("Unlit/Color"); }
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = sh;
            m.enableInstancing = false;
            return m;
        }

        static Material EnsureLitMaterial(string name, Color c)
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(RootDir, "Materials");
            string path = MatDir + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = c;
            m.SetFloat("_Glossiness", 0.2f);
            return m;
        }

        // ------------------------------------------------------------------ generic helpers
        static GameObject Child(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static RectTransform Rect(GameObject go, Transform parent, float x, float y, float w, float h)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static void Stretch(RectTransform rt, float pad)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        static GameObject MakeCanvas(Transform parent, string name, Vector3 localPos, float wPx, float hPx, float metresPerPx)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(wPx, hPx);
            rt.localPosition = localPos;
            rt.localScale = Vector3.one * metresPerPx;
            Canvas c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            CanvasScaler cs = go.AddComponent<CanvasScaler>();
            cs.dynamicPixelsPerUnit = 2;
            cs.referencePixelsPerUnit = 100;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<VRC.SDK3.Components.VRCUiShape>();
            go.layer = 0;
            return go;
        }

        static Image MakeImage(Transform parent, string name, float x, float y, float w, float h, Color c, Sprite sprite, bool raycast)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            Rect(go, parent, x, y, w, h);
            Image img = go.GetComponent<Image>();
            img.color = c;
            if (sprite != null) { img.sprite = sprite; img.type = Image.Type.Sliced; }
            img.raycastTarget = raycast;
            return img;
        }

        static TextMeshProUGUI MakeText(Transform parent, string name, float x, float y, float w, float h, string text, float size, Color c, TextAlignmentOptions align, bool wrap)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Rect(go, parent, x, y, w, h);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = c;
            t.alignment = align;
            t.enableWordWrapping = wrap;
            t.overflowMode = TextOverflowModes.Truncate;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        static void Wire(UnityEventBase ev, GameObject host, string key)
        {
            CalcKey ck = UdonSharpUndo.AddComponent<CalcKey>(host);
            ck.calc = calc;
            ck.key = key;
            EditorUtility.SetDirty(ck);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(ck);
            UnityEventTools.AddStringPersistentListener(ev, ub.SendCustomEvent, "OnPress");
        }

        static Button MakeButton(Transform parent, string name, float x, float y, float w, float h, string label, float fontSize, Color bg, Color fg, string key, out TextMeshProUGUI text)
        {
            Image img = MakeImage(parent, name, x, y, w, h, bg, uiSprite, true);
            GameObject go = img.gameObject;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.90f, 0.93f, 1f);
            cb.pressedColor = new Color(0.70f, 0.78f, 0.95f);
            cb.selectedColor = Color.white;
            cb.fadeDuration = 0.05f;
            btn.colors = cb;
            Navigation nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            text = MakeText(go.transform, "Label", 0, 0, w, h, label, fontSize, fg, TextAlignmentOptions.Center, false);
            Stretch(text.rectTransform, 4);
            if (!string.IsNullOrEmpty(key)) Wire(btn.onClick, go, key);
            return btn;
        }

        static Button MakeButton(Transform parent, string name, float x, float y, float w, float h, string label, float fontSize, Color bg, Color fg, string key)
        {
            TextMeshProUGUI unused;
            return MakeButton(parent, name, x, y, w, h, label, fontSize, bg, fg, key, out unused);
        }

        static MeshRenderer MakeMeshRenderer(Transform parent, string name, Material mat, out MeshFilter mf)
        {
            GameObject go = Child(parent, name);
            mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.allowOcclusionWhenDynamic = false;
            return mr;
        }

        static GameObject MakeQuad(Transform parent, string name, Material mat, Vector3 pos, Vector3 scale)
        {
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = pos;
            q.transform.localScale = scale;
            MeshRenderer mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return q;
        }

        // ------------------------------------------------------------------ graph display
        static void BuildDisplay(Transform root, GCEngine engine, Material matGrid, Material matLine, Material matFill, Material matPoint, Material matImplicit)
        {
            GameObject display = Child(root, "Display");
            display.transform.localPosition = new Vector3(0.5f, 1.95f, 0f);
            GameObject surface = Child(display.transform, "GraphSurface");

            GameObject bg = MakeQuad(surface.transform, "Grid", matGrid, Vector3.zero, new Vector3(BOARD_W, BOARD_H, 1));
            calc.gridRenderer = bg.GetComponent<MeshRenderer>();

            MeshFilter[] lf = new MeshFilter[SLOTS], ff = new MeshFilter[SLOTS], pf = new MeshFilter[SLOTS];
            MeshRenderer[] lr = new MeshRenderer[SLOTS], fr = new MeshRenderer[SLOTS], pr = new MeshRenderer[SLOTS], ir = new MeshRenderer[SLOTS];
            GameObject slotsRoot = Child(surface.transform, "Slots");
            for (int s = 0; s < SLOTS; s++)
            {
                GameObject slot = Child(slotsRoot.transform, "Slot" + s);
                MeshFilter mf;
                fr[s] = MakeMeshRenderer(slot.transform, "Fill", matFill, out mf); ff[s] = mf;
                GameObject imp = MakeQuad(slot.transform, "Implicit", matImplicit, new Vector3(0, 0, -0.0015f), new Vector3(BOARD_W, BOARD_H, 1));
                ir[s] = imp.GetComponent<MeshRenderer>();
                lr[s] = MakeMeshRenderer(slot.transform, "Lines", matLine, out mf); lf[s] = mf;
                pr[s] = MakeMeshRenderer(slot.transform, "Points", matPoint, out mf); pf[s] = mf;
                fr[s].enabled = false; ir[s].enabled = false; lr[s].enabled = false; pr[s].enabled = false;
            }
            engine.lineFilters = lf; engine.lineRenderers = lr;
            engine.fillFilters = ff; engine.fillRenderers = fr;
            engine.pointFilters = pf; engine.pointRenderers = pr;
            engine.implicitRenderers = ir;

            // axis labels (3D TextMeshPro)
            GameObject labels = Child(surface.transform, "Labels");
            calc.labelRoot = labels.transform;
            TextMeshPro[] xl = new TextMeshPro[24];
            TextMeshPro[] yl = new TextMeshPro[20];
            for (int i = 0; i < xl.Length; i++) xl[i] = MakeLabel(labels.transform, "X" + i);
            for (int i = 0; i < yl.Length; i++) yl[i] = MakeLabel(labels.transform, "Y" + i);
            calc.xLabels = xl;
            calc.yLabels = yl;

            // interaction canvas over the board
            const float PX = 1000f;
            float mpp = BOARD_W / PX;
            GameObject canvas = MakeCanvas(display.transform, "GraphCanvas", new Vector3(0, 0, -0.008f), PX, BOARD_H / mpp, mpp);
            float H = BOARD_H / mpp;
            calc.panCanvasWidthPx = PX;

            Image pan = MakeImage(canvas.transform, "PanArea", 0, 0, PX, H, new Color(1, 1, 1, 0), null, true);
            Stretch(pan.rectTransform, 0);
            ScrollRect sr = pan.gameObject.AddComponent<ScrollRect>();
            GameObject content = new GameObject("Content", typeof(RectTransform));
            RectTransform crt = content.GetComponent<RectTransform>();
            crt.SetParent(pan.transform, false);
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(100000, 100000);
            crt.anchoredPosition = Vector2.zero;
            sr.content = crt;
            sr.horizontal = true;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Unrestricted;
            sr.inertia = false;
            sr.scrollSensitivity = 0;
            calc.panScroll = sr;
            Wire(sr.onValueChanged, pan.gameObject, "pan");

            // view buttons (top-right)
            MakeButton(canvas.transform, "ZoomIn", PX - 80, 12, 68, 68, "+", 44, ColKey, ColText, "zoomin");
            MakeButton(canvas.transform, "ZoomOut", PX - 80, 88, 68, 68, "\u2212", 44, ColKey, ColText, "zoomout");
            MakeButton(canvas.transform, "HomeView", PX - 80, 164, 68, 68, "\u2302", 38, ColKey, ColText, "homeview");

            // settings bar (top-left)
            TextMeshProUGUI t;
            float bx = 12;
            MakeButton(canvas.transform, "Complex", bx, 12, 110, 48, "C off", 24, ColKey, ColText, "complex", out t); calc.complexLabel = t; bx += 118;
            MakeButton(canvas.transform, "Degrees", bx, 12, 90, 48, "RAD", 24, ColKey, ColText, "deg", out t); calc.degLabel = t; bx += 98;
            MakeButton(canvas.transform, "Grid", bx, 12, 100, 48, "Grid", 24, ColKey, ColText, "grid", out t); calc.gridLabel = t; bx += 108;
            MakeButton(canvas.transform, "Lock", bx, 12, 120, 48, "Unlocked", 22, ColKey, ColText, "lock", out t); calc.lockLabel = t; bx += 128;
            MakeButton(canvas.transform, "Undo", bx, 12, 90, 48, "Undo", 24, ColKey, ColText, "undo"); bx += 98;
            MakeButton(canvas.transform, "Redo", bx, 12, 90, 48, "Redo", 24, ColKey, ColText, "redo");

            calc.statusText = MakeText(canvas.transform, "Status", 12, H - 40, 700, 34, "", 20, new Color(0.35f, 0.35f, 0.35f), TextAlignmentOptions.BottomLeft, false);
        }

        static TextMeshPro MakeLabel(Transform parent, string name)
        {
            GameObject go = Child(parent, name);
            TextMeshPro t = go.AddComponent<TextMeshPro>();
            t.font = font;
            t.fontSize = 0.26f;
            t.color = new Color(0.25f, 0.25f, 0.25f);
            t.enableWordWrapping = false;
            t.alignment = TextAlignmentOptions.Top;
            t.rectTransform.sizeDelta = new Vector2(0.16f, 0.04f);
            t.text = "";
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null) { mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; }
            go.SetActive(false);
            return t;
        }

        // ------------------------------------------------------------------ expression panel
        static void BuildExpressionPanel(Transform root)
        {
            const float W = 900, H = 1500;
            GameObject canvas = MakeCanvas(root, "ExpressionCanvas", new Vector3(-1.0f, 1.95f, -0.008f), W, H, 0.001f);
            Image bg = MakeImage(canvas.transform, "Background", 0, 0, W, H, ColPanel, null, true);
            Stretch(bg.rectTransform, 0);

            // header
            MakeText(canvas.transform, "Title", 16, 14, 260, 70, "<b>Expressions</b>", 34, ColText, TextAlignmentOptions.Left, false);
            MakeButton(canvas.transform, "AddExpr", 270, 16, 110, 66, "+ expr", 26, ColKeyBlue, Color.white, "add");
            MakeButton(canvas.transform, "AddTable", 388, 16, 110, 66, "+ table", 26, ColKey, ColText, "addtable");
            MakeButton(canvas.transform, "AddNote", 506, 16, 100, 66, "+ note", 26, ColKey, ColText, "addnote");
            MakeButton(canvas.transform, "ScrollUp", 616, 16, 66, 66, "\u25B2", 28, ColKey, ColText, "scrollup");
            MakeButton(canvas.transform, "ScrollDown", 688, 16, 66, 66, "\u25BC", 28, ColKey, ColText, "scrolldown");
            MakeButton(canvas.transform, "ResetAll", 764, 16, 120, 66, "Clear all", 22, new Color(0.98f, 0.90f, 0.90f), new Color(0.6f, 0.15f, 0.15f), "resetall");
            calc.listScrollLabel = MakeText(canvas.transform, "ScrollLabel", 16, 76, 300, 28, "", 20, new Color(0.45f, 0.45f, 0.45f), TextAlignmentOptions.Left, false);

            GameObject[] rowRoot = new GameObject[VROWS];
            Image[] rowBg = new Image[VROWS];
            TextMeshProUGUI[] rowNum = new TextMeshProUGUI[VROWS], rowText = new TextMeshProUGUI[VROWS], rowInfo = new TextMeshProUGUI[VROWS];
            Image[] rowSwatch = new Image[VROWS];
            TextMeshProUGUI[] rowSwatchLabel = new TextMeshProUGUI[VROWS];
            GameObject[] sliderGroup = new GameObject[VROWS];
            Slider[] sliders = new Slider[VROWS];
            TextMeshProUGUI[] rowMin = new TextMeshProUGUI[VROWS], rowMax = new TextMeshProUGUI[VROWS], rowStep = new TextMeshProUGUI[VROWS], rowPlay = new TextMeshProUGUI[VROWS];
            GameObject[] addSlider = new GameObject[VROWS];

            for (int r = 0; r < VROWS; r++)
            {
                float y0 = 110 + r * 192;
                Image rowImg = MakeImage(canvas.transform, "Row" + r, 0, y0, W, 190, Color.white, null, true);
                GameObject row = rowImg.gameObject;
                Button rowBtn = row.AddComponent<Button>();
                rowBtn.targetGraphic = rowImg;
                ColorBlock cb = rowBtn.colors; cb.highlightedColor = new Color(0.97f, 0.98f, 1f); cb.pressedColor = new Color(0.9f, 0.93f, 1f); rowBtn.colors = cb;
                Navigation nav = rowBtn.navigation; nav.mode = Navigation.Mode.None; rowBtn.navigation = nav;
                Wire(rowBtn.onClick, row, "sel:" + r);
                rowRoot[r] = row;
                rowBg[r] = rowImg;

                // left strip
                MakeImage(row.transform, "Strip", 0, 0, 70, 190, new Color(0.93f, 0.93f, 0.93f), null, false);
                rowNum[r] = MakeText(row.transform, "Num", 0, 6, 70, 30, (r + 1).ToString(), 22, new Color(0.5f, 0.5f, 0.5f), TextAlignmentOptions.Center, false);
                TextMeshProUGUI swl;
                Button sw = MakeButton(row.transform, "Swatch", 12, 46, 46, 46, "", 26, Color.gray, Color.white, "vis:" + r, out swl);
                Image swImg = sw.GetComponent<Image>();
                swImg.sprite = knobSprite; swImg.type = Image.Type.Simple;
                rowSwatch[r] = swImg;
                rowSwatchLabel[r] = swl;
                MakeButton(row.transform, "Color", 8, 106, 54, 40, "color", 15, ColKeyDark, ColText, "col:" + r);

                // delete
                MakeButton(row.transform, "Delete", W - 62, 8, 52, 52, "\u00D7", 34, new Color(0.97f, 0.97f, 0.97f), new Color(0.45f, 0.45f, 0.45f), "delrow:" + r);

                rowText[r] = MakeText(row.transform, "Text", 82, 8, W - 160, 92, "", 34, ColText, TextAlignmentOptions.TopLeft, true);
                rowInfo[r] = MakeText(row.transform, "Info", 82, 100, W - 100, 34, "", 22, new Color(0.35f, 0.35f, 0.35f), TextAlignmentOptions.TopLeft, false);

                // add slider button (shown for undefined variables)
                Button asb = MakeButton(row.transform, "AddSlider", 82, 136, 220, 46, "+ add slider", 22, new Color(0.90f, 0.94f, 1f), ColKeyBlue, "addslider:" + r);
                addSlider[r] = asb.gameObject;
                asb.gameObject.SetActive(false);

                // slider group
                GameObject sg = new GameObject("SliderGroup", typeof(RectTransform));
                Rect(sg, row.transform, 76, 134, W - 86, 54);
                sliderGroup[r] = sg;
                TextMeshProUGUI tl;
                MakeButton(sg.transform, "Min", 0, 2, 96, 50, "-10", 20, new Color(0.97f, 0.97f, 0.97f), ColText, "smin:" + r, out tl); rowMin[r] = tl;
                sliders[r] = MakeSlider(sg.transform, 104, 8, 430, 38);
                Wire(sliders[r].onValueChanged, sliders[r].gameObject, "slider:" + r);
                MakeButton(sg.transform, "Max", 542, 2, 96, 50, "10", 20, new Color(0.97f, 0.97f, 0.97f), ColText, "smax:" + r, out tl); rowMax[r] = tl;
                MakeButton(sg.transform, "Step", 644, 2, 96, 50, "step", 18, new Color(0.97f, 0.97f, 0.97f), ColText, "sstep:" + r, out tl); rowStep[r] = tl;
                MakeButton(sg.transform, "Play", 746, 2, 60, 50, "\u25BA", 24, ColKeyBlue, Color.white, "play:" + r, out tl); rowPlay[r] = tl;
                sg.SetActive(false);
            }
            calc.rowRoot = rowRoot; calc.rowBg = rowBg; calc.rowNum = rowNum; calc.rowText = rowText; calc.rowInfo = rowInfo;
            calc.rowSwatch = rowSwatch; calc.rowSwatchLabel = rowSwatchLabel; calc.rowSliderGroup = sliderGroup; calc.rowSlider = sliders;
            calc.rowMin = rowMin; calc.rowMax = rowMax; calc.rowStep = rowStep; calc.rowPlay = rowPlay; calc.rowAddSlider = addSlider;

            BuildTablePanel(canvas.transform, W);
        }

        static Slider MakeSlider(Transform parent, float x, float y, float w, float h)
        {
            DefaultControls.Resources res = new DefaultControls.Resources();
            res.standard = uiSprite;
            res.background = bgSprite;
            res.knob = knobSprite;
            GameObject go = DefaultControls.CreateSlider(res);
            go.name = "Slider";
            Rect(go, parent, x, y, w, h);
            Slider s = go.GetComponent<Slider>();
            s.minValue = 0; s.maxValue = 1; s.wholeNumbers = false; s.value = 0.5f;
            Navigation nav = s.navigation; nav.mode = Navigation.Mode.None; s.navigation = nav;
            // enlarge handle for VR
            if (s.handleRect != null) s.handleRect.sizeDelta = new Vector2(34, 0);
            if (s.fillRect != null)
            {
                Image fill = s.fillRect.GetComponent<Image>();
                if (fill != null) fill.color = ColKeyBlue;
            }
            return s;
        }

        static void BuildTablePanel(Transform canvas, float W)
        {
            const int TR = GraphCalculator.TROWS, TC = GraphCalculator.TCOLS;
            Image panel = MakeImage(canvas, "TablePanel", 0, 105, W, 1395, Color.white, null, true);
            calc.tablePanel = panel.gameObject;
            calc.tableTitle = MakeText(panel.transform, "Title", 20, 12, 600, 50, "Table", 30, ColText, TextAlignmentOptions.Left, false);
            MakeButton(panel.transform, "Done", W - 150, 10, 130, 56, "Done", 26, ColKeyBlue, Color.white, "tbldone");

            TextMeshProUGUI[] cells = new TextMeshProUGUI[TR * TC];
            Image[] cellBg = new Image[TR * TC];
            float cw = (W - 40) / TC, ch = 112;
            for (int vr = 0; vr < TR; vr++)
            {
                for (int vc = 0; vc < TC; vc++)
                {
                    TextMeshProUGUI t;
                    Button b = MakeButton(panel.transform, "Cell" + vr + "_" + vc, 20 + vc * cw, 80 + vr * (ch + 4), cw - 4, ch, "", 30,
                        vr == 0 ? new Color(0.93f, 0.93f, 0.93f) : Color.white, ColText, "cell:" + vr + ":" + vc, out t);
                    cells[vr * TC + vc] = t;
                    cellBg[vr * TC + vc] = b.GetComponent<Image>();
                }
            }
            calc.tableCells = cells;
            calc.tableCellBg = cellBg;
            float by = 80 + TR * (ch + 4) + 16;
            float bw = (W - 40 - 5 * 8) / 6f;
            string[] labels = { "+ row", "+ column", "\u2212 column", "\u25B2", "\u25BC", "\u2193 next" };
            string[] keys = { "tbladdrow", "tbladdcol", "tbldelcol", "tblup", "tbldown", "down" };
            for (int i = 0; i < labels.Length; i++)
                MakeButton(panel.transform, "TblBtn" + i, 20 + i * (bw + 8), by, bw, 70, labels[i], 24, ColKey, ColText, keys[i]);
            panel.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ floating info panels
        const string InfoLeftName = "InfoPanel_HowToUse";
        const string InfoRightName = "InfoPanel_HowItWorks";

        /// <summary>Adds (or replaces) the two floating info panels on the selected calculator.</summary>
        [MenuItem("Tools/Graphing Calculator/Add Info Panels To Selected Calculator")]
        public static void AddInfoPanelsToSelection()
        {
            GameObject go = Selection.activeGameObject;
            GraphCalculator gc = go != null ? go.GetComponentInParent<GraphCalculator>() : null;
            if (gc == null)
            {
                EditorUtility.DisplayDialog("Graphing Calculator", "Select a GraphingCalculator object in the scene first.", "OK");
                return;
            }
            if (!PrepareFonts()) return;
            Undo.RegisterFullObjectHierarchyUndo(gc.gameObject, "Add info panels");
            BuildInfoPanels(gc.transform);
            EditorUtility.SetDirty(gc.gameObject);
        }

        /// <summary>Adds (or replaces) the info panels inside the package prefab.</summary>
        [MenuItem("Tools/Graphing Calculator/Add Info Panels To Prefab")]
        public static void AddInfoPanelsToPrefab()
        {
            if (!PrepareFonts()) return;
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (contents == null) { Debug.LogError("[GraphingCalculator] Prefab not found: " + PrefabPath); return; }
            BuildInfoPanels(contents.transform);
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
            Debug.Log("[GraphingCalculator] Info panels added to " + PrefabPath);
        }

        static bool PrepareFonts()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                EditorUtility.DisplayDialog("Graphing Calculator",
                    "TextMesh Pro essentials are missing.\n\nUse Window > TextMeshPro > Import TMP Essential Resources, then run this again.", "OK");
                return false;
            }
            font = TMP_Settings.defaultFontAsset;
            uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            knobSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            bgSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            return true;
        }

        static void BuildInfoPanels(Transform root)
        {
            foreach (string n in new[] { InfoLeftName, InfoRightName })
            {
                Transform old = root.Find(n);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }

            // left: what it is + how to use, with a copyable link
            GameObject left = MakeInfoCanvas(root, InfoLeftName, new Vector3(-2.45f, 1.6f, -0.35f), -25f);
            TextMeshProUGUI lt = MakeText(left.transform, "Text", 50, 40, 1000, 1640, HowToUseText, 34, new Color(0.93f, 0.94f, 0.96f), TextAlignmentOptions.TopLeft, true);
            lt.lineSpacing = 6;
            lt.overflowMode = TextOverflowModes.Truncate;
            MakeText(left.transform, "LinkLabel", 50, 1700, 1000, 40, "<b>Source code & docs</b>  <color=#9fb4d6>(click the box, then Ctrl+C to copy)</color>", 24, new Color(0.93f, 0.94f, 0.96f), TextAlignmentOptions.Left, false);
            MakeUrlField(left.transform, 50, 1745, 1000, 70, ProjectUrl);

            // right: how it works
            GameObject right = MakeInfoCanvas(root, InfoRightName, new Vector3(2.5f, 1.6f, -0.35f), 25f);
            TextMeshProUGUI rt = MakeText(right.transform, "Text", 50, 40, 1000, 1780, HowItWorksText, 28, new Color(0.93f, 0.94f, 0.96f), TextAlignmentOptions.TopLeft, true);
            rt.lineSpacing = 4;
            rt.overflowMode = TextOverflowModes.Truncate;
        }

        static GameObject MakeInfoCanvas(Transform root, string name, Vector3 pos, float yaw)
        {
            const float W = 1100, H = 1860;
            GameObject canvas = MakeCanvas(root, name, pos, W, H, 0.001f);
            canvas.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            Image bg = MakeImage(canvas.transform, "Background", 0, 0, W, H, new Color(0.13f, 0.15f, 0.19f, 0.92f), uiSprite, true);
            Stretch(bg.rectTransform, 0);
            Image accent = MakeImage(canvas.transform, "Accent", 0, 0, W, 10, new Color(0.18f, 0.44f, 0.70f, 1f), null, false);
            accent.rectTransform.anchorMin = new Vector2(0, 1);
            accent.rectTransform.anchorMax = new Vector2(1, 1);
            accent.rectTransform.offsetMin = new Vector2(0, -10);
            accent.rectTransform.offsetMax = Vector2.zero;
            return canvas;
        }

        static void MakeUrlField(Transform parent, float x, float y, float w, float h, string url)
        {
            TMP_DefaultControls.Resources tres = new TMP_DefaultControls.Resources();
            tres.standard = uiSprite;
            tres.inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd");
            GameObject inp = TMP_DefaultControls.CreateInputField(tres);
            inp.name = "ProjectUrl";
            Rect(inp, parent, x, y, w, h);
            TMP_InputField field = inp.GetComponent<TMP_InputField>();
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.readOnly = true;          // selectable and copyable, but not editable
            field.richText = false;
            field.text = url;
            field.selectionColor = new Color(0.55f, 0.75f, 1f, 0.6f);
            if (field.textComponent != null)
            {
                field.textComponent.font = font;
                field.textComponent.fontSize = 30;
                field.textComponent.color = new Color(0.10f, 0.33f, 0.62f);
                field.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
            }
            if (field.placeholder != null)
            {
                TextMeshProUGUI ph = field.placeholder as TextMeshProUGUI;
                if (ph != null) { ph.font = font; ph.text = ""; }
            }
            Navigation nav = field.navigation; nav.mode = Navigation.Mode.None; field.navigation = nav;
        }

        const string HowToUseText =
            "<size=150%><b>Udon Graphing Calculator</b></size>\n" +
            "A Desmos-style graphing calculator that runs entirely inside VRChat. Type equations, drag sliders and explore graphs together — everyone in the instance sees the same board.\n\n" +
            "<b><color=#8fb8ff>Getting started</color></b>\n" +
            "• Tap a row in the <b>Expressions</b> list to select it, then type on the keypad below the board.\n" +
            "• <b>123</b>, <b>abc</b> and <b>func</b> switch keypad pages. Or tap <i>Tap to type with keyboard</i> to use VRChat's keyboard.\n" +
            "• <b>Enter</b> adds a row, <b>DEL</b> on an empty row removes it, <b>↑ ↓</b> move between rows.\n" +
            "• The coloured circle shows/hides a graph; <b>color</b> changes its colour.\n\n" +
            "<b><color=#8fb8ff>Things to try</color></b>\n" +
            "<color=#ffd27f>y = x^2</color>   <color=#ffd27f>x^2 + y^2 = 9</color>   <color=#ffd27f>y < sin(x)</color>\n" +
            "<color=#ffd27f>r = 1 + cos(θ)</color>   <color=#ffd27f>(cos(t), sin(t))</color>\n" +
            "<color=#ffd27f>a = 2</color>  → a slider; use <i>a</i> in other rows\n" +
            "<color=#ffd27f>f(x) = x^3</color>, then <color=#ffd27f>y = f'(x)</color>\n" +
            "<color=#ffd27f>∫_0^1 x^2 dx</color>   <color=#ffd27f>Σ_{n=1}^{10} n</color>   <color=#ffd27f>mean([1...10])</color>\n" +
            "<b>+ table</b>, then <color=#ffd27f>y_1 ~ m x_1 + b</color> for a best-fit line\n\n" +
            "<b><color=#8fb8ff>Graph controls</color></b>\n" +
            "Drag the graph to pan · <b>+ / −</b> zoom · <b>⌂</b> reset view\n" +
            "<b>C</b> complex numbers · <b>RAD/DEG</b> angle mode · <b>Grid</b> · <b>Undo / Redo</b>\n\n" +
            "<b><color=#8fb8ff>Sharing</color></b>\n" +
            "Whoever presses a key takes control of the board. <b>Lock</b> keeps editing to the current editor until they unlock it.";

        const string HowItWorksText =
            "<size=150%><b>How it works</b></size>\n" +
            "All of the math — parsing, evaluation, calculus and graph sampling — runs in <b>Udon</b>, VRChat's sandboxed scripting language. Nothing is sent to a server.\n\n" +
            "<b><color=#8fb8ff>1. Compile</color></b>\n" +
            "Each expression is split into tokens, parsed into a syntax tree and classified: variable, function, curve, region, point, list, regression or table. It is then compiled to compact <b>bytecode</b>. Functions are inlined and definitions are ordered by what depends on what.\n\n" +
            "<b><color=#8fb8ff>2. Evaluate</color></b>\n" +
            "A small stack machine runs the bytecode on <b>256 points at once</b>. Udon is slow per instruction, so working on whole arrays hides most of that cost. There are real and complex versions of every operation. Calculus is numerical: 5-point derivatives, Gauss–Legendre integrals and direct sums.\n\n" +
            "<b><color=#8fb8ff>3. Sample</color></b>\n" +
            "Curves start with evenly spaced points, then add more where the curve bends or gets steep, and break the line at asymptotes. Implicit equations like x² + y² = 9 are sampled on a grid and turned into a distance field stored in a texture.\n\n" +
            "<b><color=#8fb8ff>4. Draw</color></b>\n" +
            "Udon builds meshes for lines, shaded regions and points. Tiny shaders only draw them: smooth lines, dashes for < and >, shading, and the zero-contour of the distance texture. Panning moves the existing meshes instantly while new samples are computed in the background.\n\n" +
            "<b><color=#8fb8ff>5. Stay smooth</color></b>\n" +
            "Work is <b>time-sliced</b>: only a few milliseconds per frame, so the frame rate stays steady and graphs sharpen over a moment. Quest uses smaller budgets and fewer samples.\n\n" +
            "<b><color=#8fb8ff>6. Sync</color></b>\n" +
            "Only the expression text, slider ranges, view and settings travel over the network. Every player computes the graphs locally, and animated sliders follow the shared server clock so they stay in step.";

        // ------------------------------------------------------------------ keypad
        static void BuildKeypad(Transform root)
        {
            const float W = 2950, H = 640;
            GameObject canvas = MakeCanvas(root, "KeypadCanvas", new Vector3(0.02f, 0.82f, -0.008f), W, H, 0.001f);
            Image bg = MakeImage(canvas.transform, "Background", 0, 0, W, H, new Color(0.90f, 0.91f, 0.93f), null, true);
            Stretch(bg.rectTransform, 0);

            string[] tabNames = { "123", "abc", "func" };
            Image[] tabBg = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                Button b = MakeButton(canvas.transform, "Tab" + i, 12, 12 + i * 122, 240, 112, tabNames[i], 38, ColKey, ColText, "tab:" + i);
                tabBg[i] = b.GetComponent<Image>();
            }
            calc.keypadTabBg = tabBg;

            // VRChat keyboard input field
            TMP_DefaultControls.Resources tres = new TMP_DefaultControls.Resources();
            tres.standard = uiSprite;
            tres.inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd");
            GameObject inp = TMP_DefaultControls.CreateInputField(tres);
            inp.name = "KeyboardInput";
            Rect(inp, canvas.transform, 12, 384, 240, 240);
            TMP_InputField field = inp.GetComponent<TMP_InputField>();
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            field.richText = false;
            if (field.textComponent != null) { field.textComponent.font = font; field.textComponent.fontSize = 26; }
            if (field.placeholder != null)
            {
                TextMeshProUGUI ph = field.placeholder as TextMeshProUGUI;
                if (ph != null) { ph.font = font; ph.text = "Tap to type with keyboard"; ph.fontSize = 24; ph.enableWordWrapping = true; }
            }
            Navigation nav = field.navigation; nav.mode = Navigation.Mode.None; field.navigation = nav;
            Wire(field.onValueChanged, inp, "kbd");
            calc.kbdInput = field;

            GameObject[] pages = new GameObject[3];
            pages[0] = BuildPage(canvas.transform, "Page123", MainPage());
            pages[1] = BuildPage(canvas.transform, "PageABC", AbcPage());
            pages[2] = BuildPage(canvas.transform, "PageFunc", FuncPage());
            pages[1].SetActive(false);
            pages[2].SetActive(false);
            calc.keypadPages = pages;
        }

        static GameObject BuildPage(Transform canvas, string name, List<string[]> rows)
        {
            GameObject page = new GameObject(name, typeof(RectTransform));
            Rect(page, canvas, 264, 10, 2676, 620);
            const int COLS = 13;
            float gap = 8;
            float kw = (2676 - gap * (COLS - 1)) / COLS;
            float kh = (620 - gap * (rows.Count - 1)) / rows.Count;
            for (int r = 0; r < rows.Count; r++)
            {
                string[] row = rows[r];
                for (int c = 0; c < row.Length / 2 && c < COLS; c++)
                {
                    string label = row[c * 2];
                    string key = row[c * 2 + 1];
                    if (string.IsNullOrEmpty(key)) continue;
                    bool action = !key.StartsWith("i:");
                    bool digit = key.Length == 3 && key[2] >= '0' && key[2] <= '9';
                    Color bgc = action ? ColKeyDark : (digit ? Color.white : new Color(0.97f, 0.97f, 0.98f));
                    if (key == "enter") bgc = ColKeyBlue;
                    Color fg = key == "enter" ? Color.white : ColText;
                    float fs = label.Length > 6 ? 30 : (label.Length > 3 ? 36 : 44);
                    MakeButton(page.transform, "Key_" + r + "_" + c, c * (kw + gap), r * (kh + gap), kw, kh, label, fs, bgc, fg, key);
                }
            }
            return page;
        }

        static List<string[]> MainPage()
        {
            List<string[]> p = new List<string[]>();
            p.Add(new[] { "x", "i:x", "y", "i:y", "a\u00B2", "i:^2", "a<sup>b</sup>", "i:^(`)", "\u221A", "i:\u221A(`)", "\u03C0", "i:\u03C0",
                          "7", "i:7", "8", "i:8", "9", "i:9", "\u00F7", "i:/", "(", "i:(", ")", "i:)", "DEL", "bk" });
            p.Add(new[] { "\u03B8", "i:\u03B8", "t", "i:t", "<", "i:<", ">", "i:>", "\u2264", "i:<=", "\u2265", "i:>=",
                          "4", "i:4", "5", "i:5", "6", "i:6", "\u00D7", "i:*", "[", "i:[", "]", "i:]", "CLR", "clr" });
            p.Add(new[] { "=", "i:=", ",", "i:,", "|a|", "i:|`|", "!", "i:!", "e", "i:e", "i", "i:i",
                          "1", "i:1", "2", "i:2", "3", "i:3", "\u2212", "i:-", "{", "i:{", "}", "i:}", "Enter", "enter" });
            p.Add(new[] { "~", "i:~", ":", "i::", "\u2026", "i:...", "'", "i:'", "a<sub>n</sub>", "i:_", "\u222B", "i:\u222B_0^1 ` dx",
                          "0", "i:0", ".", "i:.", "^", "i:^", "+", "i:+", "\u2190", "left", "\u2192", "right", "\u2191", "up" });
            p.Add(new[] { "\u03A3", "i:\u03A3_{n=1}^{10} `", "\u03A0", "i:\u03A0_{n=1}^{10} `", "d/dx", "i:d/dx(`)", "ln", "i:ln(`)", "log", "i:log(`)",
                          "e<sup>x</sup>", "i:e^(`)", "sin", "i:sin(`)", "cos", "i:cos(`)", "tan", "i:tan(`)", "space", "i: ",
                          "Home", "home", "End", "end", "\u2193", "down" });
            return p;
        }

        static List<string[]> AbcPage()
        {
            List<string[]> p = new List<string[]>();
            List<string> r0 = new List<string>();
            foreach (char ch in "qwertyuiop") { r0.Add(ch.ToString()); r0.Add("i:" + ch); }
            r0.AddRange(new[] { "(", "i:(", ")", "i:)", "DEL", "bk" });
            p.Add(r0.ToArray());
            List<string> r1 = new List<string>();
            foreach (char ch in "asdfghjkl") { r1.Add(ch.ToString()); r1.Add("i:" + ch); }
            r1.AddRange(new[] { "_", "i:_", ",", "i:,", "=", "i:=", "CLR", "clr" });
            p.Add(r1.ToArray());
            List<string> r2 = new List<string>();
            foreach (char ch in "zxcvbnm") { r2.Add(ch.ToString()); r2.Add("i:" + ch); }
            r2.AddRange(new[] { ".", "i:.", "<", "i:<", ">", "i:>", "[", "i:[", "]", "i:]", "Enter", "enter" });
            p.Add(r2.ToArray());
            List<string> r3 = new List<string>();
            foreach (char ch in "\u03B1\u03B2\u03B3\u03B4\u03B5\u03BB\u03BC\u03C3\u03C6\u03C9\u03C1\u03C4\u03B8") { r3.Add(ch.ToString()); r3.Add("i:" + ch); }
            p.Add(r3.ToArray());
            p.Add(new[] { "space", "i: ", "\u2190", "left", "\u2192", "right", "\u2191", "up", "\u2193", "down", "Home", "home", "End", "end",
                          "\"note", "i:\"", "+", "i:+", "\u2212", "i:-", "\u00D7", "i:*", "\u00F7", "i:/", "^", "i:^" });
            return p;
        }

        static List<string[]> FuncPage()
        {
            List<string[]> p = new List<string[]>();
            p.Add(new[] { "sin", "i:sin(`)", "cos", "i:cos(`)", "tan", "i:tan(`)", "csc", "i:csc(`)", "sec", "i:sec(`)", "cot", "i:cot(`)",
                          "arcsin", "i:arcsin(`)", "arccos", "i:arccos(`)", "arctan", "i:arctan(`)", "sinh", "i:sinh(`)", "cosh", "i:cosh(`)",
                          "tanh", "i:tanh(`)", "DEL", "bk" });
            p.Add(new[] { "ln", "i:ln(`)", "log", "i:log(`)", "log<sub>b</sub>", "i:log_{`}()", "exp", "i:exp(`)", "\u221A", "i:\u221A(`)",
                          "cbrt", "i:cbrt(`)", "n\u221A", "i:nthroot(`,)", "abs", "i:abs(`)", "sign", "i:sign(`)", "floor", "i:floor(`)",
                          "ceil", "i:ceil(`)", "round", "i:round(`)", "mod", "i:mod(`,)" });
            p.Add(new[] { "nCr", "i:nCr(`,)", "nPr", "i:nPr(`,)", "!", "i:!", "gcd", "i:gcd(`,)", "lcm", "i:lcm(`,)", "min", "i:min(`)",
                          "max", "i:max(`)", "total", "i:total(`)", "mean", "i:mean(`)", "median", "i:median(`)", "length", "i:length(`)",
                          "stdev", "i:stdev(`)", "var", "i:var(`)" });
            p.Add(new[] { "d/dx", "i:d/dx(`)", "d/dy", "i:d/dy(`)", "f'(x)", "i:'", "\u222B", "i:\u222B_0^1 ` dx", "\u03A3", "i:\u03A3_{n=1}^{10} `",
                          "\u03A0", "i:\u03A0_{n=1}^{10} `", "{a:b}", "i:{`:}", "{a:b,c}", "i:{`:,}", "real", "i:real(`)", "imag", "i:imag(`)",
                          "conj", "i:conj(`)", "arg", "i:arg(`)", "i", "i:i" });
            p.Add(new[] { "[1...n]", "i:[1...`]", "[a,b]", "i:[`]", "(x,y)", "i:(`,)", ".x", "i:.x", ".y", "i:.y", "~", "i:~",
                          "e", "i:e", "\u03C0", "i:\u03C0", "\u221E", "i:\u221E", "\u2190", "left", "\u2192", "right", "CLR", "clr", "Enter", "enter" });
            return p;
        }
    }
}
#endif
