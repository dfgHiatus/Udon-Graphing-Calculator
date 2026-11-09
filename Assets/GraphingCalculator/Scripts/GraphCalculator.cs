using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace GraphCalc
{
    /// <summary>
    /// Main controller: expression list state, editing, keypad, networking (manual sync),
    /// view (pan / zoom), grid + axis labels, sliders, tables, undo / redo.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class GraphCalculator : UdonSharpBehaviour
    {
        // ------------------------------------------------------------------ references (set by builder)
        public GCCompiler compiler;
        public GCEngine engine;

        [Header("Graph")]
        public MeshRenderer gridRenderer;
        public Transform labelRoot;
        public TextMeshPro[] xLabels;
        public TextMeshPro[] yLabels;
        public ScrollRect panScroll;
        public float panCanvasWidthPx = 1000f;
        public float boardWidth = 2.0f;
        public float boardHeight = 1.5f;

        [Header("Expression rows")]
        public GameObject[] rowRoot;
        public Image[] rowBg;
        public TextMeshProUGUI[] rowNum;
        public TextMeshProUGUI[] rowText;
        public TextMeshProUGUI[] rowInfo;
        public Image[] rowSwatch;
        public TextMeshProUGUI[] rowSwatchLabel;
        public GameObject[] rowSliderGroup;
        public Slider[] rowSlider;
        public TextMeshProUGUI[] rowMin;
        public TextMeshProUGUI[] rowMax;
        public TextMeshProUGUI[] rowStep;
        public TextMeshProUGUI[] rowPlay;
        public GameObject[] rowAddSlider;
        public TextMeshProUGUI listScrollLabel;

        [Header("Keypad")]
        public GameObject[] keypadPages;
        public Image[] keypadTabBg;
        public TMP_InputField kbdInput;

        [Header("Status / settings")]
        public TextMeshProUGUI statusText;
        public TextMeshProUGUI complexLabel;
        public TextMeshProUGUI degLabel;
        public TextMeshProUGUI gridLabel;
        public TextMeshProUGUI lockLabel;

        [Header("Table editor")]
        public GameObject tablePanel;
        public TextMeshProUGUI[] tableCells;   // TROWS * TCOLS (row-major), row 0 = headers
        public Image[] tableCellBg;
        public TextMeshProUGUI tableTitle;

        [Header("Performance")]
        public float workBudgetMs = 3.0f;
        public float questWorkBudgetMs = 2.0f;

        // ------------------------------------------------------------------ constants
        const int MAX = 16;
        const int VROWS = 7;
        public const int TROWS = 9;   // visible table rows incl. header
        public const int TCOLS = 4;   // visible table columns
        const int MAX_TCOLS = 6;
        const int MAX_TROWS = 64;
        const int UNDO_MAX = 30;
        const char SEP_SLOT = '\u001E';
        const char SEP_FIELD = '\u001C';
        const char TABLE_MARK = '\u0002';
        const char TBL_ROW = '\u001D';
        const char TBL_COL = '\u001F';
        const string CARET = "<color=#1e6fd9>|</color>";

        // ------------------------------------------------------------------ synced state
        [UdonSynced] string syncState = "";
        [UdonSynced] int[] syncMeta;
        [UdonSynced] double syncCx;
        [UdonSynced] double syncCy;
        [UdonSynced] double syncHw = 10;
        [UdonSynced] int syncFlags;
        [UdonSynced] int syncCount;

        // ------------------------------------------------------------------ local state
        string[] eText;
        string[] eMin;
        string[] eMax;
        string[] eStep;
        int[] eColor;
        bool[] eHidden;
        bool[] ePlay;
        int[] ePlayStart;
        int count;
        double viewCx;
        double viewCy;
        double viewHw = 10;
        bool complexMode;
        bool degreeMode;
        bool gridHidden;
        bool locked;

        int focusSlot;
        int focusField; // 0 text, 1 min, 2 max, 3 step, 100+ table cell (r*MAX_TCOLS+c)
        int caret;
        int scrollTop;
        int tableScroll;

        bool rebuildPending;
        double rebuildAt;
        bool serializePending;
        double lastSerialize;
        bool suppressKbd;
        Vector2 lastPanPos;
        bool panInit;
        double lastAnimTime;

        string[] undoStack;
        int undoCount;
        string[] redoStack;
        int redoCount;
        double lastSnapTime;

        Color[] palette;
        MaterialPropertyBlock gridMpb;
        bool started;
        bool isQuest;

        // =================================================================== lifecycle
        void Start()
        {
#if UNITY_ANDROID
            isQuest = true;
#endif
            palette = new Color[]
            {
                new Color(0.78f, 0.27f, 0.25f), new Color(0.18f, 0.44f, 0.70f), new Color(0.22f, 0.55f, 0.27f),
                new Color(0.38f, 0.26f, 0.65f), new Color(0.98f, 0.49f, 0.10f), new Color(0.0f, 0.0f, 0.0f),
                new Color(0.86f, 0.33f, 0.60f), new Color(0.0f, 0.60f, 0.60f)
            };
            eText = new string[MAX]; eMin = new string[MAX]; eMax = new string[MAX]; eStep = new string[MAX];
            eColor = new int[MAX]; eHidden = new bool[MAX]; ePlay = new bool[MAX]; ePlayStart = new int[MAX];
            for (int i = 0; i < MAX; i++) { eText[i] = ""; eMin[i] = ""; eMax[i] = ""; eStep[i] = ""; eColor[i] = i % 8; }
            if (syncMeta == null || syncMeta.Length != MAX * 2) syncMeta = new int[MAX * 2];
            undoStack = new string[UNDO_MAX];
            redoStack = new string[UNDO_MAX];

            compiler.Init();
            engine.Init();

            // default content
            count = 3;
            eText[0] = "y=sin(x)";
            eText[1] = "y=ax^2-3";
            eText[2] = "a=0.5";
            eColor[0] = 0; eColor[1] = 1; eColor[2] = 2;
            focusSlot = 0; focusField = 0; caret = eText[0].Length;
            started = true;

            SetViewLocal(0, 0, 10);
            DoRebuild();
            ShowKeypadPage(0);
            UpdateSettingLabels();
        }

        void Update()
        {
            if (!started) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (rebuildPending && now >= rebuildAt) DoRebuild();
            AnimateSliders(now);
            engine.Work(isQuest ? questWorkBudgetMs : workBudgetMs);
            if (serializePending && Networking.IsOwner(gameObject) && now - lastSerialize > 0.2)
            {
                serializePending = false;
                lastSerialize = now;
                RequestSerialization();
            }
            if (statusText != null && Time.frameCount % 15 == 0) UpdateStatus();
        }

        // =================================================================== networking
        public override void OnPreSerialization()
        {
            syncState = EncodeTexts();
            if (syncMeta == null || syncMeta.Length != MAX * 2) syncMeta = new int[MAX * 2];
            for (int i = 0; i < MAX; i++)
            {
                syncMeta[i] = (eColor[i] & 15) | (eHidden[i] ? 16 : 0) | (ePlay[i] ? 32 : 0);
                syncMeta[MAX + i] = ePlayStart[i];
            }
            syncCx = viewCx; syncCy = viewCy; syncHw = viewHw;
            syncFlags = (complexMode ? 1 : 0) | (degreeMode ? 2 : 0) | (gridHidden ? 4 : 0) | (locked ? 8 : 0);
            syncCount = count;
        }

        public override void OnDeserialization()
        {
            if (!started) return;
            ApplyRemoteState(syncState, syncMeta, syncCount, syncFlags);
            SetViewLocal(syncCx, syncCy, syncHw);
        }

        void ApplyRemoteState(string state, int[] meta, int newCount, int flags)
        {
            if (newCount < 0) newCount = 0;
            if (newCount > MAX) newCount = MAX;
            string[] nt = new string[MAX], nmin = new string[MAX], nmax = new string[MAX], nstep = new string[MAX];
            for (int i = 0; i < MAX; i++) { nt[i] = ""; nmin[i] = ""; nmax[i] = ""; nstep[i] = ""; }
            DecodeTexts(state, newCount, nt, nmin, nmax, nstep);
            bool newComplex = (flags & 1) != 0, newDeg = (flags & 2) != 0;
            bool structural = newCount != count || newComplex != complexMode || newDeg != degreeMode;
            if (!structural)
            {
                for (int i = 0; i < newCount; i++)
                {
                    if (nt[i] == eText[i]) continue;
                    string a = SliderName(eText[i]);
                    string b = SliderName(nt[i]);
                    if (a.Length == 0 || a != b) { structural = true; break; }
                }
            }
            bool rangesChanged = false;
            for (int i = 0; i < newCount; i++)
            {
                if (nmin[i] != eMin[i] || nmax[i] != eMax[i] || nstep[i] != eStep[i]) rangesChanged = true;
            }
            // apply
            string[] oldText = eText;
            eText = nt; eMin = nmin; eMax = nmax; eStep = nstep;
            int oldCount = count;
            count = newCount;
            if (meta != null && meta.Length >= MAX * 2)
            {
                for (int i = 0; i < MAX; i++)
                {
                    int m = meta[i];
                    eColor[i] = m & 15;
                    eHidden[i] = (m & 16) != 0;
                    bool play = (m & 32) != 0;
                    ePlay[i] = play;
                    ePlayStart[i] = meta[MAX + i];
                }
            }
            complexMode = newComplex;
            degreeMode = newDeg;
            gridHidden = (flags & 4) != 0;
            locked = (flags & 8) != 0;
            if (focusSlot >= count) { focusSlot = count > 0 ? count - 1 : 0; focusField = 0; caret = FieldText().Length; }
            if (caret > FieldText().Length) caret = FieldText().Length;
            if (structural)
            {
                DoRebuild();
            }
            else
            {
                bool any = false;
                for (int i = 0; i < count; i++)
                {
                    if (oldText[i] == eText[i]) continue;
                    double v = SliderValueOf(eText[i]);
                    if (!compiler.SetSliderValue(i, v)) { DoRebuild(); any = false; break; }
                    any = true;
                }
                if (any)
                {
                    engine.EvaluateDefinitions();
                    MarkDirtyForSliderChange();
                }
                ApplyStyles();
                UpdateRowsUI();
            }
            if (rangesChanged) UpdateRowsUI();
            UpdateSettingLabels();
            UpdateGrid();
            SyncKbd();
        }

        void MarkDirtyForSliderChange()
        {
            long mask = 0;
            for (int i = 0; i < count; i++) if (compiler.slotIsSlider[i] && compiler.slotDef[i] >= 0) mask |= 1L << compiler.slotDef[i];
            engine.MarkDirtyByDefMask(mask);
        }

        string EncodeTexts()
        {
            string s = "";
            for (int i = 0; i < count; i++)
            {
                if (i > 0) s += SEP_SLOT.ToString();
                s += eText[i] + SEP_FIELD.ToString() + eMin[i] + SEP_FIELD.ToString() + eMax[i] + SEP_FIELD.ToString() + eStep[i];
            }
            return s;
        }

        void DecodeTexts(string state, int n, string[] t, string[] mn, string[] mx, string[] st)
        {
            if (state == null) state = "";
            string[] slots = state.Split(new char[] { SEP_SLOT });
            for (int i = 0; i < n && i < slots.Length; i++)
            {
                string[] f = slots[i].Split(new char[] { SEP_FIELD });
                t[i] = f.Length > 0 ? f[0] : "";
                mn[i] = f.Length > 1 ? f[1] : "";
                mx[i] = f.Length > 2 ? f[2] : "";
                st[i] = f.Length > 3 ? f[3] : "";
            }
        }

        bool CanEdit()
        {
            if (!locked) return true;
            return Networking.IsOwner(gameObject);
        }

        void TakeOwnership()
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        void MarkChanged()
        {
            serializePending = true;
        }

        // =================================================================== undo
        string Snapshot()
        {
            string meta = "";
            for (int i = 0; i < MAX; i++) meta += (eColor[i] + (eHidden[i] ? 16 : 0)).ToString() + ",";
            return count.ToString() + SEP_SLOT.ToString() + meta + SEP_SLOT.ToString() + EncodeTexts();
        }

        void Restore(string snap)
        {
            int p1 = snap.IndexOf(SEP_SLOT);
            if (p1 < 0) return;
            int p2 = snap.IndexOf(SEP_SLOT, p1 + 1);
            if (p2 < 0) return;
            int n = ParseIntSafe(snap.Substring(0, p1));
            string meta = snap.Substring(p1 + 1, p2 - p1 - 1);
            string texts = snap.Substring(p2 + 1);
            string[] ms = meta.Split(new char[] { ',' });
            for (int i = 0; i < MAX && i < ms.Length; i++)
            {
                int m = ParseIntSafe(ms[i]);
                eColor[i] = m & 15; eHidden[i] = (m & 16) != 0;
            }
            if (n < 0) n = 0; if (n > MAX) n = MAX;
            for (int i = 0; i < MAX; i++) { eText[i] = ""; eMin[i] = ""; eMax[i] = ""; eStep[i] = ""; ePlay[i] = false; }
            DecodeTexts(texts, n, eText, eMin, eMax, eStep);
            count = n;
            if (focusSlot >= count) focusSlot = count > 0 ? count - 1 : 0;
            focusField = 0;
            caret = FieldText().Length;
        }

        void PushUndo(bool force)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!force && now - lastSnapTime < 1.0) { lastSnapTime = now; return; }
            lastSnapTime = now;
            string snap = Snapshot();
            if (undoCount > 0 && undoStack[undoCount - 1] == snap) return;
            if (undoCount >= UNDO_MAX)
            {
                for (int i = 1; i < UNDO_MAX; i++) undoStack[i - 1] = undoStack[i];
                undoCount = UNDO_MAX - 1;
            }
            undoStack[undoCount++] = snap;
            redoCount = 0;
        }

        void DoUndo()
        {
            if (undoCount == 0) return;
            string cur = Snapshot();
            if (redoCount < UNDO_MAX) redoStack[redoCount++] = cur;
            string prev = undoStack[--undoCount];
            if (prev == cur && undoCount > 0) prev = undoStack[--undoCount];
            Restore(prev);
            lastSnapTime = 0;
            AfterStructuralEdit();
        }

        void DoRedo()
        {
            if (redoCount == 0) return;
            string cur = Snapshot();
            if (undoCount < UNDO_MAX) undoStack[undoCount++] = cur;
            Restore(redoStack[--redoCount]);
            lastSnapTime = 0;
            AfterStructuralEdit();
        }

        // =================================================================== rebuild
        void ScheduleRebuild(double delay)
        {
            double t = Time.realtimeSinceStartupAsDouble + delay;
            if (!rebuildPending || t < rebuildAt) rebuildAt = t;
            if (rebuildPending && t > rebuildAt) rebuildAt = t;
            rebuildPending = true;
        }

        void DoRebuild()
        {
            rebuildPending = false;
            compiler.Build(eText, count, complexMode, degreeMode);
            engine.Bind();
            engine.EvaluateDefinitions();
            for (int s = count; s < MAX; s++) engine.ClearSlot(s);
            engine.MarkAllDirty();
            ApplyStyles();
            UpdateRowsUI();
        }

        void ApplyStyles()
        {
            for (int s = 0; s < MAX; s++)
            {
                Color c = palette[eColor[s] & 7];
                engine.SetSlotStyle(s, c, s < count && !eHidden[s]);
            }
        }

        // =================================================================== field access
        bool IsTable(int s) { return s >= 0 && s < count && eText[s].Length > 0 && eText[s][0] == TABLE_MARK; }

        string FieldText()
        {
            if (focusSlot < 0 || focusSlot >= count) return "";
            if (focusField == 0) return IsTable(focusSlot) ? "" : eText[focusSlot];
            if (focusField == 1) return eMin[focusSlot];
            if (focusField == 2) return eMax[focusSlot];
            if (focusField == 3) return eStep[focusSlot];
            if (focusField >= 100)
            {
                int ci = focusField - 100;
                return TableGet(focusSlot, ci / MAX_TCOLS, ci % MAX_TCOLS);
            }
            return "";
        }

        void SetFieldText(string v)
        {
            if (focusSlot < 0 || focusSlot >= count) return;
            if (focusField == 0) { if (!IsTable(focusSlot)) eText[focusSlot] = v; }
            else if (focusField == 1) eMin[focusSlot] = v;
            else if (focusField == 2) eMax[focusSlot] = v;
            else if (focusField == 3) eStep[focusSlot] = v;
            else if (focusField >= 100)
            {
                int ci = focusField - 100;
                TableSet(focusSlot, ci / MAX_TCOLS, ci % MAX_TCOLS, v);
            }
        }

        // ------------------------------------------------------------------ tables
        string[] TableRows(int s)
        {
            string body = eText[s].Length > 1 ? eText[s].Substring(1) : "";
            return body.Split(new char[] { TBL_ROW });
        }

        int TableCols(int s)
        {
            string[] rows = TableRows(s);
            if (rows.Length == 0) return 0;
            return rows[0].Split(new char[] { TBL_COL }).Length;
        }

        int TableRowCount(int s) { return TableRows(s).Length; }

        string TableGet(int s, int r, int c)
        {
            if (!IsTable(s)) return "";
            string[] rows = TableRows(s);
            if (r >= rows.Length) return "";
            string[] cells = rows[r].Split(new char[] { TBL_COL });
            return c < cells.Length ? cells[c] : "";
        }

        void TableSet(int s, int r, int c, string v)
        {
            string[] rows = TableRows(s);
            int cols = rows.Length > 0 ? rows[0].Split(new char[] { TBL_COL }).Length : 1;
            string outS = TABLE_MARK.ToString();
            int nrows = rows.Length;
            if (r >= nrows) nrows = r + 1;
            for (int ri = 0; ri < nrows; ri++)
            {
                string[] cells = ri < rows.Length ? rows[ri].Split(new char[] { TBL_COL }) : new string[0];
                if (ri > 0) outS += TBL_ROW.ToString();
                for (int ci = 0; ci < cols; ci++)
                {
                    if (ci > 0) outS += TBL_COL.ToString();
                    string cell = ci < cells.Length ? cells[ci] : "";
                    if (ri == r && ci == c) cell = v;
                    outS += cell;
                }
            }
            eText[s] = outS;
        }

        void TableAddColumn(int s)
        {
            string[] rows = TableRows(s);
            int cols = rows[0].Split(new char[] { TBL_COL }).Length;
            if (cols >= MAX_TCOLS) return;
            string outS = TABLE_MARK.ToString();
            for (int ri = 0; ri < rows.Length; ri++)
            {
                if (ri > 0) outS += TBL_ROW.ToString();
                outS += rows[ri] + TBL_COL.ToString();
                if (ri == 0) outS += "y_" + (s + 1) + "" + (char)('a' + cols - 1);
            }
            eText[s] = outS;
        }

        void TableRemoveColumn(int s, int c)
        {
            string[] rows = TableRows(s);
            int cols = rows[0].Split(new char[] { TBL_COL }).Length;
            if (cols <= 2 || c < 0 || c >= cols) return;
            string outS = TABLE_MARK.ToString();
            for (int ri = 0; ri < rows.Length; ri++)
            {
                if (ri > 0) outS += TBL_ROW.ToString();
                string[] cells = rows[ri].Split(new char[] { TBL_COL });
                bool first = true;
                for (int ci = 0; ci < cols; ci++)
                {
                    if (ci == c) continue;
                    if (!first) outS += TBL_COL.ToString();
                    first = false;
                    outS += ci < cells.Length ? cells[ci] : "";
                }
            }
            eText[s] = outS;
        }

        void TableAddRow(int s)
        {
            int rows = TableRowCount(s);
            if (rows >= MAX_TROWS) return;
            TableSet(s, rows, 0, "");
        }

        // =================================================================== key handling
        /// <summary>Key injected by editor tooling / other behaviours: set devKey then SendCustomEvent("DevPress").</summary>
        [HideInInspector] public string devKey = "";
        public void DevPress() { OnKey(devKey); }

        public void OnKey(string key)
        {
            if (!started || key == null || key.Length == 0) return;
            // view-only keys allowed when locked? No: view is shared state.
            if (!CanEdit())
            {
                if (statusText != null) statusText.text = "<color=#c74440>Locked by " + OwnerName() + "</color>";
                return;
            }
            TakeOwnership();

            if (key.Length > 2 && key[0] == 'i' && key[1] == ':') { InsertText(key.Substring(2)); return; }

            string cmd = key;
            int arg = -1;
            int arg2 = -1;
            int colon = key.IndexOf(':');
            if (colon > 0)
            {
                cmd = key.Substring(0, colon);
                string rest = key.Substring(colon + 1);
                int colon2 = rest.IndexOf(':');
                if (colon2 >= 0) { arg = ParseIntSafe(rest.Substring(0, colon2)); arg2 = ParseIntSafe(rest.Substring(colon2 + 1)); }
                else arg = ParseIntSafe(rest);
            }

            if (cmd == "bk") Backspace();
            else if (cmd == "del") DeleteForward();
            else if (cmd == "left") { if (caret > 0) caret--; RefreshEditUI(); }
            else if (cmd == "right") { if (caret < FieldText().Length) caret++; RefreshEditUI(); }
            else if (cmd == "home") { caret = 0; RefreshEditUI(); }
            else if (cmd == "end") { caret = FieldText().Length; RefreshEditUI(); }
            else if (cmd == "up") MoveFocus(-1);
            else if (cmd == "down") MoveFocus(1);
            else if (cmd == "enter") EnterKey();
            else if (cmd == "clr") { PushUndo(true); SetFieldText(""); caret = 0; AfterTextEdit(); }
            else if (cmd == "tab") ShowKeypadPage(arg);
            else if (cmd == "sel") SelectRow(arg);
            else if (cmd == "vis") ToggleVisible(arg);
            else if (cmd == "col") CycleColor(arg);
            else if (cmd == "delrow") DeleteRow(scrollTop + arg);
            else if (cmd == "smin") FocusField(scrollTop + arg, 1);
            else if (cmd == "smax") FocusField(scrollTop + arg, 2);
            else if (cmd == "sstep") FocusField(scrollTop + arg, 3);
            else if (cmd == "play") TogglePlay(scrollTop + arg);
            else if (cmd == "slider") SliderMoved(arg);
            else if (cmd == "addslider") AddSliderFor(scrollTop + arg);
            else if (cmd == "scrollup") { scrollTop = scrollTop > 0 ? scrollTop - 1 : 0; UpdateRowsUI(); }
            else if (cmd == "scrolldown") { if (scrollTop + VROWS <= count) scrollTop++; UpdateRowsUI(); }
            else if (cmd == "add") AddRow("", false);
            else if (cmd == "addtable") AddTable();
            else if (cmd == "addnote") AddRow("\"", false);
            else if (cmd == "zoomin") ZoomBy(1.0 / 1.5);
            else if (cmd == "zoomout") ZoomBy(1.5);
            else if (cmd == "homeview") { SetViewLocal(0, 0, 10); MarkChanged(); }
            else if (cmd == "squareview") { SetViewLocal(viewCx, viewCy, viewHw); MarkChanged(); }
            else if (cmd == "pan") OnPan();
            else if (cmd == "complex") { PushUndo(true); complexMode = !complexMode; AfterSettingsChange(true); }
            else if (cmd == "deg") { PushUndo(true); degreeMode = !degreeMode; AfterSettingsChange(true); }
            else if (cmd == "grid") { gridHidden = !gridHidden; AfterSettingsChange(false); }
            else if (cmd == "lock") { locked = !locked; AfterSettingsChange(false); }
            else if (cmd == "undo") DoUndo();
            else if (cmd == "redo") DoRedo();
            else if (cmd == "kbd") OnKbdChanged();
            else if (cmd == "cell") SelectCell(arg, arg2);
            else if (cmd == "tbladdrow") { PushUndo(true); TableAddRow(focusSlot); AfterTextEdit(); }
            else if (cmd == "tbladdcol") { PushUndo(true); TableAddColumn(focusSlot); AfterTextEdit(); }
            else if (cmd == "tbldelcol") { PushUndo(true); int ci = focusField >= 100 ? (focusField - 100) % MAX_TCOLS : TableCols(focusSlot) - 1; TableRemoveColumn(focusSlot, ci); focusField = 100; caret = FieldText().Length; AfterTextEdit(); }
            else if (cmd == "tblup") { if (tableScroll > 0) tableScroll--; UpdateTableUI(); }
            else if (cmd == "tbldown") { if (tableScroll + TROWS - 1 < TableRowCount(focusSlot)) tableScroll++; UpdateTableUI(); }
            else if (cmd == "tbldone") { focusField = 0; caret = 0; RefreshEditUI(); UpdateRowsUI(); }
            else if (cmd == "resetall") ResetAll();
        }

        void InsertText(string t)
        {
            if (count == 0) AddRow("", true);
            if (focusField == 0 && IsTable(focusSlot)) { focusField = 100 + MAX_TCOLS; caret = FieldText().Length; }
            PushUndo(false);
            string f = FieldText();
            if (caret > f.Length) caret = f.Length;
            int marker = t.IndexOf('`');
            string ins = marker >= 0 ? t.Substring(0, marker) + t.Substring(marker + 1) : t;
            // typing after a closing bracket of a selected superscript is fine; just insert
            f = f.Substring(0, caret) + ins + f.Substring(caret);
            caret += marker >= 0 ? marker : ins.Length;
            SetFieldText(f);
            AfterTextEdit();
        }

        string[] multiTokens;

        void Backspace()
        {
            string f = FieldText();
            if (focusField == 0 && f.Length == 0 && count > 1 && !IsTable(focusSlot))
            {
                int s = focusSlot;
                DeleteRow(s);
                if (s > 0) { focusSlot = s - 1; focusField = 0; caret = FieldText().Length; RefreshEditUI(); }
                return;
            }
            if (caret <= 0) return;
            PushUndo(false);
            int del = 1;
            if (multiTokens == null)
            {
                multiTokens = new string[]
                {
                    "arcsin(", "arccos(", "arctan(", "sinh(", "cosh(", "tanh(", "sin(", "cos(", "tan(", "csc(", "sec(", "cot(",
                    "sqrt(", "ln(", "log(", "exp(", "abs(", "floor(", "ceil(", "round(", "mod(", "nCr(", "nPr(", "gcd(", "lcm(",
                    "min(", "max(", "total(", "mean(", "median(", "length(", "stdev(", "var(", "real(", "imag(", "conj(", "arg(",
                    "sign(", "erf(", "nthroot(", "cbrt(", "d/dx", "d/dy", "d/dt", "<=", ">="
                };
            }
            for (int i = 0; i < multiTokens.Length; i++)
            {
                string tok = multiTokens[i];
                int L = tok.Length;
                if (caret >= L && f.Substring(caret - L, L) == tok) { del = L; break; }
            }
            f = f.Substring(0, caret - del) + f.Substring(caret);
            caret -= del;
            SetFieldText(f);
            AfterTextEdit();
        }

        void DeleteForward()
        {
            string f = FieldText();
            if (caret >= f.Length) return;
            PushUndo(false);
            f = f.Substring(0, caret) + f.Substring(caret + 1);
            SetFieldText(f);
            AfterTextEdit();
        }

        void AfterTextEdit()
        {
            MarkChanged();
            // slider fast path when only the value changed
            if (focusField == 0 && focusSlot < count && compiler.slotIsSlider.Length > focusSlot && !rebuildPending)
            {
                string nm = SliderName(eText[focusSlot]);
                if (nm.Length > 0 && focusSlot < compiler.slotCount && compiler.slotIsSlider[focusSlot] && compiler.slotDefName[focusSlot] == nm)
                {
                    compiler.SetSliderValue(focusSlot, SliderValueOf(eText[focusSlot]));
                    engine.EvaluateDefinitions();
                    MarkDirtyForSliderChange();
                    RefreshEditUI();
                    UpdateRowsUI();
                    return;
                }
            }
            if (focusField >= 1 && focusField <= 3) { RefreshEditUI(); UpdateRowsUI(); return; }
            ScheduleRebuild(0.2);
            RefreshEditUI();
        }

        void AfterStructuralEdit()
        {
            MarkChanged();
            DoRebuild();
            RefreshEditUI();
        }

        void AfterSettingsChange(bool recompile)
        {
            MarkChanged();
            if (recompile) DoRebuild();
            UpdateSettingLabels();
            UpdateGrid();
        }

        void RefreshEditUI()
        {
            UpdateRowsUI();
            SyncKbd();
        }

        void SyncKbd()
        {
            if (kbdInput == null) return;
            suppressKbd = true;
            kbdInput.SetTextWithoutNotify(FieldText());
            suppressKbd = false;
        }

        void OnKbdChanged()
        {
            if (suppressKbd || kbdInput == null) return;
            if (count == 0) AddRow("", true);
            string t = kbdInput.text;
            if (focusField == 0 && IsTable(focusSlot)) return;
            PushUndo(false);
            SetFieldText(t);
            caret = t.Length;
            MarkChanged();
            ScheduleRebuild(0.25);
            UpdateRowsUI();
        }

        void MoveFocus(int dir)
        {
            if (focusField >= 100)
            {
                int ci = focusField - 100;
                int r = ci / MAX_TCOLS + dir, c = ci % MAX_TCOLS;
                if (r < 0) r = 0;
                int rows = TableRowCount(focusSlot);
                if (r >= rows) { PushUndo(true); TableAddRow(focusSlot); MarkChanged(); ScheduleRebuild(0.2); }
                focusField = 100 + r * MAX_TCOLS + c;
                caret = FieldText().Length;
                EnsureTableRowVisible(r);
                RefreshEditUI();
                return;
            }
            int s = focusSlot + dir;
            if (s < 0) s = 0;
            if (s >= count) { if (dir > 0) { AddRow("", false); } return; }
            focusSlot = s; focusField = 0; caret = FieldText().Length;
            EnsureVisible(s);
            RefreshEditUI();
        }

        void EnterKey()
        {
            if (focusField >= 100) { MoveFocus(1); return; }
            if (focusField >= 1 && focusField <= 3) { focusField = 0; caret = FieldText().Length; RefreshEditUI(); return; }
            AddRow("", false);
        }

        void FocusField(int s, int field)
        {
            if (s < 0 || s >= count) return;
            focusSlot = s; focusField = field; caret = FieldText().Length;
            RefreshEditUI();
        }

        void SelectRow(int r)
        {
            int s = scrollTop + r;
            if (s >= count) { AddRow("", false); return; }
            focusSlot = s;
            if (IsTable(s)) { focusField = 100 + MAX_TCOLS; tableScroll = 0; }
            else focusField = 0;
            caret = FieldText().Length;
            RefreshEditUI();
        }

        void SelectCell(int vr, int vc)
        {
            if (!IsTable(focusSlot)) return;
            int r = vr == 0 ? 0 : vr + tableScroll;
            int cols = TableCols(focusSlot);
            if (vc >= cols)
            {
                if (vr == 0) { PushUndo(true); TableAddColumn(focusSlot); MarkChanged(); ScheduleRebuild(0.2); vc = TableCols(focusSlot) - 1; }
                else return;
            }
            if (r >= TableRowCount(focusSlot)) { PushUndo(true); TableAddRow(focusSlot); MarkChanged(); ScheduleRebuild(0.2); r = TableRowCount(focusSlot) - 1; }
            focusField = 100 + r * MAX_TCOLS + vc;
            caret = FieldText().Length;
            RefreshEditUI();
        }

        void EnsureVisible(int s)
        {
            if (s < scrollTop) scrollTop = s;
            if (s >= scrollTop + VROWS) scrollTop = s - VROWS + 1;
            if (scrollTop < 0) scrollTop = 0;
        }

        void EnsureTableRowVisible(int r)
        {
            if (r == 0) return;
            if (r - 1 < tableScroll) tableScroll = r - 1;
            if (r - 1 >= tableScroll + TROWS - 1) tableScroll = r - (TROWS - 1);
            if (tableScroll < 0) tableScroll = 0;
        }

        void AddRow(string text, bool silentUndo)
        {
            if (count >= MAX)
            {
                if (statusText != null) statusText.text = "<color=#c74440>Maximum of " + MAX + " expressions</color>";
                return;
            }
            if (!silentUndo) PushUndo(true);
            int at = count == 0 ? 0 : focusSlot + 1;
            if (at > count) at = count;
            for (int i = count; i > at; i--) CopySlot(i - 1, i);
            eText[at] = text; eMin[at] = ""; eMax[at] = ""; eStep[at] = "";
            eColor[at] = NextColor(); eHidden[at] = false; ePlay[at] = false; ePlayStart[at] = 0;
            count++;
            focusSlot = at; focusField = 0; caret = text.Length;
            EnsureVisible(at);
            AfterStructuralEdit();
        }

        int NextColor()
        {
            int c = 0;
            for (int i = 0; i < count; i++) c = (eColor[i] + 1) & 7;
            if (count == 0) c = 0;
            return c;
        }

        void AddTable()
        {
            if (count >= MAX) return;
            int n = 1;
            for (int i = 0; i < count; i++) if (IsTable(i)) n++;
            string t = TABLE_MARK.ToString() + "x_" + n + TBL_COL.ToString() + "y_" + n;
            for (int r = 0; r < 4; r++) t += TBL_ROW.ToString() + TBL_COL.ToString();
            AddRow(t, false);
            focusField = 100 + MAX_TCOLS;
            tableScroll = 0;
            caret = 0;
            RefreshEditUI();
        }

        void CopySlot(int from, int to)
        {
            eText[to] = eText[from]; eMin[to] = eMin[from]; eMax[to] = eMax[from]; eStep[to] = eStep[from];
            eColor[to] = eColor[from]; eHidden[to] = eHidden[from]; ePlay[to] = ePlay[from]; ePlayStart[to] = ePlayStart[from];
        }

        void DeleteRow(int s)
        {
            if (s < 0 || s >= count) return;
            PushUndo(true);
            for (int i = s; i < count - 1; i++) CopySlot(i + 1, i);
            count--;
            eText[count] = ""; eMin[count] = ""; eMax[count] = ""; eStep[count] = ""; ePlay[count] = false;
            if (focusSlot >= count) focusSlot = count > 0 ? count - 1 : 0;
            focusField = 0;
            caret = FieldText().Length;
            if (scrollTop > 0 && scrollTop + VROWS > count + 1) scrollTop = count + 1 - VROWS;
            if (scrollTop < 0) scrollTop = 0;
            AfterStructuralEdit();
        }

        void ResetAll()
        {
            PushUndo(true);
            for (int i = 0; i < MAX; i++) { eText[i] = ""; eMin[i] = ""; eMax[i] = ""; eStep[i] = ""; ePlay[i] = false; eHidden[i] = false; eColor[i] = i % 8; }
            count = 1;
            focusSlot = 0; focusField = 0; caret = 0; scrollTop = 0;
            SetViewLocal(0, 0, 10);
            AfterStructuralEdit();
        }

        void ToggleVisible(int r)
        {
            int s = scrollTop + r;
            if (s < 0 || s >= count) return;
            eHidden[s] = !eHidden[s];
            engine.SetSlotStyle(s, palette[eColor[s] & 7], !eHidden[s]);
            MarkChanged();
            UpdateRowsUI();
        }

        void CycleColor(int r)
        {
            int s = scrollTop + r;
            if (s < 0 || s >= count) return;
            eColor[s] = (eColor[s] + 1) & 7;
            engine.SetSlotStyle(s, palette[eColor[s]], !eHidden[s]);
            MarkChanged();
            UpdateRowsUI();
        }

        void AddSliderFor(int s)
        {
            if (s < 0 || s >= count) return;
            string nm = compiler.slotUndef[s];
            if (nm == null || nm.Length == 0) return;
            int keepFocus = focusSlot;
            focusSlot = s > 0 ? s - 1 : 0;
            if (s == 0)
            {
                // insert at the very top
                PushUndo(true);
                for (int i = count; i > 0; i--) CopySlot(i - 1, i);
                eText[0] = nm + "=1"; eMin[0] = ""; eMax[0] = ""; eStep[0] = ""; eColor[0] = NextColor(); eHidden[0] = false; ePlay[0] = false;
                count++;
                focusSlot = keepFocus + 1;
                caret = FieldText().Length;
                AfterStructuralEdit();
                return;
            }
            // insert before s
            PushUndo(true);
            for (int i = count; i > s; i--) CopySlot(i - 1, i);
            eText[s] = nm + "=1"; eMin[s] = ""; eMax[s] = ""; eStep[s] = ""; eColor[s] = NextColor(); eHidden[s] = false; ePlay[s] = false;
            count++;
            focusSlot = keepFocus >= s ? keepFocus + 1 : keepFocus;
            focusField = 0;
            caret = FieldText().Length;
            AfterStructuralEdit();
        }

        // =================================================================== sliders
        bool IsNameStart(char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= 'α' && c <= 'ω'); }

        /// <summary>Returns the variable name if text is "name = number", else "".</summary>
        string SliderName(string t)
        {
            if (t == null) return "";
            int n = t.Length;
            int i = 0;
            while (i < n && t[i] == ' ') i++;
            if (i >= n || !IsNameStart(t[i])) return "";
            int st = i;
            i++;
            if (i < n && t[i] == '_')
            {
                i++;
                if (i < n && t[i] == '{') { while (i < n && t[i] != '}') i++; if (i < n) i++; }
                else while (i < n && IsAlnum(t[i]) && t[i] != '.') i++;
            }
            string nm = t.Substring(st, i - st).Replace("{", "").Replace("}", "");
            if (nm == "x" || nm == "y" || nm == "r" || nm == "e" || nm == "i" || nm == "θ") return "";
            while (i < n && t[i] == ' ') i++;
            if (i >= n || t[i] != '=') return "";
            i++;
            while (i < n && t[i] == ' ') i++;
            if (i < n && (t[i] == '-' || t[i] == '−')) i++;
            int digits = 0;
            bool dot = false;
            while (i < n && ((t[i] >= '0' && t[i] <= '9') || (t[i] == '.' && !dot)))
            {
                if (t[i] == '.') dot = true; else digits++;
                i++;
            }
            while (i < n && t[i] == ' ') i++;
            if (digits == 0 || i != n) return "";
            return nm;
        }

        double SliderValueOf(string t)
        {
            int eq = t.IndexOf('=');
            if (eq < 0) return double.NaN;
            return ParseNumber(t.Substring(eq + 1));
        }

        double SliderMinOf(int s)
        {
            double v = ParseNumber(eMin[s]);
            if (!double.IsNaN(v)) return v;
            double cur = compiler.slotSliderValue[s];
            double lim = 10;
            while (System.Math.Abs(cur) > lim && lim < 1e12) lim *= 10;
            return -lim;
        }

        double SliderMaxOf(int s)
        {
            double v = ParseNumber(eMax[s]);
            if (!double.IsNaN(v)) return v;
            double cur = compiler.slotSliderValue[s];
            double lim = 10;
            while (System.Math.Abs(cur) > lim && lim < 1e12) lim *= 10;
            return lim;
        }

        string SliderValueText(int s, double v)
        {
            double step = ParseNumber(eStep[s]);
            double lo = SliderMinOf(s), hi = SliderMaxOf(s);
            int dec;
            if (!double.IsNaN(step) && step > 0)
            {
                v = lo + System.Math.Round((v - lo) / step) * step;
                dec = 0;
                double st = step;
                while (dec < 8 && System.Math.Abs(st - System.Math.Round(st)) > 1e-9) { st *= 10; dec++; }
            }
            else
            {
                double range = System.Math.Abs(hi - lo);
                if (range <= 0) range = 1;
                dec = (int)System.Math.Ceiling(-System.Math.Log10(range / 1000.0));
                if (dec < 0) dec = 0;
                if (dec > 8) dec = 8;
            }
            return engine.FormatFixed(v, dec);
        }

        void SliderMoved(int r)
        {
            int s = scrollTop + r;
            if (s < 0 || s >= count || rowSlider == null || r >= rowSlider.Length) return;
            if (s >= compiler.slotCount || !compiler.slotIsSlider[s]) return;
            float t = rowSlider[r].value;
            double lo = SliderMinOf(s), hi = SliderMaxOf(s);
            double v = lo + (hi - lo) * t;
            string vs = SliderValueText(s, v);
            PushUndo(false);
            eText[s] = compiler.slotDefName[s] + "=" + vs;
            if (ePlay[s]) ePlay[s] = false;
            compiler.SetSliderValue(s, ParseNumber(vs));
            engine.EvaluateDefinitions();
            MarkDirtyForSliderChange();
            MarkChanged();
            if (s == focusSlot && focusField == 0) { caret = eText[s].Length; SyncKbd(); }
            UpdateRowsUI();
        }

        void TogglePlay(int s)
        {
            if (s < 0 || s >= count || s >= compiler.slotCount || !compiler.slotIsSlider[s]) return;
            if (ePlay[s])
            {
                // stop: commit current animated value to text
                ePlay[s] = false;
                eText[s] = compiler.slotDefName[s] + "=" + SliderValueText(s, compiler.slotSliderValue[s]);
            }
            else
            {
                ePlay[s] = true;
                // choose start so the animation continues from the current value
                double lo = SliderMinOf(s), hi = SliderMaxOf(s);
                double frac = hi > lo ? (compiler.slotSliderValue[s] - lo) / (hi - lo) : 0;
                if (frac < 0) frac = 0; if (frac > 1) frac = 1;
                ePlayStart[s] = Networking.GetServerTimeInMilliseconds() - (int)(frac * 5000.0);
            }
            MarkChanged();
            UpdateRowsUI();
        }

        void AnimateSliders(double now)
        {
            if (now - lastAnimTime < 1.0 / 30.0) return;
            bool any = false;
            int server = 0;
            for (int s = 0; s < count; s++)
            {
                if (!ePlay[s]) continue;
                if (s >= compiler.slotCount || !compiler.slotIsSlider[s]) continue;
                if (!any) { server = Networking.GetServerTimeInMilliseconds(); any = true; }
                double lo = SliderMinOf(s), hi = SliderMaxOf(s);
                double ph = ((server - ePlayStart[s]) % 10000 + 10000) % 10000 / 5000.0; // 0..2
                double frac = ph <= 1 ? ph : 2 - ph;
                double v = lo + (hi - lo) * frac;
                double step = ParseNumber(eStep[s]);
                if (!double.IsNaN(step) && step > 0) v = lo + System.Math.Round((v - lo) / step) * step;
                compiler.SetSliderValue(s, v);
            }
            if (!any) return;
            lastAnimTime = now;
            engine.EvaluateDefinitions();
            long mask = 0;
            for (int s = 0; s < count; s++) if (ePlay[s] && s < compiler.slotCount && compiler.slotIsSlider[s] && compiler.slotDef[s] >= 0) mask |= 1L << compiler.slotDef[s];
            engine.MarkDirtyByDefMask(mask);
            UpdateRowsUI();
        }

        // =================================================================== view
        void ZoomBy(double f)
        {
            double hw = viewHw * f;
            if (hw < 1e-9) hw = 1e-9;
            if (hw > 1e12) hw = 1e12;
            SetViewLocal(viewCx, viewCy, hw);
            MarkChanged();
        }

        void OnPan()
        {
            if (panScroll == null || panScroll.content == null) return;
            Vector2 p = panScroll.content.anchoredPosition;
            if (!panInit) { lastPanPos = p; panInit = true; return; }
            Vector2 d = p - lastPanPos;
            lastPanPos = p;
            if (d.sqrMagnitude < 1e-6) return;
            if (d.sqrMagnitude > 250000f) return; // ignore jumps (content reset)
            double gpp = 2.0 * viewHw / panCanvasWidthPx;
            SetViewLocal(viewCx - d.x * gpp, viewCy - d.y * gpp, viewHw);
            MarkChanged();
        }

        void SetViewLocal(double cx, double cy, double hw)
        {
            viewCx = cx; viewCy = cy; viewHw = hw;
            engine.SetView(cx, cy, hw);
            UpdateGrid();
        }

        double NiceStep(double raw)
        {
            double p = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(raw)));
            double m = raw / p;
            double mant = m <= 1 ? 1 : (m <= 2 ? 2 : (m <= 5 ? 5 : 10));
            return mant * p;
        }

        double Frac(double v) { return v - System.Math.Floor(v); }

        void UpdateGrid()
        {
            double hw = viewHw, hh = viewHw * boardHeight / boardWidth;
            double major = NiceStep(2 * hw / 10.0);
            double mant = major / System.Math.Pow(10, System.Math.Floor(System.Math.Log10(major) + 1e-9));
            float minorDiv = (mant > 1.5 && mant < 2.5) ? 4f : 5f;
            double x0 = viewCx - hw, y0 = viewCy - hh;
            if (gridRenderer != null)
            {
                if (gridMpb == null) gridMpb = new MaterialPropertyBlock();
                MaterialPropertyBlock mpb = gridMpb;
                gridRenderer.GetPropertyBlock(mpb);
                mpb.SetVector("_MajorX", new Vector4((float)Frac(x0 / major), (float)(2 * hw / major), minorDiv, (float)((0 - x0) / (2 * hw))));
                mpb.SetVector("_MajorY", new Vector4((float)Frac(y0 / major), (float)(2 * hh / major), minorDiv, (float)((0 - y0) / (2 * hh))));
                mpb.SetFloat("_ShowGrid", gridHidden ? 0f : 1f);
                gridRenderer.SetPropertyBlock(mpb);
            }
            UpdateLabels(major, hw, hh);
        }

        void UpdateLabels(double major, double hw, double hh)
        {
            double k = boardWidth / (2 * hw);
            float halfW = boardWidth * 0.5f, halfH = boardHeight * 0.5f;
            // y position of x-axis labels (just below the axis, clamped to board)
            float axisY = (float)((0 - viewCy) * k);
            float xlY = axisY - 0.03f;
            if (xlY < -halfH + 0.02f) xlY = -halfH + 0.02f;
            if (xlY > halfH - 0.03f) xlY = halfH - 0.03f;
            float axisX = (float)((0 - viewCx) * k);
            float ylX = axisX - 0.012f;
            bool yLeft = true;
            if (ylX < -halfW + 0.06f) { ylX = -halfW + 0.012f; yLeft = false; }
            if (ylX > halfW - 0.01f) ylX = halfW - 0.012f;

            int i0 = (int)System.Math.Ceiling((viewCx - hw) / major);
            int li = 0;
            if (xLabels != null)
            {
                for (int i = i0; li < xLabels.Length; i++)
                {
                    double gx = i * major;
                    if (gx > viewCx + hw) break;
                    TextMeshPro lbl = xLabels[li];
                    if (lbl == null) { li++; continue; }
                    float bx = (float)((gx - viewCx) * k);
                    if (i == 0) bx -= 0.012f; // "0" sits just left of the y-axis
                    bool outside = bx < -halfW + 0.02f || bx > halfW - 0.02f;
                    if (gridHidden || outside)
                    {
                        lbl.gameObject.SetActive(false);
                        li++;
                        continue;
                    }
                    lbl.gameObject.SetActive(true);
                    lbl.text = LabelText(i == 0 ? 0 : gx);
                    ((TMP_Text)lbl).alignment = TextAlignmentOptions.Top;
                    lbl.gameObject.transform.localPosition = new Vector3(bx, xlY, -0.004f);
                    li++;
                }
                for (; li < xLabels.Length; li++) if (xLabels[li] != null) xLabels[li].gameObject.SetActive(false);
            }
            int j0 = (int)System.Math.Ceiling((viewCy - hh) / major);
            int lj = 0;
            if (yLabels != null)
            {
                for (int j = j0; lj < yLabels.Length; j++)
                {
                    double gy = j * major;
                    if (gy > viewCy + hh) break;
                    TextMeshPro lbl = yLabels[lj];
                    if (lbl == null) { lj++; continue; }
                    float by = (float)((gy - viewCy) * k);
                    if (gridHidden || j == 0 || by < -halfH + 0.02f || by > halfH - 0.02f) { lbl.gameObject.SetActive(false); lj++; continue; }
                    lbl.gameObject.SetActive(true);
                    lbl.text = LabelText(gy);
                    ((TMP_Text)lbl).alignment = yLeft ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
                    lbl.gameObject.transform.localPosition = new Vector3(ylX, by, -0.004f);
                    lj++;
                }
                for (; lj < yLabels.Length; lj++) if (yLabels[lj] != null) yLabels[lj].gameObject.SetActive(false);
            }
        }

        string LabelText(double v)
        {
            if (System.Math.Abs(v) < 1e-300) return "0";
            string s = engine.FormatNum(v, 6);
            int p = s.IndexOf('^');
            if (p > 0) s = s.Substring(0, p) + "<sup>" + s.Substring(p + 1) + "</sup>";
            return s.Replace("-", "−");
        }

        // =================================================================== UI refresh
        void ShowKeypadPage(int p)
        {
            if (keypadPages == null) return;
            for (int i = 0; i < keypadPages.Length; i++)
            {
                if (keypadPages[i] != null) keypadPages[i].SetActive(i == p);
                if (keypadTabBg != null && i < keypadTabBg.Length && keypadTabBg[i] != null)
                    keypadTabBg[i].color = i == p ? new Color(0.80f, 0.86f, 0.95f) : new Color(0.93f, 0.93f, 0.93f);
            }
        }

        void UpdateSettingLabels()
        {
            if (complexLabel != null) complexLabel.text = complexMode ? "<b>C on</b>" : "C off";
            if (degLabel != null) degLabel.text = degreeMode ? "<b>DEG</b>" : "RAD";
            if (gridLabel != null) gridLabel.text = gridHidden ? "Grid off" : "Grid";
            if (lockLabel != null) lockLabel.text = locked ? "<b>Locked</b>" : "Unlocked";
        }

        string OwnerName()
        {
            VRCPlayerApi o = Networking.GetOwner(gameObject);
            if (o == null || !Utilities.IsValid(o)) return "?";
            return o.displayName;
        }

        void UpdateStatus()
        {
            string s = "Editing: " + OwnerName();
            if (engine.IsBusy() || rebuildPending) s += "   <color=#2d70b3>computing…</color>";
            statusText.text = s;
        }

        void UpdateRowsUI()
        {
            if (rowRoot == null) return;
            int sc = compiler.slotCount;
            for (int r = 0; r < VROWS && r < rowRoot.Length; r++)
            {
                int s = scrollTop + r;
                GameObject root = rowRoot[r];
                if (root == null) continue;
                if (s > count || (s == count && count >= MAX))
                {
                    root.SetActive(false);
                    continue;
                }
                root.SetActive(true);
                bool focused = s == focusSlot && s < count;
                if (rowBg != null && r < rowBg.Length && rowBg[r] != null)
                    rowBg[r].color = focused ? new Color(0.90f, 0.94f, 1.0f) : Color.white;
                if (rowNum != null && rowNum[r] != null) rowNum[r].text = s < count ? (s + 1).ToString() : "+";
                bool showSlider = false;
                bool showAddSlider = false;
                if (s == count)
                {
                    rowText[r].text = "<color=#999999><i>tap to add an expression</i></color>";
                    rowInfo[r].text = "";
                    if (rowSwatch[r] != null) rowSwatch[r].color = new Color(0.85f, 0.85f, 0.85f);
                    if (rowSwatchLabel != null && rowSwatchLabel[r] != null) rowSwatchLabel[r].text = "";
                }
                else
                {
                    string txt = eText[s];
                    bool table = IsTable(s);
                    bool note = txt.Length > 0 && txt[0] == '"';
                    if (table) rowText[r].text = TableSummary(s);
                    else if (note) rowText[r].text = "<color=#555555><i>" + Pretty(txt.Substring(1), focused && focusField == 0 ? caret - 1 : -1) + "</i></color>";
                    else rowText[r].text = Pretty(txt, focused && focusField == 0 ? caret : -1);

                    int kind = s < sc ? compiler.slotKind[s] : GCCompiler.K_EMPTY;
                    string info = "";
                    if (kind == GCCompiler.K_ERROR)
                    {
                        info = "<color=#c74440>! " + Escape(compiler.slotError[s]) + "</color>";
                        string und = compiler.slotUndef[s];
                        showAddSlider = und != null && und.Length > 0;
                    }
                    else if (engine.slotDisplay[s] != null && engine.slotDisplay[s].Length > 0) info = "<color=#555555>" + Escape(engine.slotDisplay[s]) + "</color>";
                    if (engine.IsSlotBusy(s) && s < sc && compiler.slotPlottable[s]) info += (info.Length > 0 ? "  " : "") + "<color=#9aa>…</color>";
                    rowInfo[r].text = info;
                    showSlider = s < sc && compiler.slotIsSlider[s] && kind != GCCompiler.K_ERROR;

                    bool plottable = s < sc && compiler.slotPlottable[s];
                    Color c = palette[eColor[s] & 7];
                    if (rowSwatch[r] != null)
                    {
                        if (!plottable) rowSwatch[r].color = new Color(0.88f, 0.88f, 0.88f);
                        else rowSwatch[r].color = eHidden[s] ? new Color(c.r, c.g, c.b, 0.25f) : c;
                    }
                    if (rowSwatchLabel != null && rowSwatchLabel[r] != null)
                        rowSwatchLabel[r].text = plottable ? (eHidden[s] ? "<color=#888888>○</color>" : "") : "";
                }
                if (rowSliderGroup != null && r < rowSliderGroup.Length && rowSliderGroup[r] != null)
                {
                    rowSliderGroup[r].SetActive(showSlider);
                    if (showSlider)
                    {
                        double lo = SliderMinOf(s), hi = SliderMaxOf(s);
                        double v = compiler.slotSliderValue[s];
                        float t = hi > lo ? (float)((v - lo) / (hi - lo)) : 0f;
                        rowSlider[r].SetValueWithoutNotify(Mathf.Clamp01(t));
                        bool fMin = focused && focusField == 1, fMax = focused && focusField == 2, fStep = focused && focusField == 3;
                        rowMin[r].text = fMin ? FieldWithCaret(eMin[s]) : (eMin[s].Length > 0 ? Escape(eMin[s]) : "<color=#999999>" + engine.FormatNum(lo, 4) + "</color>");
                        rowMax[r].text = fMax ? FieldWithCaret(eMax[s]) : (eMax[s].Length > 0 ? Escape(eMax[s]) : "<color=#999999>" + engine.FormatNum(hi, 4) + "</color>");
                        if (rowStep != null && rowStep[r] != null)
                            rowStep[r].text = fStep ? "step " + FieldWithCaret(eStep[s]) : (eStep[s].Length > 0 ? "step " + Escape(eStep[s]) : "<color=#999999>step</color>");
                        if (rowPlay != null && rowPlay[r] != null) rowPlay[r].text = ePlay[s] ? "||" : "►";
                        if (ePlay[s] && !(focused && focusField == 0))
                            rowText[r].text = Pretty(compiler.slotDefName[s] + "=" + SliderValueText(s, v), -1);
                    }
                }
                if (rowAddSlider != null && r < rowAddSlider.Length && rowAddSlider[r] != null) rowAddSlider[r].SetActive(showAddSlider);
            }
            if (listScrollLabel != null)
            {
                int last = scrollTop + VROWS;
                if (last > count) last = count;
                listScrollLabel.text = count == 0 ? "" : ((scrollTop + 1) + "–" + last + " of " + count);
            }
            UpdateTableUI();
        }

        string FieldWithCaret(string f)
        {
            int c = caret;
            if (c > f.Length) c = f.Length;
            return Escape(f.Substring(0, c)) + CARET + Escape(f.Substring(c));
        }

        string TableSummary(int s)
        {
            int cols = TableCols(s);
            int rows = TableRowCount(s) - 1;
            string hdr = "";
            for (int c = 0; c < cols; c++) hdr += (c > 0 ? ", " : "") + Pretty(TableGet(s, 0, c), -1);
            return "<color=#333333><b>Table</b></color>  " + hdr + "  <color=#888888>(" + rows + " rows)</color>";
        }

        void UpdateTableUI()
        {
            if (tablePanel == null) return;
            bool show = IsTable(focusSlot) && focusField >= 100;
            tablePanel.SetActive(show);
            if (!show) return;
            int cols = TableCols(focusSlot);
            int rows = TableRowCount(focusSlot);
            if (tableTitle != null) tableTitle.text = "Table " + (focusSlot + 1) + "   <color=#888888>(" + (rows - 1) + " rows)</color>";
            int fr = (focusField - 100) / MAX_TCOLS, fc = (focusField - 100) % MAX_TCOLS;
            for (int vr = 0; vr < TROWS; vr++)
            {
                int r = vr == 0 ? 0 : vr + tableScroll;
                for (int vc = 0; vc < TCOLS; vc++)
                {
                    int idx = vr * TCOLS + vc;
                    if (tableCells == null || idx >= tableCells.Length || tableCells[idx] == null) continue;
                    string txt;
                    bool sel = r == fr && vc == fc;
                    if (vc >= cols) txt = vr == 0 ? "<color=#aaaaaa>+ column</color>" : "";
                    else if (r >= rows) txt = vc == 0 && r == rows ? "<color=#aaaaaa>+ row</color>" : "";
                    else
                    {
                        string cell = TableGet(focusSlot, r, vc);
                        txt = sel ? FieldWithCaret(cell) : (vr == 0 ? "<b>" + Pretty(cell, -1) + "</b>" : Escape(cell));
                    }
                    tableCells[idx].text = txt;
                    if (tableCellBg != null && idx < tableCellBg.Length && tableCellBg[idx] != null)
                        tableCellBg[idx].color = sel ? new Color(0.85f, 0.91f, 1f) : (vr == 0 ? new Color(0.93f, 0.93f, 0.93f) : Color.white);
                }
            }
        }

        string Escape(string s)
        {
            if (s == null) return "";
            if (s.IndexOf('<') < 0) return s;
            return s.Replace("<", "<noparse><</noparse>");
        }

        bool IsAlnum(char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || (c >= 'α' && c <= 'ω'); }

        int MatchParen(string s, int open)
        {
            int depth = 0;
            for (int i = open; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '(') depth++;
                else if (c == ')') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        /// <summary>Converts raw expression text to TMP rich text with superscripts, subscripts, symbols and caret.</summary>
        string Pretty(string s, int caretPos)
        {
            if (s == null) s = "";
            string o = "";
            int n = s.Length;
            int[] closeAt = new int[16];
            string[] closeTag = new string[16];
            int top = 0;
            int scriptRunEnd = -1;
            string scriptRunTag = "";
            for (int i = 0; i <= n; i++)
            {
                if (scriptRunEnd == i) { o += scriptRunTag; scriptRunEnd = -1; }
                if (i == caretPos) o += CARET;
                if (i == n) break;
                char c = s[i];
                if (top > 0 && closeAt[top - 1] == i) { o += closeTag[top - 1]; top--; continue; }
                if ((c == '^' || c == '_') && i + 1 < n && scriptRunEnd < 0)
                {
                    string open = c == '^' ? "<sup>" : "<sub>";
                    string close = c == '^' ? "</sup>" : "</sub>";
                    char nx = s[i + 1];
                    if ((nx == '(' || nx == '{') && top < 16)
                    {
                        int m = nx == '(' ? MatchParen(s, i + 1) : s.IndexOf('}', i + 1);
                        if (m > 0)
                        {
                            o += open;
                            closeAt[top] = m; closeTag[top] = close; top++;
                            if (caretPos == i + 1) o += CARET;
                            i++; // skip opening bracket
                            continue;
                        }
                    }
                    if (IsAlnum(nx) || nx == '-')
                    {
                        int j = i + 1;
                        if (s[j] == '-') j++;
                        if (j < n && s[j] >= '0' && s[j] <= '9') { while (j < n && ((s[j] >= '0' && s[j] <= '9') || s[j] == '.')) j++; }
                        else if (j < n && IsAlnum(s[j])) { j++; if (c == '_') while (j < n && IsAlnum(s[j])) j++; }
                        o += open;
                        scriptRunEnd = j;
                        scriptRunTag = close;
                        continue;
                    }
                    o += c.ToString();
                    continue;
                }
                if (c == '*') o += "·";
                else if (c == '<')
                {
                    if (i + 1 < n && s[i + 1] == '=') { o += "≤"; if (caretPos == i + 1) o += CARET; i++; }
                    else o += "<noparse><</noparse>";
                }
                else if (c == '>' && i + 1 < n && s[i + 1] == '=') { o += "≥"; if (caretPos == i + 1) o += CARET; i++; }
                else if (c == '-') o += "−";
                else if (c == TABLE_MARK || c == TBL_ROW || c == TBL_COL) { }
                else o += c.ToString();
            }
            if (scriptRunEnd >= 0) o += scriptRunTag;
            while (top > 0) { top--; o += closeTag[top]; }
            if (n == 0 && caretPos < 0) o = "";
            return o;
        }

        // =================================================================== parsing helpers (culture-invariant)
        int ParseIntSafe(string s)
        {
            if (s == null) return 0;
            int v = 0; bool neg = false; int i = 0;
            if (i < s.Length && s[i] == '-') { neg = true; i++; }
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') break;
                v = v * 10 + (c - '0');
            }
            return neg ? -v : v;
        }

        double ParseNumber(string s)
        {
            if (s == null) return double.NaN;
            s = s.Trim();
            if (s.Length == 0) return double.NaN;
            int i = 0; bool neg = false;
            if (s[i] == '-' || s[i] == '−') { neg = true; i++; }
            else if (s[i] == '+') i++;
            double v = 0; bool any = false; double scale = 0;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9')
                {
                    any = true;
                    if (scale == 0) v = v * 10 + (c - '0');
                    else { v += (c - '0') * scale; scale *= 0.1; }
                }
                else if (c == '.' && scale == 0) scale = 0.1;
                else break;
            }
            // optional trailing pi / π multiplier
            if (i < s.Length)
            {
                string rest = s.Substring(i).Trim();
                if (rest == "π" || rest == "pi") { v = (any ? v : 1) * System.Math.PI; any = true; i = s.Length; }
                else return double.NaN;
            }
            if (!any) return double.NaN;
            return neg ? -v : v;
        }
    }
}
