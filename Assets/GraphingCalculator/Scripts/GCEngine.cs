using UdonSharp;
using UnityEngine;

namespace GraphCalc
{
    /// <summary>
    /// Vectorised bytecode evaluator (real + complex), definition evaluation, regression,
    /// adaptive curve sampling, implicit-grid sampling and mesh / texture generation.
    /// Work is time-sliced via Work(budgetMs).
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GCEngine : UdonSharpBehaviour
    {
        // ------------------------------------------------------------------ configuration
        public GCCompiler compiler;
        public MeshFilter[] lineFilters;
        public MeshRenderer[] lineRenderers;
        public MeshFilter[] fillFilters;
        public MeshRenderer[] fillRenderers;
        public MeshFilter[] pointFilters;
        public MeshRenderer[] pointRenderers;
        public MeshRenderer[] implicitRenderers;
        public float boardWidth = 2.0f;
        public float boardHeight = 1.5f;
        [Tooltip("Initial samples per curve (PC). Quest uses questBaseSamples.")]
        public int baseSamples = 384;
        public int questBaseSamples = 224;
        public int maxCurvePoints = 3000;
        public int questMaxCurvePoints = 1600;
        [Tooltip("Implicit grid columns (max 255).")]
        public int implicitRes = 160;
        public int questImplicitRes = 96;
        public int refineLevels = 5;

        // ------------------------------------------------------------------ constants
        const int CHUNK = 256;
        const int MAX_STACK = 64;
        const int MAX_VARS = 32;
        const int MAX_PARAMS = 256;
        const int MAX_LISTS = 128;
        const int LIST_POOL = 32768;
        const int MAX_SEG = 12000;
        const int MAX_FILL = 6000;
        const int MAX_POINTS = 4000;
        const int MAX_CURVE = 4096;
        const int MAX_SUM_TERMS = 20000;
        const int MED_MAX = 16384;
        const int MAX_REG_ROWS = 2048;
        const int MAX_REG_PARAMS = 8;
        const int MAX_GRID = 256;
        const int SLOTS = 16;

        // opcode mirrors (kept local for speed / clarity)
        const int OP_HDR = 1;
        const int OP_CONST = 2;
        const int OP_VAR = 3;
        const int OP_PARAM = 4;
        const int OP_LISTVAR = 5;
        const int OP_LISTLIT = 6;
        const int OP_RANGE = 7;
        const int OP_ADD = 8;
        const int OP_SUB = 9;
        const int OP_MUL = 10;
        const int OP_DIV = 11;
        const int OP_POW = 12;
        const int OP_NEG = 13;
        const int OP_FN1 = 14;
        const int OP_FN2 = 15;
        const int OP_CMP = 16;
        const int OP_AND = 17;
        const int OP_SELECT = 18;
        const int OP_DERIV = 19;
        const int OP_SUM = 20;
        const int OP_PROD = 21;
        const int OP_INT = 22;
        const int OP_REDUCE = 23;
        const int OP_INDEX = 24;
        const int HDR_SIZE = 10;

        // ------------------------------------------------------------------ program data (from compiler)
        int[] code;
        double[] cst;
        int[] progStart;
        int[] progEnd;
        bool cplx;

        // ------------------------------------------------------------------ VM state
        double[][] sRe;
        double[][] sIm;
        bool[] sSc;
        double[] sScRe;
        double[] sScIm;
        double[][] vRe;
        double[][] vIm;
        bool[] vSc;
        double[] vScRe;
        double[] vScIm;
        [HideInInspector] public double[] pRe;
        [HideInInspector] public double[] pIm;
        int[] lStart;
        int[] lLen;
        double[] lRe;
        double[] lIm;
        int lUsed;
        double[] medBuf;

        // complex helper outputs
        double tRe;
        double tIm;

        // range info outputs
        double rFirst;
        double rStep;
        int rCount;

        // integration node outputs
        double iT;
        double iW;
        double[] glX;
        double[] glW;

        // ------------------------------------------------------------------ display / status (read by controller)
        [HideInInspector] public string[] slotDisplay;
        [HideInInspector] public bool[] slotVisible;
        [HideInInspector] public Color[] slotColor;

        // ------------------------------------------------------------------ view
        double viewCx;
        double viewCy;
        double viewHw = 10;
        double viewHh = 7.5;

        // per-slot sampled state
        double[] slotOx;
        double[] slotOy;
        double[] slotS;
        double[] slotHwAtSample;
        double[] slotDomX0;
        double[] slotDomX1;
        double[] slotDomY0;
        double[] slotDomY1;
        bool[] slotUsesDomX;
        bool[] slotUsesDomY;
        bool[] slotHasImplicit;
        double[] slotGx0;
        double[] slotGy0;
        double[] slotGcx;
        double[] slotGcy;
        int[] slotGw;
        int[] slotGh;
        int[] slotImplicitMode;
        bool[] slotDashed;
        bool[] slotHasLines;
        bool[] slotHasFill;
        bool[] slotHasPoints;
        bool[] slotDirty;
        bool[] slotEverSampled;
        Texture2D[] slotTex;
        Color[][] slotTexData;
        Mesh[] lineMeshes;
        Mesh[] fillMeshes;
        Mesh[] pointMeshes;
        MaterialPropertyBlock mpb;

        // ------------------------------------------------------------------ job state
        int curSlot = -1;
        bool restartSlot;
        int jobStage;
        int[] jobEntries;
        int jobEntryCount;
        int jobEntryIdx;
        int jobK;
        int jobB;
        int jobCursor;
        int jobLevel;
        double jobVcx;
        double jobVcy;
        double jobHw;
        double jobHh;
        double jobLo;
        double jobHi;

        // curve buffers
        double[] cu;
        double[] cpx;
        double[] cpy;
        double[] nu;
        double[] npx;
        double[] npy;
        int cm;
        double[] mu;
        double[] mpx;
        double[] mpy;
        bool[] refineFlag;
        int mc;
        double[] tmpA;

        // geometry buffers
        double[] segAx;
        double[] segAy;
        double[] segBx;
        double[] segBy;
        double[] segArcA;
        double[] segArcB;
        int segCount;
        double[] fillQ; // 8 doubles per quad
        int fillCount;
        double[] ptX;
        double[] ptY;
        int ptCount;

        // implicit buffers
        double[] gridF;
        double[] gridD;
        double[] gridV;
        double[] gridGX;
        double[] gridGY;
        double[] gridDk;
        double[] gridVk;
        double[] gridXs;
        int gridW;
        int gridH;
        double gridX0;
        double gridY0;
        double gridCx;
        double gridCy;
        int gridMode;
        bool gridRegion;

        // mesh arrays
        Vector3[] mVerts;
        Vector4[] mUv0;
        Vector4[] mUv1;
        int[] mTriLine;
        int[] mTriQuad;

        // regression buffers
        double[] regR;
        double[] regR2;
        double[] regJ;
        double[] regA;
        double[] regG;
        double[] regP;
        double[] regPNew;
        double[] regDelta;
        double[] regY;

        bool initialized;
        bool isQuest;
        string dbgInfo = "";
        [HideInInspector] public string[] dbgSlotInfo;

        // =================================================================== setup
        void Start()
        {
            Init();
        }

        public void Init()
        {
            if (initialized) return;
            initialized = true;
#if UNITY_ANDROID
            isQuest = true;
#endif
            if (isQuest)
            {
                baseSamples = questBaseSamples;
                maxCurvePoints = questMaxCurvePoints;
                implicitRes = questImplicitRes;
            }
            if (maxCurvePoints > MAX_CURVE) maxCurvePoints = MAX_CURVE;
            if (implicitRes > MAX_GRID - 1) implicitRes = MAX_GRID - 1;

            sRe = new double[MAX_STACK][];
            sIm = new double[MAX_STACK][];
            for (int i = 0; i < MAX_STACK; i++) { sRe[i] = new double[CHUNK]; sIm[i] = new double[CHUNK]; }
            sSc = new bool[MAX_STACK];
            sScRe = new double[MAX_STACK];
            sScIm = new double[MAX_STACK];
            vRe = new double[MAX_VARS][];
            vIm = new double[MAX_VARS][];
            for (int i = 0; i < MAX_VARS; i++) { vRe[i] = new double[CHUNK]; vIm[i] = new double[CHUNK]; }
            vSc = new bool[MAX_VARS];
            vScRe = new double[MAX_VARS];
            vScIm = new double[MAX_VARS];
            pRe = new double[MAX_PARAMS];
            pIm = new double[MAX_PARAMS];
            lStart = new int[MAX_LISTS];
            lLen = new int[MAX_LISTS];
            lRe = new double[LIST_POOL];
            lIm = new double[LIST_POOL];
            medBuf = new double[MED_MAX];

            glX = new double[] { -0.9739065285171717, -0.8650633666889845, -0.6794095682990244, -0.4333953941292472, -0.1488743389816312,
                                 0.1488743389816312, 0.4333953941292472, 0.6794095682990244, 0.8650633666889845, 0.9739065285171717 };
            glW = new double[] { 0.0666713443086881, 0.1494513491505806, 0.2190863625159820, 0.2692667193099963, 0.2955242247147529,
                                 0.2955242247147529, 0.2692667193099963, 0.2190863625159820, 0.1494513491505806, 0.0666713443086881 };

            slotDisplay = new string[SLOTS];
            dbgSlotInfo = new string[SLOTS];
            slotVisible = new bool[SLOTS];
            slotColor = new Color[SLOTS];
            slotOx = new double[SLOTS];
            slotOy = new double[SLOTS];
            slotS = new double[SLOTS];
            slotHwAtSample = new double[SLOTS];
            slotDomX0 = new double[SLOTS];
            slotDomX1 = new double[SLOTS];
            slotDomY0 = new double[SLOTS];
            slotDomY1 = new double[SLOTS];
            slotUsesDomX = new bool[SLOTS];
            slotUsesDomY = new bool[SLOTS];
            slotHasImplicit = new bool[SLOTS];
            slotGx0 = new double[SLOTS];
            slotGy0 = new double[SLOTS];
            slotGcx = new double[SLOTS];
            slotGcy = new double[SLOTS];
            slotGw = new int[SLOTS];
            slotGh = new int[SLOTS];
            slotImplicitMode = new int[SLOTS];
            slotDashed = new bool[SLOTS];
            slotHasLines = new bool[SLOTS];
            slotHasFill = new bool[SLOTS];
            slotHasPoints = new bool[SLOTS];
            slotDirty = new bool[SLOTS];
            slotEverSampled = new bool[SLOTS];
            slotTex = new Texture2D[SLOTS];
            slotTexData = new Color[SLOTS][];
            for (int s = 0; s < SLOTS; s++) { slotDisplay[s] = ""; slotVisible[s] = true; slotColor[s] = Color.black; slotS[s] = 1; }

            jobEntries = new int[64];
            cu = new double[MAX_CURVE + 8];
            cpx = new double[MAX_CURVE + 8];
            cpy = new double[MAX_CURVE + 8];
            nu = new double[MAX_CURVE + 8];
            npx = new double[MAX_CURVE + 8];
            npy = new double[MAX_CURVE + 8];
            mu = new double[MAX_CURVE + 8];
            mpx = new double[MAX_CURVE + 8];
            mpy = new double[MAX_CURVE + 8];
            refineFlag = new bool[MAX_CURVE + 8];
            tmpA = new double[CHUNK];

            segAx = new double[MAX_SEG];
            segAy = new double[MAX_SEG];
            segBx = new double[MAX_SEG];
            segBy = new double[MAX_SEG];
            segArcA = new double[MAX_SEG];
            segArcB = new double[MAX_SEG];
            fillQ = new double[MAX_FILL * 8];
            ptX = new double[MAX_POINTS];
            ptY = new double[MAX_POINTS];

            int gn = MAX_GRID * MAX_GRID;
            gridF = new double[gn];
            gridD = new double[gn];
            gridV = new double[gn];
            gridGX = new double[gn];
            gridGY = new double[gn];
            gridDk = new double[gn];
            gridVk = new double[gn];
            gridXs = new double[CHUNK];

            int mv = MAX_SEG * 4;
            mVerts = new Vector3[mv];
            mUv0 = new Vector4[mv];
            mUv1 = new Vector4[mv];
            mTriLine = new int[MAX_SEG * 6];
            mTriQuad = new int[MAX_SEG * 6];
            for (int q = 0; q < MAX_SEG; q++)
            {
                int v = q * 4;
                int t = q * 6;
                mTriLine[t] = v; mTriLine[t + 1] = v + 1; mTriLine[t + 2] = v + 2;
                mTriLine[t + 3] = v + 2; mTriLine[t + 4] = v + 1; mTriLine[t + 5] = v + 3;
                mTriQuad[t] = v; mTriQuad[t + 1] = v + 1; mTriQuad[t + 2] = v + 2;
                mTriQuad[t + 3] = v; mTriQuad[t + 4] = v + 2; mTriQuad[t + 5] = v + 3;
            }

            regR = new double[MAX_REG_ROWS];
            regR2 = new double[MAX_REG_ROWS];
            regY = new double[MAX_REG_ROWS];
            regJ = new double[MAX_REG_ROWS * MAX_REG_PARAMS];
            regA = new double[MAX_REG_PARAMS * MAX_REG_PARAMS];
            regG = new double[MAX_REG_PARAMS];
            regP = new double[MAX_REG_PARAMS];
            regPNew = new double[MAX_REG_PARAMS];
            regDelta = new double[MAX_REG_PARAMS];

            mpb = new MaterialPropertyBlock();
            lineMeshes = new Mesh[SLOTS];
            fillMeshes = new Mesh[SLOTS];
            pointMeshes = new Mesh[SLOTS];
            Bounds bb = new Bounds(Vector3.zero, new Vector3(boardWidth, boardHeight, 0.2f));
            for (int s = 0; s < SLOTS; s++)
            {
                if (lineFilters != null && s < lineFilters.Length && lineFilters[s] != null)
                {
                    Mesh m = new Mesh(); m.MarkDynamic(); m.bounds = bb; lineFilters[s].sharedMesh = m; lineMeshes[s] = m;
                }
                if (fillFilters != null && s < fillFilters.Length && fillFilters[s] != null)
                {
                    Mesh m = new Mesh(); m.MarkDynamic(); m.bounds = bb; fillFilters[s].sharedMesh = m; fillMeshes[s] = m;
                }
                if (pointFilters != null && s < pointFilters.Length && pointFilters[s] != null)
                {
                    Mesh m = new Mesh(); m.MarkDynamic(); m.bounds = bb; pointFilters[s].sharedMesh = m; pointMeshes[s] = m;
                }
                SetRenderersEnabled(s, false, false, false, false);
            }
        }

        void SetRenderersEnabled(int s, bool lines, bool fill, bool points, bool implicitOn)
        {
            if (lineRenderers != null && s < lineRenderers.Length && lineRenderers[s] != null) lineRenderers[s].enabled = lines;
            if (fillRenderers != null && s < fillRenderers.Length && fillRenderers[s] != null) fillRenderers[s].enabled = fill;
            if (pointRenderers != null && s < pointRenderers.Length && pointRenderers[s] != null) pointRenderers[s].enabled = points;
            if (implicitRenderers != null && s < implicitRenderers.Length && implicitRenderers[s] != null) implicitRenderers[s].enabled = implicitOn;
        }

        /// <summary>Call after compiler.Build().</summary>
        public void Bind()
        {
            Init();
            code = compiler.code;
            cst = compiler.cst;
            progStart = compiler.progStart;
            progEnd = compiler.progEnd;
            cplx = compiler.complexMode;
            curSlot = -1;
        }

        // =================================================================== VM primitives
        void Materialize(int s, int n)
        {
            if (!sSc[s]) return;
            sSc[s] = false;
            double v = sScRe[s];
            double[] R = sRe[s];
            for (int i = 0; i < n; i++) R[i] = v;
            if (cplx)
            {
                double w = sScIm[s];
                double[] I = sIm[s];
                for (int i = 0; i < n; i++) I[i] = w;
            }
        }

        void SetNaN(int s)
        {
            sSc[s] = true; sScRe[s] = double.NaN; sScIm[s] = 0;
        }

        void SwapSlots(int a, int b)
        {
            double[] t = sRe[a]; sRe[a] = sRe[b]; sRe[b] = t;
            t = sIm[a]; sIm[a] = sIm[b]; sIm[b] = t;
            bool f = sSc[a]; sSc[a] = sSc[b]; sSc[b] = f;
            double d = sScRe[a]; sScRe[a] = sScRe[b]; sScRe[b] = d;
            d = sScIm[a]; sScIm[a] = sScIm[b]; sScIm[b] = d;
        }

        double GetRe(int s, int i) { return sSc[s] ? sScRe[s] : sRe[s][i]; }
        double GetIm(int s, int i) { if (!cplx) return 0; return sSc[s] ? sScIm[s] : sIm[s][i]; }

        void PushVar(int s, int v, int n)
        {
            if (vSc[v]) { sSc[s] = true; sScRe[s] = vScRe[v]; sScIm[s] = vScIm[v]; return; }
            sSc[s] = false;
            System.Array.Copy(vRe[v], sRe[s], n);
            if (cplx) System.Array.Copy(vIm[v], sIm[s], n);
        }

        public void SetVarScalar(int v, double val)
        {
            vSc[v] = true; vScRe[v] = val; vScIm[v] = 0;
        }

        void SetVarVector(int v, double[] src, int off, int n)
        {
            vSc[v] = false;
            System.Array.Copy(src, off, vRe[v], 0, n);
        }

        void PushListVar(int s, int l, int n, int k, bool kvec)
        {
            int len = lLen[l];
            int st = lStart[l];
            if (!kvec)
            {
                sSc[s] = true;
                if (k >= 0 && k < len) { sScRe[s] = lRe[st + k]; sScIm[s] = lIm[st + k]; }
                else { sScRe[s] = double.NaN; sScIm[s] = 0; }
                return;
            }
            sSc[s] = false;
            double[] R = sRe[s];
            double[] I = sIm[s];
            int avail = len - k;
            if (avail < 0) avail = 0;
            int cnt = avail < n ? avail : n;
            if (cnt > 0)
            {
                System.Array.Copy(lRe, st + k, R, 0, cnt);
                if (cplx) System.Array.Copy(lIm, st + k, I, 0, cnt);
            }
            for (int i = cnt; i < n; i++) { R[i] = double.NaN; I[i] = 0; }
        }

        // =================================================================== the interpreter
        [RecursiveMethod]
        void Exec(int pc, int end, int n, int sp, int k, bool kvec)
        {
            int s = sp;
            while (pc < end)
            {
                int op = code[pc];
                pc++;
                if (op == OP_CONST)
                {
                    int ci = code[pc]; pc++;
                    sSc[s] = true; sScRe[s] = cst[ci]; sScIm[s] = cst[ci + 1];
                    s++;
                }
                else if (op == OP_VAR) { PushVar(s, code[pc], n); pc++; s++; }
                else if (op == OP_PARAM)
                {
                    int p = code[pc]; pc++;
                    sSc[s] = true; sScRe[s] = pRe[p]; sScIm[s] = pIm[p];
                    s++;
                }
                else if (op >= OP_ADD && op <= OP_POW) { s--; Bin(op, s - 1, n); }
                else if (op == OP_FN1) { Fn1(code[pc], s - 1, n); pc++; }
                else if (op == OP_NEG) { Neg(s - 1, n); }
                else if (op == OP_HDR) { pc += HDR_SIZE - 1; }
                else if (op == OP_LISTVAR) { PushListVar(s, code[pc], n, k, kvec); pc++; s++; }
                else if (op == OP_FN2) { s--; Fn2(code[pc], s - 1, n); pc++; }
                else if (op == OP_CMP) { s--; Cmp(code[pc], s - 1, n); pc++; }
                else if (op == OP_AND) { s--; AndOp(s - 1, n); }
                else if (op == OP_SELECT) { s -= 2; SelectOp(s - 1, n); }
                else if (op == OP_LISTLIT) { pc = OpListLit(pc, s, n, k, kvec); s++; }
                else if (op == OP_RANGE) { pc = OpRange(pc, s, n, k, kvec); s++; }
                else if (op == OP_DERIV)
                {
                    int slot = code[pc]; int len = code[pc + 1]; pc += 2;
                    OpDeriv(slot, pc, pc + len, s - 1, n, k, kvec);
                    pc += len;
                }
                else if (op == OP_SUM || op == OP_PROD)
                {
                    int slot = code[pc]; int len = code[pc + 1]; pc += 2;
                    s--;
                    OpSum(op == OP_PROD, slot, pc, pc + len, s - 1, n, k, kvec);
                    pc += len;
                }
                else if (op == OP_INT)
                {
                    int slot = code[pc]; int len = code[pc + 1]; pc += 2;
                    s--;
                    OpInt(slot, pc, pc + len, s - 1, n, k, kvec);
                    pc += len;
                }
                else if (op == OP_REDUCE)
                {
                    int f = code[pc]; int len = code[pc + 1]; pc += 2;
                    OpReduce(f, pc, pc + len, s, n);
                    pc += len;
                    s++;
                }
                else if (op == OP_INDEX)
                {
                    int len = code[pc]; pc++;
                    OpIndex(pc, pc + len, s - 1, n);
                    pc += len;
                }
                else
                {
                    // unknown opcode: abort with NaN
                    SetNaN(sp);
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ arithmetic
        void Bin(int op, int a, int n)
        {
            int b = a + 1;
            if (cplx) { BinC(op, a, n); return; }
            bool sa = sSc[a];
            bool sb = sSc[b];
            if (sa && sb) { sScRe[a] = BinR(op, sScRe[a], sScRe[b]); return; }
            double[] A = sRe[a];
            if (sb)
            {
                double y = sScRe[b];
                if (op == OP_ADD) { for (int i = 0; i < n; i++) A[i] += y; }
                else if (op == OP_SUB) { for (int i = 0; i < n; i++) A[i] -= y; }
                else if (op == OP_MUL) { for (int i = 0; i < n; i++) A[i] *= y; }
                else if (op == OP_DIV) { for (int i = 0; i < n; i++) A[i] /= y; }
                else PowVecScalar(A, y, n);
                return;
            }
            double[] B = sRe[b];
            if (sa)
            {
                double x = sScRe[a];
                sSc[a] = false;
                if (op == OP_ADD) { for (int i = 0; i < n; i++) A[i] = x + B[i]; }
                else if (op == OP_SUB) { for (int i = 0; i < n; i++) A[i] = x - B[i]; }
                else if (op == OP_MUL) { for (int i = 0; i < n; i++) A[i] = x * B[i]; }
                else if (op == OP_DIV) { for (int i = 0; i < n; i++) A[i] = x / B[i]; }
                else { for (int i = 0; i < n; i++) A[i] = PowR(x, B[i]); }
                return;
            }
            if (op == OP_ADD) { for (int i = 0; i < n; i++) A[i] += B[i]; }
            else if (op == OP_SUB) { for (int i = 0; i < n; i++) A[i] -= B[i]; }
            else if (op == OP_MUL) { for (int i = 0; i < n; i++) A[i] *= B[i]; }
            else if (op == OP_DIV) { for (int i = 0; i < n; i++) A[i] /= B[i]; }
            else { for (int i = 0; i < n; i++) A[i] = PowR(A[i], B[i]); }
        }

        double BinR(int op, double x, double y)
        {
            if (op == OP_ADD) return x + y;
            if (op == OP_SUB) return x - y;
            if (op == OP_MUL) return x * y;
            if (op == OP_DIV) return x / y;
            return PowR(x, y);
        }

        // sign of odd-denominator rational exponent: 0 none, 1 negative result, 2 positive result
        int OddRootSign(double e)
        {
            for (int q = 3; q <= 15; q += 2)
            {
                double p = e * q;
                double rp = System.Math.Round(p);
                if (System.Math.Abs(p - rp) < 1e-9)
                {
                    if (System.Math.Abs(rp) > 1e9) return 0;
                    int ip = (int)rp;
                    return (ip % 2 != 0) ? 1 : 2;
                }
            }
            return 0;
        }

        double PowR(double b, double e)
        {
            if (b < 0 && e != System.Math.Floor(e) && !double.IsNaN(e) && !double.IsInfinity(e))
            {
                int sg = OddRootSign(e);
                if (sg == 0) return double.NaN;
                double m = System.Math.Pow(-b, e);
                return sg == 1 ? -m : m;
            }
            return System.Math.Pow(b, e);
        }

        void PowVecScalar(double[] A, double y, int n)
        {
            if (y == 2.0) { for (int i = 0; i < n; i++) A[i] = A[i] * A[i]; return; }
            if (y == 3.0) { for (int i = 0; i < n; i++) { double a = A[i]; A[i] = a * a * a; } return; }
            if (y == 1.0) return;
            if (y == 0.5) { for (int i = 0; i < n; i++) A[i] = System.Math.Sqrt(A[i]); return; }
            if (y == -1.0) { for (int i = 0; i < n; i++) A[i] = 1.0 / A[i]; return; }
            bool yInt = y == System.Math.Floor(y);
            int sg = yInt ? 0 : OddRootSign(y);
            for (int i = 0; i < n; i++)
            {
                double b = A[i];
                if (b < 0 && !yInt)
                {
                    if (sg == 0) A[i] = double.NaN;
                    else { double m = System.Math.Pow(-b, y); A[i] = sg == 1 ? -m : m; }
                }
                else A[i] = System.Math.Pow(b, y);
            }
        }

        void Neg(int a, int n)
        {
            if (sSc[a]) { sScRe[a] = -sScRe[a]; sScIm[a] = -sScIm[a]; return; }
            double[] A = sRe[a];
            for (int i = 0; i < n; i++) A[i] = -A[i];
            if (cplx) { double[] I = sIm[a]; for (int i = 0; i < n; i++) I[i] = -I[i]; }
        }

        // ------------------------------------------------------------------ complex arithmetic
        void CMul(double a, double b, double c, double d) { tRe = a * c - b * d; tIm = a * d + b * c; }

        void CDiv(double a, double b, double c, double d)
        {
            if (System.Math.Abs(c) >= System.Math.Abs(d))
            {
                if (c == 0 && d == 0) { tRe = double.NaN; tIm = double.NaN; return; }
                double r = d / c; double den = c + d * r;
                tRe = (a + b * r) / den; tIm = (b - a * r) / den;
            }
            else
            {
                double r = c / d; double den = c * r + d;
                tRe = (a * r + b) / den; tIm = (b * r - a) / den;
            }
        }

        double CAbs(double a, double b)
        {
            double x = System.Math.Abs(a), y = System.Math.Abs(b);
            if (x < y) { double t = x; x = y; y = t; }
            if (x == 0) return 0;
            double r = y / x;
            return x * System.Math.Sqrt(1 + r * r);
        }

        void CExp(double a, double b)
        {
            double m = System.Math.Exp(a);
            if (b == 0) { tRe = m; tIm = 0; return; }
            tRe = m * System.Math.Cos(b); tIm = m * System.Math.Sin(b);
        }

        void CLn(double a, double b)
        {
            tRe = System.Math.Log(CAbs(a, b));
            tIm = System.Math.Atan2(b, a);
        }

        void CSqrt(double a, double b)
        {
            if (b == 0)
            {
                if (a >= 0) { tRe = System.Math.Sqrt(a); tIm = 0; }
                else { tRe = 0; tIm = System.Math.Sqrt(-a); }
                return;
            }
            double r = CAbs(a, b);
            double re = System.Math.Sqrt((r + a) * 0.5);
            double im = System.Math.Sqrt((r - a) * 0.5);
            tRe = re; tIm = b < 0 ? -im : im;
        }

        void CPow(double a, double b, double c, double d)
        {
            if (d == 0 && c == System.Math.Floor(c) && System.Math.Abs(c) <= 64)
            {
                int e = (int)System.Math.Abs(c);
                double rr = 1, ri = 0, br = a, bi = b;
                while (e > 0)
                {
                    if ((e & 1) != 0) { double nr = rr * br - ri * bi; ri = rr * bi + ri * br; rr = nr; }
                    double sr = br * br - bi * bi; bi = 2 * br * bi; br = sr;
                    e >>= 1;
                }
                if (c < 0) { CDiv(1, 0, rr, ri); return; }
                tRe = rr; tIm = ri;
                return;
            }
            if (a == 0 && b == 0)
            {
                if (c > 0) { tRe = 0; tIm = 0; } else { tRe = double.NaN; tIm = double.NaN; }
                return;
            }
            if (b == 0 && d == 0 && a > 0) { tRe = System.Math.Pow(a, c); tIm = 0; return; }
            CLn(a, b);
            double lr = tRe, li = tIm;
            CMul(lr, li, c, d);
            CExp(tRe, tIm);
        }

        void BinC(int op, int a, int n)
        {
            int b = a + 1;
            if (sSc[a] && sSc[b])
            {
                BinCS(op, sScRe[a], sScIm[a], sScRe[b], sScIm[b]);
                sScRe[a] = tRe; sScIm[a] = tIm;
                return;
            }
            Materialize(a, n);
            Materialize(b, n);
            double[] AR = sRe[a], AI = sIm[a], BR = sRe[b], BI = sIm[b];
            if (op == OP_ADD) { for (int i = 0; i < n; i++) { AR[i] += BR[i]; AI[i] += BI[i]; } return; }
            if (op == OP_SUB) { for (int i = 0; i < n; i++) { AR[i] -= BR[i]; AI[i] -= BI[i]; } return; }
            for (int i = 0; i < n; i++)
            {
                BinCS(op, AR[i], AI[i], BR[i], BI[i]);
                AR[i] = tRe; AI[i] = tIm;
            }
        }

        void BinCS(int op, double a, double b, double c, double d)
        {
            if (op == OP_ADD) { tRe = a + c; tIm = b + d; }
            else if (op == OP_SUB) { tRe = a - c; tIm = b - d; }
            else if (op == OP_MUL) { CMul(a, b, c, d); }
            else if (op == OP_DIV) { CDiv(a, b, c, d); }
            else
            {
                if (b == 0 && d == 0 && (a >= 0 || c == System.Math.Floor(c))) { tRe = System.Math.Pow(a, c); tIm = 0; }
                else CPow(a, b, c, d);
            }
        }

        // ------------------------------------------------------------------ unary functions
        void Fn1(int f, int a, int n)
        {
            if (sSc[a])
            {
                if (cplx) { C1(f, sScRe[a], sScIm[a]); sScRe[a] = tRe; sScIm[a] = tIm; }
                else sScRe[a] = F1R(f, sScRe[a]);
                return;
            }
            double[] A = sRe[a];
            if (cplx)
            {
                double[] I = sIm[a];
                for (int i = 0; i < n; i++) { C1(f, A[i], I[i]); A[i] = tRe; I[i] = tIm; }
                return;
            }
            switch (f)
            {
                case GCCompiler.F_SIN: for (int i = 0; i < n; i++) A[i] = System.Math.Sin(A[i]); break;
                case GCCompiler.F_COS: for (int i = 0; i < n; i++) A[i] = System.Math.Cos(A[i]); break;
                case GCCompiler.F_TAN: for (int i = 0; i < n; i++) A[i] = System.Math.Tan(A[i]); break;
                case GCCompiler.F_SQRT: for (int i = 0; i < n; i++) A[i] = System.Math.Sqrt(A[i]); break;
                case GCCompiler.F_EXP: for (int i = 0; i < n; i++) A[i] = System.Math.Exp(A[i]); break;
                case GCCompiler.F_LN: for (int i = 0; i < n; i++) A[i] = System.Math.Log(A[i]); break;
                case GCCompiler.F_LOG10: for (int i = 0; i < n; i++) A[i] = System.Math.Log10(A[i]); break;
                case GCCompiler.F_ABS: for (int i = 0; i < n; i++) A[i] = System.Math.Abs(A[i]); break;
                case GCCompiler.F_FLOOR: for (int i = 0; i < n; i++) A[i] = System.Math.Floor(A[i]); break;
                case GCCompiler.F_CEIL: for (int i = 0; i < n; i++) A[i] = System.Math.Ceiling(A[i]); break;
                case GCCompiler.F_ATAN: for (int i = 0; i < n; i++) A[i] = System.Math.Atan(A[i]); break;
                case GCCompiler.F_ASIN: for (int i = 0; i < n; i++) A[i] = System.Math.Asin(A[i]); break;
                case GCCompiler.F_ACOS: for (int i = 0; i < n; i++) A[i] = System.Math.Acos(A[i]); break;
                case GCCompiler.F_SINH: for (int i = 0; i < n; i++) A[i] = System.Math.Sinh(A[i]); break;
                case GCCompiler.F_COSH: for (int i = 0; i < n; i++) A[i] = System.Math.Cosh(A[i]); break;
                case GCCompiler.F_TANH: for (int i = 0; i < n; i++) A[i] = System.Math.Tanh(A[i]); break;
                case GCCompiler.F_RE: break;
                default: for (int i = 0; i < n; i++) A[i] = F1R(f, A[i]); break;
            }
        }

        double F1R(int f, double v)
        {
            switch (f)
            {
                case GCCompiler.F_SIN: return System.Math.Sin(v);
                case GCCompiler.F_COS: return System.Math.Cos(v);
                case GCCompiler.F_TAN: return System.Math.Tan(v);
                case GCCompiler.F_CSC: return 1.0 / System.Math.Sin(v);
                case GCCompiler.F_SEC: return 1.0 / System.Math.Cos(v);
                case GCCompiler.F_COT: return System.Math.Cos(v) / System.Math.Sin(v);
                case GCCompiler.F_ASIN: return System.Math.Asin(v);
                case GCCompiler.F_ACOS: return System.Math.Acos(v);
                case GCCompiler.F_ATAN: return System.Math.Atan(v);
                case GCCompiler.F_ACSC: return System.Math.Asin(1.0 / v);
                case GCCompiler.F_ASEC: return System.Math.Acos(1.0 / v);
                case GCCompiler.F_ACOT:
                    if (v == 0) return System.Math.PI * 0.5;
                    if (v > 0) return System.Math.Atan(1.0 / v);
                    return System.Math.PI + System.Math.Atan(1.0 / v);
                case GCCompiler.F_SINH: return System.Math.Sinh(v);
                case GCCompiler.F_COSH: return System.Math.Cosh(v);
                case GCCompiler.F_TANH: return System.Math.Tanh(v);
                case GCCompiler.F_CSCH: return 1.0 / System.Math.Sinh(v);
                case GCCompiler.F_SECH: return 1.0 / System.Math.Cosh(v);
                case GCCompiler.F_COTH: return 1.0 / System.Math.Tanh(v);
                case GCCompiler.F_ASINH:
                    {
                        double av = System.Math.Abs(v);
                        double r = System.Math.Log(av + System.Math.Sqrt(av * av + 1.0));
                        return v < 0 ? -r : r;
                    }
                case GCCompiler.F_ACOSH:
                    if (v < 1) return double.NaN;
                    return System.Math.Log(v + System.Math.Sqrt(v * v - 1.0));
                case GCCompiler.F_ATANH:
                    if (v <= -1 || v >= 1) return (v == 1) ? double.PositiveInfinity : (v == -1 ? double.NegativeInfinity : double.NaN);
                    return 0.5 * System.Math.Log((1.0 + v) / (1.0 - v));
                case GCCompiler.F_SQRT: return System.Math.Sqrt(v);
                case GCCompiler.F_CBRT: return v < 0 ? -System.Math.Pow(-v, 1.0 / 3.0) : System.Math.Pow(v, 1.0 / 3.0);
                case GCCompiler.F_EXP: return System.Math.Exp(v);
                case GCCompiler.F_LN: return System.Math.Log(v);
                case GCCompiler.F_LOG10: return System.Math.Log10(v);
                case GCCompiler.F_ABS: return System.Math.Abs(v);
                case GCCompiler.F_SIGN: if (double.IsNaN(v)) return v; return v > 0 ? 1.0 : (v < 0 ? -1.0 : 0.0);
                case GCCompiler.F_FLOOR: return System.Math.Floor(v);
                case GCCompiler.F_CEIL: return System.Math.Ceiling(v);
                case GCCompiler.F_ROUND: return v < 0 ? -System.Math.Floor(-v + 0.5) : System.Math.Floor(v + 0.5);
                case GCCompiler.F_RE: return v;
                case GCCompiler.F_IM: return double.IsNaN(v) ? v : 0.0;
                case GCCompiler.F_CONJ: return v;
                case GCCompiler.F_ARG: if (double.IsNaN(v)) return v; return v < 0 ? System.Math.PI : 0.0;
                case GCCompiler.F_ERF: return Erf(v);
                case GCCompiler.F_FACT: return Factorial(v);
            }
            return double.NaN;
        }

        double Erf(double x)
        {
            if (double.IsNaN(x)) return x;
            double ax = System.Math.Abs(x);
            double r;
            if (ax <= 3.0)
            {
                double sum = 0, term = ax, x2 = ax * ax;
                for (int n = 0; n < 100; n++)
                {
                    double t = term / (2 * n + 1);
                    sum += t;
                    if (System.Math.Abs(t) < 1e-17 * System.Math.Abs(sum)) break;
                    term *= -x2 / (n + 1);
                }
                r = sum * 1.1283791670955126;
            }
            else
            {
                double x2 = ax * ax;
                double inv = 1.0 / (2 * x2);
                double series = 1 - inv + 3 * inv * inv - 15 * inv * inv * inv + 105 * inv * inv * inv * inv;
                r = 1 - System.Math.Exp(-x2) / (ax * 1.7724538509055159) * series;
            }
            return x < 0 ? -r : r;
        }

        double Gamma(double x)
        {
            if (double.IsNaN(x)) return x;
            if (x == System.Math.Floor(x) && x <= 0) return double.NaN;
            bool reflect = x < 0.5;
            double y = reflect ? 1.0 - x : x;
            y -= 1;
            double a = 0.99999999999980993;
            double t = y + 7.5;
            a += 676.5203681218851 / (y + 1);
            a += -1259.1392167224028 / (y + 2);
            a += 771.32342877765313 / (y + 3);
            a += -176.61502916214059 / (y + 4);
            a += 12.507343278686905 / (y + 5);
            a += -0.13857109526572012 / (y + 6);
            a += 9.9843695780195716e-6 / (y + 7);
            a += 1.5056327351493116e-7 / (y + 8);
            double g = 2.5066282746310002 * System.Math.Pow(t, y + 0.5) * System.Math.Exp(-t) * a;
            if (reflect) return System.Math.PI / (System.Math.Sin(System.Math.PI * x) * g);
            return g;
        }

        double Factorial(double v)
        {
            if (double.IsNaN(v)) return v;
            if (v == System.Math.Floor(v))
            {
                if (v < 0) return double.NaN;
                if (v > 170) return double.PositiveInfinity;
                double r = 1;
                for (int i = 2; i <= (int)v; i++) r *= i;
                return r;
            }
            return Gamma(v + 1);
        }

        void C1(int f, double a, double b)
        {
            if (b == 0)
            {
                // real-valued inputs: use real versions where they are defined
                if (f == GCCompiler.F_SQRT && a < 0) { tRe = 0; tIm = System.Math.Sqrt(-a); return; }
                if (f == GCCompiler.F_LN && a < 0) { tRe = System.Math.Log(-a); tIm = System.Math.PI; return; }
                if (f == GCCompiler.F_LOG10 && a < 0) { tRe = System.Math.Log10(-a); tIm = System.Math.PI / 2.302585092994046; return; }
                bool needsComplex = (f == GCCompiler.F_ASIN || f == GCCompiler.F_ACOS) && (a < -1 || a > 1);
                needsComplex = needsComplex || (f == GCCompiler.F_ACOSH && a < 1) || (f == GCCompiler.F_ATANH && (a < -1 || a > 1));
                needsComplex = needsComplex || ((f == GCCompiler.F_ACSC || f == GCCompiler.F_ASEC) && a > -1 && a < 1);
                if (!needsComplex) { tRe = F1R(f, a); tIm = 0; return; }
            }
            switch (f)
            {
                case GCCompiler.F_SIN: tRe = System.Math.Sin(a) * System.Math.Cosh(b); tIm = System.Math.Cos(a) * System.Math.Sinh(b); return;
                case GCCompiler.F_COS: tRe = System.Math.Cos(a) * System.Math.Cosh(b); tIm = -System.Math.Sin(a) * System.Math.Sinh(b); return;
                case GCCompiler.F_TAN:
                    {
                        double sr = System.Math.Sin(a) * System.Math.Cosh(b), si = System.Math.Cos(a) * System.Math.Sinh(b);
                        double cr = System.Math.Cos(a) * System.Math.Cosh(b), ci = -System.Math.Sin(a) * System.Math.Sinh(b);
                        CDiv(sr, si, cr, ci); return;
                    }
                case GCCompiler.F_CSC: C1(GCCompiler.F_SIN, a, b); CDiv(1, 0, tRe, tIm); return;
                case GCCompiler.F_SEC: C1(GCCompiler.F_COS, a, b); CDiv(1, 0, tRe, tIm); return;
                case GCCompiler.F_COT: C1(GCCompiler.F_TAN, a, b); CDiv(1, 0, tRe, tIm); return;
                case GCCompiler.F_SINH: tRe = System.Math.Sinh(a) * System.Math.Cos(b); tIm = System.Math.Cosh(a) * System.Math.Sin(b); return;
                case GCCompiler.F_COSH: tRe = System.Math.Cosh(a) * System.Math.Cos(b); tIm = System.Math.Sinh(a) * System.Math.Sin(b); return;
                case GCCompiler.F_TANH:
                    {
                        double sr = System.Math.Sinh(a) * System.Math.Cos(b), si = System.Math.Cosh(a) * System.Math.Sin(b);
                        double cr = System.Math.Cosh(a) * System.Math.Cos(b), ci = System.Math.Sinh(a) * System.Math.Sin(b);
                        CDiv(sr, si, cr, ci); return;
                    }
                case GCCompiler.F_CSCH: C1(GCCompiler.F_SINH, a, b); CDiv(1, 0, tRe, tIm); return;
                case GCCompiler.F_SECH: C1(GCCompiler.F_COSH, a, b); CDiv(1, 0, tRe, tIm); return;
                case GCCompiler.F_COTH: C1(GCCompiler.F_TANH, a, b); CDiv(1, 0, tRe, tIm); return;
                case GCCompiler.F_ASIN: CAsin(a, b); return;
                case GCCompiler.F_ACOS: CAsin(a, b); tRe = System.Math.PI * 0.5 - tRe; tIm = -tIm; return;
                case GCCompiler.F_ATAN: CAtan(a, b); return;
                case GCCompiler.F_ACSC: CDiv(1, 0, a, b); CAsin(tRe, tIm); return;
                case GCCompiler.F_ASEC: CDiv(1, 0, a, b); CAsin(tRe, tIm); tRe = System.Math.PI * 0.5 - tRe; tIm = -tIm; return;
                case GCCompiler.F_ACOT: CDiv(1, 0, a, b); CAtan(tRe, tIm); return;
                case GCCompiler.F_ASINH:
                    {
                        // ln(z + sqrt(z^2+1))
                        CMul(a, b, a, b); CSqrt(tRe + 1, tIm); CLn(a + tRe, b + tIm); return;
                    }
                case GCCompiler.F_ACOSH:
                    {
                        // ln(z + sqrt(z+1)*sqrt(z-1))
                        CSqrt(a + 1, b); double pr = tRe, pi = tIm;
                        CSqrt(a - 1, b); CMul(pr, pi, tRe, tIm); CLn(a + tRe, b + tIm); return;
                    }
                case GCCompiler.F_ATANH:
                    {
                        CLn(1 + a, b); double lr = tRe, li = tIm;
                        CLn(1 - a, -b); tRe = 0.5 * (lr - tRe); tIm = 0.5 * (li - tIm); return;
                    }
                case GCCompiler.F_SQRT: CSqrt(a, b); return;
                case GCCompiler.F_CBRT: CLn(a, b); CExp(tRe / 3.0, tIm / 3.0); return;
                case GCCompiler.F_EXP: CExp(a, b); return;
                case GCCompiler.F_LN: CLn(a, b); return;
                case GCCompiler.F_LOG10: CLn(a, b); tRe /= 2.302585092994046; tIm /= 2.302585092994046; return;
                case GCCompiler.F_ABS: tRe = CAbs(a, b); tIm = 0; return;
                case GCCompiler.F_SIGN: { double m = CAbs(a, b); if (m == 0) { tRe = 0; tIm = 0; } else { tRe = a / m; tIm = b / m; } return; }
                case GCCompiler.F_FLOOR: tRe = System.Math.Floor(a); tIm = System.Math.Floor(b); return;
                case GCCompiler.F_CEIL: tRe = System.Math.Ceiling(a); tIm = System.Math.Ceiling(b); return;
                case GCCompiler.F_ROUND: tRe = F1R(GCCompiler.F_ROUND, a); tIm = F1R(GCCompiler.F_ROUND, b); return;
                case GCCompiler.F_RE: tRe = a; tIm = 0; return;
                case GCCompiler.F_IM: tRe = b; tIm = 0; return;
                case GCCompiler.F_CONJ: tRe = a; tIm = -b; return;
                case GCCompiler.F_ARG: tRe = System.Math.Atan2(b, a); tIm = 0; return;
            }
            tRe = double.NaN; tIm = double.NaN;
        }

        void CAsin(double a, double b)
        {
            // asin z = -i ln(iz + sqrt(1 - z^2))
            CMul(a, b, a, b);
            CSqrt(1 - tRe, -tIm);
            double sr = tRe, si = tIm;
            CLn(sr - b, si + a);
            double lr = tRe, li = tIm;
            tRe = li; tIm = -lr;
        }

        void CAtan(double a, double b)
        {
            // atan z = (i/2) (ln(1 - iz) - ln(1 + iz))
            CLn(1 + b, -a); double l1r = tRe, l1i = tIm;
            CLn(1 - b, a); double dr = l1r - tRe, di = l1i - tIm;
            tRe = -0.5 * di; tIm = 0.5 * dr;
        }

        // ------------------------------------------------------------------ binary functions
        void Fn2(int f, int a, int n)
        {
            int b = a + 1;
            if (sSc[a] && sSc[b])
            {
                if (cplx) { C2(f, sScRe[a], sScIm[a], sScRe[b], sScIm[b]); sScRe[a] = tRe; sScIm[a] = tIm; }
                else sScRe[a] = F2R(f, sScRe[a], sScRe[b]);
                return;
            }
            Materialize(a, n);
            Materialize(b, n);
            double[] A = sRe[a], B = sRe[b];
            if (cplx)
            {
                double[] AI = sIm[a], BI = sIm[b];
                for (int i = 0; i < n; i++) { C2(f, A[i], AI[i], B[i], BI[i]); A[i] = tRe; AI[i] = tIm; }
                return;
            }
            if (f == GCCompiler.F_ATAN2) { for (int i = 0; i < n; i++) A[i] = System.Math.Atan2(A[i], B[i]); return; }
            for (int i = 0; i < n; i++) A[i] = F2R(f, A[i], B[i]);
        }

        double F2R(int f, double a, double b)
        {
            switch (f)
            {
                case GCCompiler.F_MOD: return a - b * System.Math.Floor(a / b);
                case GCCompiler.F_ATAN2: return System.Math.Atan2(a, b);
                case GCCompiler.F_LOGB: return System.Math.Log(a) / System.Math.Log(b);
                case GCCompiler.F_NCR: return NCr(a, b);
                case GCCompiler.F_NPR: return NPr(a, b);
                case GCCompiler.F_NTHROOT:
                    if (a < 0 && b == System.Math.Floor(b) && System.Math.Abs(b) < 1e9 && ((int)b) % 2 != 0) return -System.Math.Pow(-a, 1.0 / b);
                    return System.Math.Pow(a, 1.0 / b);
                case GCCompiler.F_ROUND2:
                    {
                        double d = System.Math.Floor(b);
                        if (d > 15) return a;
                        double m = System.Math.Pow(10, d);
                        return F1R(GCCompiler.F_ROUND, a * m) / m;
                    }
            }
            return double.NaN;
        }

        double NCr(double n, double r)
        {
            if (double.IsNaN(n) || double.IsNaN(r)) return double.NaN;
            if (n == System.Math.Floor(n) && r == System.Math.Floor(r))
            {
                if (r < 0 || (n >= 0 && r > n)) return 0;
                if (n >= 0)
                {
                    if (r > n - r) r = n - r;
                    double res = 1;
                    for (int i = 1; i <= (int)r; i++) res = res * (n - r + i) / i;
                    return System.Math.Round(res);
                }
            }
            return Gamma(n + 1) / (Gamma(r + 1) * Gamma(n - r + 1));
        }

        double NPr(double n, double r)
        {
            if (double.IsNaN(n) || double.IsNaN(r)) return double.NaN;
            if (n == System.Math.Floor(n) && r == System.Math.Floor(r) && n >= 0)
            {
                if (r < 0 || r > n) return 0;
                double res = 1;
                for (int i = 0; i < (int)r; i++) res *= (n - i);
                return res;
            }
            return Gamma(n + 1) / Gamma(n - r + 1);
        }

        void C2(int f, double a, double ai, double b, double bi)
        {
            if (f == GCCompiler.F_LOGB)
            {
                CLn(a, ai); double lr = tRe, li = tIm;
                CLn(b, bi); CDiv(lr, li, tRe, tIm);
                return;
            }
            if (f == GCCompiler.F_NTHROOT && (ai != 0 || bi != 0 || a < 0))
            {
                CDiv(1, 0, b, bi);
                double er = tRe, ei = tIm;
                if (ai == 0 && bi == 0 && b == System.Math.Floor(b) && System.Math.Abs(b) < 1e9 && ((int)b) % 2 != 0) { tRe = -System.Math.Pow(-a, 1.0 / b); tIm = 0; return; }
                CPow(a, ai, er, ei);
                return;
            }
            if (System.Math.Abs(ai) > 1e-12 || System.Math.Abs(bi) > 1e-12) { tRe = double.NaN; tIm = 0; return; }
            tRe = F2R(f, a, b); tIm = 0;
        }

        // ------------------------------------------------------------------ comparisons
        void Cmp(int rel, int a, int n)
        {
            int b = a + 1;
            if (cplx)
            {
                // compare real parts; non-real values are undefined (false)
                Materialize(a, n); Materialize(b, n);
                double[] AR = sRe[a], AI = sIm[a], BR = sRe[b], BI = sIm[b];
                for (int i = 0; i < n; i++)
                {
                    bool real = System.Math.Abs(AI[i]) < 1e-10 && System.Math.Abs(BI[i]) < 1e-10;
                    AR[i] = real ? CmpR(rel, AR[i], BR[i]) : 0;
                    AI[i] = 0;
                }
                return;
            }
            if (sSc[a] && sSc[b]) { sScRe[a] = CmpR(rel, sScRe[a], sScRe[b]); return; }
            Materialize(a, n); Materialize(b, n);
            double[] A = sRe[a], B = sRe[b];
            if (rel == GCCompiler.R_LT) { for (int i = 0; i < n; i++) A[i] = A[i] < B[i] ? 1.0 : 0.0; }
            else if (rel == GCCompiler.R_LE) { for (int i = 0; i < n; i++) A[i] = A[i] <= B[i] ? 1.0 : 0.0; }
            else if (rel == GCCompiler.R_GT) { for (int i = 0; i < n; i++) A[i] = A[i] > B[i] ? 1.0 : 0.0; }
            else if (rel == GCCompiler.R_GE) { for (int i = 0; i < n; i++) A[i] = A[i] >= B[i] ? 1.0 : 0.0; }
            else { for (int i = 0; i < n; i++) A[i] = CmpR(rel, A[i], B[i]); }
        }

        double CmpR(int rel, double x, double y)
        {
            if (rel == GCCompiler.R_LT) return x < y ? 1.0 : 0.0;
            if (rel == GCCompiler.R_LE) return x <= y ? 1.0 : 0.0;
            if (rel == GCCompiler.R_GT) return x > y ? 1.0 : 0.0;
            if (rel == GCCompiler.R_GE) return x >= y ? 1.0 : 0.0;
            double m = System.Math.Max(1.0, System.Math.Max(System.Math.Abs(x), System.Math.Abs(y)));
            return System.Math.Abs(x - y) <= 1e-12 * m ? 1.0 : 0.0;
        }

        void AndOp(int a, int n)
        {
            int b = a + 1;
            if (sSc[a] && sSc[b]) { sScRe[a] = (sScRe[a] == 1.0 && sScRe[b] == 1.0) ? 1.0 : 0.0; sScIm[a] = 0; return; }
            Materialize(a, n); Materialize(b, n);
            double[] A = sRe[a], B = sRe[b];
            for (int i = 0; i < n; i++) A[i] = (A[i] == 1.0 && B[i] == 1.0) ? 1.0 : 0.0;
            if (cplx) { double[] I = sIm[a]; for (int i = 0; i < n; i++) I[i] = 0; }
        }

        void SelectOp(int c, int n)
        {
            int a = c + 1, b = c + 2;
            if (sSc[c])
            {
                // uniform condition: pick a branch
                if (sScRe[c] == 1.0) SwapSlots(c, a); else SwapSlots(c, b);
                return;
            }
            Materialize(a, n); Materialize(b, n);
            double[] C = sRe[c], A = sRe[a], B = sRe[b];
            if (cplx)
            {
                double[] CI = sIm[c], AI = sIm[a], BI = sIm[b];
                for (int i = 0; i < n; i++) { bool t = C[i] == 1.0; CI[i] = t ? AI[i] : BI[i]; C[i] = t ? A[i] : B[i]; }
                return;
            }
            for (int i = 0; i < n; i++) C[i] = C[i] == 1.0 ? A[i] : B[i];
        }

        // ------------------------------------------------------------------ lists
        [RecursiveMethod]
        int OpListLit(int pc, int s, int n, int k, bool kvec)
        {
            int m = code[pc];
            int p = pc + 1;
            if (!kvec)
            {
                bool found = false;
                for (int j = 0; j < m; j++)
                {
                    int len = code[p];
                    if (j == k) { Exec(p + 1, p + 1 + len, n, s, 0, false); found = true; }
                    p += 1 + len;
                }
                if (!found) SetNaN(s);
                return p;
            }
            for (int j = 0; j < m; j++)
            {
                int len = code[p];
                int i = j - k;
                if (i >= 0 && i < n)
                {
                    Exec(p + 1, p + 1 + len, 1, s + 1, 0, false);
                    sRe[s][i] = GetRe(s + 1, 0);
                    sIm[s][i] = GetIm(s + 1, 0);
                }
                p += 1 + len;
            }
            for (int i = 0; i < n; i++) if (k + i >= m) { sRe[s][i] = double.NaN; sIm[s][i] = 0; }
            sSc[s] = false;
            return p;
        }

        [RecursiveMethod]
        int RangeInfo(int pc, int sp)
        {
            int m = code[pc];
            int p = pc + 1;
            double v0 = double.NaN, v1 = double.NaN, v2 = double.NaN;
            for (int j = 0; j < m; j++)
            {
                int len = code[p];
                Exec(p + 1, p + 1 + len, 1, sp, 0, false);
                double v = GetRe(sp, 0);
                if (j == 0) v0 = v; else if (j == 1) v1 = v; else v2 = v;
                p += 1 + len;
            }
            double first = v0;
            double last = m == 3 ? v2 : v1;
            double step = m == 3 ? v1 - v0 : (last >= first ? 1.0 : -1.0);
            rFirst = first;
            rStep = step;
            rCount = 0;
            if (!double.IsNaN(first) && !double.IsNaN(last) && !double.IsNaN(step) && step != 0 &&
                !double.IsInfinity(first) && !double.IsInfinity(last))
            {
                double cnt = System.Math.Floor((last - first) / step + 1e-9) + 1;
                if (cnt < 0) cnt = 0;
                if (cnt > 10000) cnt = 10000;
                rCount = (int)cnt;
            }
            return p;
        }

        [RecursiveMethod]
        int OpRange(int pc, int s, int n, int k, bool kvec)
        {
            int end = RangeInfo(pc, s);
            double first = rFirst, step = rStep;
            int count = rCount;
            if (!kvec)
            {
                sSc[s] = true; sScIm[s] = 0;
                sScRe[s] = (k >= 0 && k < count) ? first + k * step : double.NaN;
                return end;
            }
            sSc[s] = false;
            double[] R = sRe[s], I = sIm[s];
            for (int i = 0; i < n; i++)
            {
                int j = k + i;
                R[i] = j < count ? first + j * step : double.NaN;
                I[i] = 0;
            }
            return end;
        }

        [RecursiveMethod]
        int BroadcastLen(int hdr, int sp)
        {
            if (code[hdr] != OP_HDR) return -1;
            int cnt = code[hdr + 1];
            if (cnt == 0) return -1;
            int best = int.MaxValue;
            for (int i = 0; i < cnt; i++)
            {
                int pos = code[hdr + 2 + i];
                int op = code[pos];
                int L = 0;
                if (op == OP_LISTVAR) L = lLen[code[pos + 1]];
                else if (op == OP_LISTLIT) L = code[pos + 1];
                else if (op == OP_RANGE) { RangeInfo(pos + 1, sp); L = rCount; }
                if (L < best) best = L;
            }
            return best;
        }

        // ------------------------------------------------------------------ calculus
        const double DERIV_H = 1e-3;

        // five-point stencil: f'(x) ~ (-f(x+2h) + 8f(x+h) - 8f(x-h) + f(x-2h)) / 12h
        [RecursiveMethod]
        void OpDeriv(int slot, int s0, int s1, int a, int n, int k, bool kvec)
        {
            bool sc = sSc[a];
            double x0 = sc ? sScRe[a] : 0;
            for (int j = 0; j < 4; j++)
            {
                double off = j == 0 ? 2.0 : (j == 1 ? 1.0 : (j == 2 ? -1.0 : -2.0));
                if (sc) SetVarScalar(slot, x0 + off * DERIV_H * System.Math.Max(1.0, System.Math.Abs(x0)));
                else
                {
                    double[] P = sRe[a];
                    double[] V = vRe[slot];
                    vSc[slot] = false;
                    for (int i = 0; i < n; i++) { double x = P[i]; V[i] = x + off * DERIV_H * System.Math.Max(1.0, System.Math.Abs(x)); }
                }
                Exec(s0, s1, n, a + 1 + j, k, kvec);
            }
            if (sc && sSc[a + 1] && sSc[a + 2] && sSc[a + 3] && sSc[a + 4])
            {
                double inv = 1.0 / (12.0 * DERIV_H * System.Math.Max(1.0, System.Math.Abs(x0)));
                sScRe[a] = (-sScRe[a + 1] + 8 * sScRe[a + 2] - 8 * sScRe[a + 3] + sScRe[a + 4]) * inv;
                sScIm[a] = (-sScIm[a + 1] + 8 * sScIm[a + 2] - 8 * sScIm[a + 3] + sScIm[a + 4]) * inv;
                return;
            }
            Materialize(a + 1, n); Materialize(a + 2, n); Materialize(a + 3, n); Materialize(a + 4, n);
            sSc[a] = false;
            double[] O = sRe[a], R1 = sRe[a + 1], R2 = sRe[a + 2], R3 = sRe[a + 3], R4 = sRe[a + 4];
            double[] OI = sIm[a], I1 = sIm[a + 1], I2 = sIm[a + 2], I3 = sIm[a + 3], I4 = sIm[a + 4];
            for (int i = 0; i < n; i++)
            {
                double x = sc ? x0 : O[i];
                double inv = 1.0 / (12.0 * DERIV_H * System.Math.Max(1.0, System.Math.Abs(x)));
                O[i] = (-R1[i] + 8 * R2[i] - 8 * R3[i] + R4[i]) * inv;
                if (cplx) OI[i] = (-I1[i] + 8 * I2[i] - 8 * I3[i] + I4[i]) * inv;
            }
        }

        void Accumulate(int acc, int r, int n, bool prod)
        {
            if (sSc[acc] && sSc[r])
            {
                if (prod)
                {
                    if (cplx) { CMul(sScRe[acc], sScIm[acc], sScRe[r], sScIm[r]); sScRe[acc] = tRe; sScIm[acc] = tIm; }
                    else sScRe[acc] *= sScRe[r];
                }
                else { sScRe[acc] += sScRe[r]; sScIm[acc] += sScIm[r]; }
                return;
            }
            Materialize(acc, n); Materialize(r, n);
            double[] A = sRe[acc], R = sRe[r];
            if (!cplx)
            {
                if (prod) { for (int i = 0; i < n; i++) A[i] *= R[i]; }
                else { for (int i = 0; i < n; i++) A[i] += R[i]; }
                return;
            }
            double[] AI = sIm[acc], RI = sIm[r];
            if (prod) { for (int i = 0; i < n; i++) { CMul(A[i], AI[i], R[i], RI[i]); A[i] = tRe; AI[i] = tIm; } }
            else { for (int i = 0; i < n; i++) { A[i] += R[i]; AI[i] += RI[i]; } }
        }

        [RecursiveMethod]
        void OpSum(bool prod, int slot, int s0, int s1, int a, int n, int k, bool kvec)
        {
            int acc = a + 2;
            int r = a + 3;
            sSc[acc] = true; sScRe[acc] = prod ? 1.0 : 0.0; sScIm[acc] = 0;
            if (sSc[a] && sSc[a + 1])
            {
                double lo = sScRe[a], hi = sScRe[a + 1];
                if (double.IsNaN(lo) || double.IsNaN(hi) || double.IsInfinity(lo) || double.IsInfinity(hi)) { SetNaN(a); return; }
                double L = System.Math.Ceiling(lo - 1e-9), H = System.Math.Floor(hi + 1e-9);
                if (H - L > MAX_SUM_TERMS) { SetNaN(a); return; }
                for (double nn = L; nn <= H; nn += 1.0)
                {
                    SetVarScalar(slot, nn);
                    Exec(s0, s1, n, r, k, kvec);
                    Accumulate(acc, r, n, prod);
                }
                SwapSlots(a, acc);
                return;
            }
            Materialize(a, n); Materialize(a + 1, n);
            double[] LO = sRe[a], HI = sRe[a + 1];
            double gmin = double.PositiveInfinity, gmax = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                double l = LO[i], h = HI[i];
                if (double.IsNaN(l) || double.IsNaN(h)) continue;
                if (l < gmin) gmin = l;
                if (h > gmax) gmax = h;
            }
            Materialize(acc, n);
            if (gmin > gmax || double.IsInfinity(gmin) || double.IsInfinity(gmax)) { SetNaN(a); return; }
            double L2 = System.Math.Ceiling(gmin - 1e-9), H2 = System.Math.Floor(gmax + 1e-9);
            if (H2 - L2 > MAX_SUM_TERMS) { SetNaN(a); return; }
            for (double nn = L2; nn <= H2; nn += 1.0)
            {
                SetVarScalar(slot, nn);
                Exec(s0, s1, n, r, k, kvec);
                Materialize(r, n);
                double[] A = sRe[acc], R = sRe[r];
                double[] AI = sIm[acc], RI = sIm[r];
                for (int i = 0; i < n; i++)
                {
                    if (nn < LO[i] - 1e-9 || nn > HI[i] + 1e-9) continue;
                    if (prod)
                    {
                        if (cplx) { CMul(A[i], AI[i], R[i], RI[i]); A[i] = tRe; AI[i] = tIm; }
                        else A[i] *= R[i];
                    }
                    else { A[i] += R[i]; if (cplx) AI[i] += RI[i]; }
                }
            }
            for (int i = 0; i < n; i++) if (double.IsNaN(LO[i]) || double.IsNaN(HI[i])) { sRe[acc][i] = double.NaN; }
            SwapSlots(a, acc);
        }

        int IntPanels(double lo, double hi) { return (double.IsInfinity(lo) || double.IsInfinity(hi)) ? 8 : 3; }

        // integration node: sets iT, iW for panel p, node j of composite 10-point Gauss-Legendre
        // (3 panels for finite ranges, 8 for infinite ranges via variable transforms). iW = 0 for unused panels.
        void IntNode(double lo, double hi, int p, int j)
        {
            bool loInf = double.IsInfinity(lo), hiInf = double.IsInfinity(hi);
            int panels = (loInf || hiInf) ? 8 : 3;
            if (p >= panels) { iT = 0.5 * (lo + hi); iW = 0; return; }
            double a, b;
            if (!loInf && !hiInf) { a = lo; b = hi; }
            else if (!loInf && hiInf) { a = 0; b = 1; }
            else if (loInf && !hiInf) { a = 0; b = 1; }
            else { a = -1; b = 1; }
            double pw = (b - a) / panels;
            double pa = a + p * pw;
            double half = pw * 0.5;
            double u = pa + half + half * glX[j];
            double w = half * glW[j];
            if (!loInf && !hiInf) { iT = u; iW = w; return; }
            if (!loInf && hiInf)
            {
                double om = 1 - u;
                iT = lo + u / om; iW = w / (om * om);
                if (hi < 0) { iT = lo - u / om; iW = -iW; }
                return;
            }
            if (loInf && !hiInf)
            {
                double om = 1 - u;
                iT = hi - u / om; iW = w / (om * om);
                if (lo > 0) { iT = hi + u / om; iW = -iW; }
                return;
            }
            double om2 = 1 - u * u;
            iT = u / om2; iW = w * (1 + u * u) / (om2 * om2);
            if (lo > 0) iW = -iW;
        }

        [RecursiveMethod]
        void OpInt(int slot, int s0, int s1, int a, int n, int k, bool kvec)
        {
            int acc = a + 2;
            int r = a + 3;
            sSc[acc] = true; sScRe[acc] = 0; sScIm[acc] = 0;
            if (sSc[a] && sSc[a + 1])
            {
                double lo = sScRe[a], hi = sScRe[a + 1];
                if (double.IsNaN(lo) || double.IsNaN(hi)) { SetNaN(a); return; }
                if (lo == hi) { sScRe[a] = 0; sScIm[a] = 0; return; }
                int panels = IntPanels(lo, hi);
                for (int p = 0; p < panels; p++)
                {
                    for (int j = 0; j < 10; j++)
                    {
                        IntNode(lo, hi, p, j);
                        double w = iW;
                        SetVarScalar(slot, iT);
                        Exec(s0, s1, n, r, k, kvec);
                        if (sSc[r] && sSc[acc]) { sScRe[acc] += w * sScRe[r]; sScIm[acc] += w * sScIm[r]; }
                        else
                        {
                            Materialize(acc, n); Materialize(r, n);
                            double[] A = sRe[acc], R = sRe[r];
                            for (int i = 0; i < n; i++) A[i] += w * R[i];
                            if (cplx) { double[] AI = sIm[acc], RI = sIm[r]; for (int i = 0; i < n; i++) AI[i] += w * RI[i]; }
                        }
                    }
                }
                SwapSlots(a, acc);
                return;
            }
            Materialize(a, n); Materialize(a + 1, n);
            Materialize(acc, n);
            double[] LO = sRe[a], HI = sRe[a + 1];
            int maxPanels = 3;
            for (int i = 0; i < n; i++) { int pn = IntPanels(LO[i], HI[i]); if (pn > maxPanels) maxPanels = pn; }
            for (int p = 0; p < maxPanels; p++)
            {
                for (int j = 0; j < 10; j++)
                {
                    double[] V = vRe[slot];
                    vSc[slot] = false;
                    for (int i = 0; i < n; i++) { IntNode(LO[i], HI[i], p, j); V[i] = iT; }
                    Exec(s0, s1, n, r, k, kvec);
                    Materialize(r, n);
                    double[] A = sRe[acc], R = sRe[r], AI = sIm[acc], RI = sIm[r];
                    for (int i = 0; i < n; i++)
                    {
                        IntNode(LO[i], HI[i], p, j);
                        if (iW == 0) continue;
                        A[i] += iW * R[i];
                        if (cplx) AI[i] += iW * RI[i];
                    }
                }
            }
            for (int i = 0; i < n; i++) if (double.IsNaN(LO[i]) || double.IsNaN(HI[i])) sRe[acc][i] = double.NaN; else if (LO[i] == HI[i]) sRe[acc][i] = 0;
            SwapSlots(a, acc);
        }

        // ------------------------------------------------------------------ reducers
        double Gcd(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            a = System.Math.Abs(System.Math.Round(a)); b = System.Math.Abs(System.Math.Round(b));
            int guard = 0;
            while (b > 0.5 && guard < 200) { double t = a - b * System.Math.Floor(a / b); a = b; b = t; guard++; }
            return a;
        }

        [RecursiveMethod]
        void OpReduce(int f, int s0, int s1, int s, int n)
        {
            int B = BroadcastLen(s0, s);
            bool single = B < 0;
            if (single) B = 1;
            if (f == GCCompiler.F_LENGTH) { sSc[s] = true; sScRe[s] = single ? 1 : B; sScIm[s] = 0; return; }
            if (B == 0)
            {
                sSc[s] = true; sScIm[s] = 0;
                sScRe[s] = f == GCCompiler.F_TOTAL ? 0.0 : double.NaN;
                return;
            }
            int acc = s, acc2 = s + 1, r = s + 2;
            if (f == GCCompiler.F_TOTAL || f == GCCompiler.F_MEAN)
            {
                sSc[acc] = true; sScRe[acc] = 0; sScIm[acc] = 0;
                for (int kk = 0; kk < B; kk++) { Exec(s0, s1, n, r, kk, false); Accumulate(acc, r, n, false); }
                if (f == GCCompiler.F_MEAN)
                {
                    double inv = 1.0 / B;
                    if (sSc[acc]) { sScRe[acc] *= inv; sScIm[acc] *= inv; }
                    else { double[] A = sRe[acc]; for (int i = 0; i < n; i++) A[i] *= inv; if (cplx) { double[] AI = sIm[acc]; for (int i = 0; i < n; i++) AI[i] *= inv; } }
                }
                return;
            }
            if (f == GCCompiler.F_MIN || f == GCCompiler.F_MAX || f == GCCompiler.F_GCD || f == GCCompiler.F_LCM)
            {
                bool isMin = f == GCCompiler.F_MIN;
                bool isMax = f == GCCompiler.F_MAX;
                for (int kk = 0; kk < B; kk++)
                {
                    Exec(s0, s1, n, kk == 0 ? acc : r, kk, false);
                    if (kk == 0) { if (cplx) { Materialize(acc, n); } continue; }
                    if (sSc[acc] && sSc[r])
                    {
                        double x = sScRe[acc], y = sScRe[r];
                        sScRe[acc] = CombineMM(f, x, y);
                        sScIm[acc] = 0;
                        continue;
                    }
                    Materialize(acc, n); Materialize(r, n);
                    double[] A = sRe[acc], R = sRe[r];
                    if (isMin) { for (int i = 0; i < n; i++) { double y = R[i]; if (double.IsNaN(y) || y < A[i]) A[i] = y; } }
                    else if (isMax) { for (int i = 0; i < n; i++) { double y = R[i]; if (double.IsNaN(y) || y > A[i]) A[i] = y; } }
                    else { for (int i = 0; i < n; i++) A[i] = CombineMM(f, A[i], R[i]); }
                }
                if (cplx) { if (sSc[acc]) sScIm[acc] = 0; else { double[] AI = sIm[acc]; for (int i = 0; i < n; i++) AI[i] = 0; } }
                return;
            }
            if (f == GCCompiler.F_VAR || f == GCCompiler.F_STDEV)
            {
                // two-pass for numerical stability
                sSc[acc] = true; sScRe[acc] = 0; sScIm[acc] = 0;
                for (int kk = 0; kk < B; kk++) { Exec(s0, s1, n, r, kk, false); Accumulate(acc, r, n, false); }
                Materialize(acc, n);
                double[] M = sRe[acc];
                for (int i = 0; i < n; i++) M[i] /= B;
                sSc[acc2] = false;
                double[] S2 = sRe[acc2];
                for (int i = 0; i < n; i++) S2[i] = 0;
                for (int kk = 0; kk < B; kk++)
                {
                    Exec(s0, s1, n, r, kk, false);
                    Materialize(r, n);
                    double[] R = sRe[r];
                    for (int i = 0; i < n; i++) { double d = R[i] - M[i]; S2[i] += d * d; }
                }
                for (int i = 0; i < n; i++)
                {
                    double v = B > 1 ? S2[i] / (B - 1) : double.NaN;
                    M[i] = f == GCCompiler.F_STDEV ? System.Math.Sqrt(v) : v;
                }
                if (cplx) { double[] MI = sIm[acc]; for (int i = 0; i < n; i++) MI[i] = 0; }
                return;
            }
            if (f == GCCompiler.F_MEDIAN)
            {
                if ((long)B * n > MED_MAX) { SetNaN(s); return; }
                for (int kk = 0; kk < B; kk++)
                {
                    Exec(s0, s1, n, r, kk, false);
                    for (int i = 0; i < n; i++) medBuf[kk * n + i] = GetRe(r, i);
                }
                sSc[acc] = false;
                double[] A = sRe[acc];
                for (int i = 0; i < n; i++)
                {
                    // insertion sort column i in place (stride n)
                    bool bad = false;
                    for (int x = 1; x < B; x++)
                    {
                        double key = medBuf[x * n + i];
                        if (double.IsNaN(key)) bad = true;
                        int y = x - 1;
                        while (y >= 0 && medBuf[y * n + i] > key) { medBuf[(y + 1) * n + i] = medBuf[y * n + i]; y--; }
                        medBuf[(y + 1) * n + i] = key;
                    }
                    if (double.IsNaN(medBuf[i])) bad = true;
                    if (bad) { A[i] = double.NaN; continue; }
                    A[i] = (B % 2 == 1) ? medBuf[(B / 2) * n + i] : 0.5 * (medBuf[(B / 2 - 1) * n + i] + medBuf[(B / 2) * n + i]);
                }
                if (cplx) { double[] AI = sIm[acc]; for (int i = 0; i < n; i++) AI[i] = 0; }
                return;
            }
            SetNaN(s);
        }

        double CombineMM(int f, double x, double y)
        {
            if (f == GCCompiler.F_MIN) return (double.IsNaN(y) || y < x) ? y : x;
            if (f == GCCompiler.F_MAX) return (double.IsNaN(y) || y > x) ? y : x;
            if (f == GCCompiler.F_GCD) return Gcd(x, y);
            double g = Gcd(x, y);
            if (double.IsNaN(g)) return g;
            if (g == 0) return 0;
            return System.Math.Abs(System.Math.Round(x) * System.Math.Round(y)) / g;
        }

        [RecursiveMethod]
        void OpIndex(int s0, int s1, int a, int n)
        {
            int B = BroadcastLen(s0, a + 1);
            if (B < 0) B = 1;
            if (sSc[a])
            {
                double iv = sScRe[a];
                if (double.IsNaN(iv)) { SetNaN(a); return; }
                int kk = (int)System.Math.Round(iv) - 1;
                if (kk < 0 || kk >= B) { SetNaN(a); return; }
                Exec(s0, s1, n, a + 1, kk, false);
                SwapSlots(a, a + 1);
                return;
            }
            double[] IDX = sRe[a];
            int res = a + 1, r = a + 2;
            sSc[res] = false;
            double[] RR = sRe[res], RI = sIm[res];
            for (int i = 0; i < n; i++) { RR[i] = double.NaN; RI[i] = 0; }
            for (int kk = 0; kk < B; kk++)
            {
                bool any = false;
                for (int i = 0; i < n; i++) if (System.Math.Round(IDX[i]) - 1 == kk) { any = true; break; }
                if (!any) continue;
                Exec(s0, s1, n, r, kk, false);
                RR = sRe[res]; RI = sIm[res];
                for (int i = 0; i < n; i++)
                {
                    if (System.Math.Round(IDX[i]) - 1 != kk) continue;
                    RR[i] = GetRe(r, i);
                    RI[i] = GetIm(r, i);
                }
            }
            SwapSlots(a, res);
        }

        // =================================================================== program helpers
        void RunProg(int pid, int n, int k, bool kvec)
        {
            Exec(progStart[pid], progEnd[pid], n, 0, k, kvec);
        }

        int ProgBroadcast(int pid)
        {
            if (pid < 0) return -1;
            return BroadcastLen(progStart[pid], 0);
        }

        double EvalScalar(int pid)
        {
            RunProg(pid, 1, 0, false);
            tIm = GetIm(0, 0);
            return GetRe(0, 0);
        }

        // =================================================================== definitions
        /// <summary>Evaluate all definitions (params, lists, regressions) and display strings.</summary>
        public void EvaluateDefinitions()
        {
            Init();
            lUsed = 0;
            int ec = compiler.evalCount;
            int[] order = compiler.evalOrder;
            int[] dk = compiler.defKind;
            int[] dprog = compiler.defProg;
            int[] dprogB = compiler.defProgB;
            int[] dparam = compiler.defParam;
            int[] dlist = compiler.defList;
            for (int i = 0; i < ec; i++)
            {
                int d = order[i];
                int kind = dk[d];
                if (kind == GCCompiler.DK_SCALAR)
                {
                    int p = dparam[d];
                    if (p < 0) continue;
                    pRe[p] = EvalScalar(dprog[d]);
                    pIm[p] = tIm;
                }
                else if (kind == GCCompiler.DK_POINT)
                {
                    int p = dparam[d];
                    if (p < 0) continue;
                    pRe[p] = EvalScalar(dprog[d]); pIm[p] = 0;
                    pRe[p + 1] = EvalScalar(dprogB[d]); pIm[p + 1] = 0;
                }
                else if (kind == GCCompiler.DK_LIST)
                {
                    if (dlist[d] >= 0) EvalListInto(dprog[d], dlist[d], -1);
                }
                else if (kind == GCCompiler.DK_POINTLIST)
                {
                    if (dlist[d] < 0) continue;
                    int ba = ProgBroadcast(dprog[d]);
                    int bb = ProgBroadcast(dprogB[d]);
                    int B = ba < 0 ? bb : (bb < 0 ? ba : (ba < bb ? ba : bb));
                    EvalListInto(dprog[d], dlist[d], B);
                    EvalListInto(dprogB[d], dlist[d] + 1, B);
                }
                else if (kind == GCCompiler.DK_REG)
                {
                    RunRegression(d);
                }
            }
            BuildDisplays();
        }

        void EvalListInto(int pid, int lid, int forceLen)
        {
            int B = forceLen >= 0 ? forceLen : ProgBroadcast(pid);
            if (B < 0) B = 1;
            if (lUsed + B > LIST_POOL) B = LIST_POOL - lUsed;
            if (B < 0) B = 0;
            lStart[lid] = lUsed;
            lLen[lid] = B;
            for (int k0 = 0; k0 < B; k0 += CHUNK)
            {
                int n = B - k0; if (n > CHUNK) n = CHUNK;
                RunProg(pid, n, k0, true);
                for (int i = 0; i < n; i++)
                {
                    lRe[lUsed + k0 + i] = GetRe(0, i);
                    lIm[lUsed + k0 + i] = GetIm(0, i);
                }
            }
            lUsed += B;
        }

        // ------------------------------------------------------------------ regression (Levenberg–Marquardt)
        double regSSE;
        int regRows;

        double RegResiduals(int pid, double[] outR)
        {
            double sse = 0;
            for (int k0 = 0; k0 < regRows; k0 += CHUNK)
            {
                int n = regRows - k0; if (n > CHUNK) n = CHUNK;
                RunProg(pid, n, k0, true);
                for (int i = 0; i < n; i++)
                {
                    double v = GetRe(0, i);
                    outR[k0 + i] = v;
                    if (!double.IsNaN(v)) sse += v * v;
                }
            }
            return sse;
        }

        void RunRegression(int d)
        {
            int pid = compiler.defProg[d];
            int p0 = compiler.defRegParamStart[d];
            int P = compiler.defRegParamCount[d];
            int s = compiler.defSlot[d];
            if (P > MAX_REG_PARAMS) { if (s >= 0) slotDisplay[s] = "Too many parameters"; return; }
            int B = ProgBroadcast(pid);
            if (B <= 0) { if (s >= 0) slotDisplay[s] = "No data"; return; }
            if (B > MAX_REG_ROWS) B = MAX_REG_ROWS;
            regRows = B;
            for (int j = 0; j < P; j++)
            {
                double prev = pRe[p0 + j];
                regP[j] = (double.IsNaN(prev) || double.IsInfinity(prev) || prev == 0) ? 1.0 : prev;
                pRe[p0 + j] = regP[j]; pIm[p0 + j] = 0;
            }
            double lambda = 1e-3;
            double sse = RegResiduals(pid, regR);
            for (int iter = 0; iter < 80; iter++)
            {
                // Jacobian
                for (int j = 0; j < P; j++)
                {
                    double pj = regP[j];
                    double h = 1e-6 * System.Math.Max(1.0, System.Math.Abs(pj));
                    pRe[p0 + j] = pj + h;
                    RegResiduals(pid, regR2);
                    pRe[p0 + j] = pj;
                    for (int r = 0; r < B; r++) regJ[r * P + j] = (regR2[r] - regR[r]) / h;
                }
                // normal equations
                for (int a = 0; a < P; a++)
                {
                    double g = 0;
                    for (int r = 0; r < B; r++) { double rv = regR[r]; if (!double.IsNaN(rv)) g += regJ[r * P + a] * rv; }
                    regG[a] = g;
                    for (int b = 0; b < P; b++)
                    {
                        double acc = 0;
                        for (int r = 0; r < B; r++) { if (double.IsNaN(regR[r])) continue; acc += regJ[r * P + a] * regJ[r * P + b]; }
                        regA[a * P + b] = acc;
                    }
                }
                bool improved = false;
                double newSse = sse;
                for (int tries = 0; tries < 12; tries++)
                {
                    if (!SolveDamped(P, lambda)) { lambda *= 10; continue; }
                    for (int j = 0; j < P; j++) { regPNew[j] = regP[j] + regDelta[j]; pRe[p0 + j] = regPNew[j]; }
                    newSse = RegResiduals(pid, regR2);
                    if (!double.IsNaN(newSse) && newSse <= sse)
                    {
                        improved = true;
                        break;
                    }
                    lambda *= 4;
                }
                if (!improved)
                {
                    for (int j = 0; j < P; j++) pRe[p0 + j] = regP[j];
                    break;
                }
                double change = sse - newSse;
                for (int j = 0; j < P; j++) regP[j] = regPNew[j];
                System.Array.Copy(regR2, regR, B);
                sse = newSse;
                lambda = System.Math.Max(lambda / 3.0, 1e-12);
                if (change <= 1e-15 * (1 + sse)) break;
            }
            for (int j = 0; j < P; j++) pRe[p0 + j] = regP[j];
            regSSE = sse;
            // R^2 from lhs values
            string txt = "";
            for (int j = 0; j < P; j++)
            {
                txt += (j > 0 ? "   " : "") + compiler.GetRegParamName(d, j) + " = " + FormatNum(regP[j], 6);
            }
            int lhs = compiler.defProgB[d];
            if (lhs >= 0)
            {
                double mean = 0; int cnt = 0;
                for (int k0 = 0; k0 < B; k0 += CHUNK)
                {
                    int n = B - k0; if (n > CHUNK) n = CHUNK;
                    RunProg(lhs, n, k0, true);
                    for (int i = 0; i < n; i++) { double v = GetRe(0, i); regY[k0 + i] = v; if (!double.IsNaN(v)) { mean += v; cnt++; } }
                }
                if (cnt > 1)
                {
                    mean /= cnt;
                    double sst = 0;
                    for (int r = 0; r < B; r++) { double v = regY[r]; if (!double.IsNaN(v)) sst += (v - mean) * (v - mean); }
                    if (sst > 0) txt += "   R² = " + FormatNum(1 - sse / sst, 5);
                }
            }
            if (s >= 0) slotDisplay[s] = txt;
        }

        bool SolveDamped(int P, double lambda)
        {
            // M = A + lambda*diag(A), solve M delta = -g with Gaussian elimination (partial pivoting)
            int W = P + 1;
            double[] M = regJ; // reuse tail of Jacobian buffer as scratch (after rows) is unsafe; use separate area
            int baseIdx = MAX_REG_ROWS * MAX_REG_PARAMS - W * P;
            for (int a = 0; a < P; a++)
            {
                for (int b = 0; b < P; b++)
                {
                    double v = regA[a * P + b];
                    if (a == b) v += lambda * System.Math.Max(v, 1e-12);
                    M[baseIdx + a * W + b] = v;
                }
                M[baseIdx + a * W + P] = -regG[a];
            }
            for (int c = 0; c < P; c++)
            {
                int piv = c;
                double best = System.Math.Abs(M[baseIdx + c * W + c]);
                for (int r = c + 1; r < P; r++) { double v = System.Math.Abs(M[baseIdx + r * W + c]); if (v > best) { best = v; piv = r; } }
                if (best < 1e-300 || double.IsNaN(best)) return false;
                if (piv != c)
                {
                    for (int q = 0; q < W; q++) { double t = M[baseIdx + c * W + q]; M[baseIdx + c * W + q] = M[baseIdx + piv * W + q]; M[baseIdx + piv * W + q] = t; }
                }
                double dv = M[baseIdx + c * W + c];
                for (int r = 0; r < P; r++)
                {
                    if (r == c) continue;
                    double fct = M[baseIdx + r * W + c] / dv;
                    if (fct == 0) continue;
                    for (int q = c; q < W; q++) M[baseIdx + r * W + q] -= fct * M[baseIdx + c * W + q];
                }
            }
            for (int a = 0; a < P; a++) regDelta[a] = M[baseIdx + a * W + P] / M[baseIdx + a * W + a];
            return true;
        }

        // ------------------------------------------------------------------ display strings
        void BuildDisplays()
        {
            int sc = compiler.slotCount;
            int[] kinds = compiler.slotKind;
            for (int s = 0; s < SLOTS; s++)
            {
                if (s >= sc) { slotDisplay[s] = ""; continue; }
                int k = kinds[s];
                if (k == GCCompiler.K_REGRESSION) continue; // set by RunRegression
                slotDisplay[s] = "";
                if (k == GCCompiler.K_VALUE) slotDisplay[s] = "= " + ValueString(compiler.slotProg[s]);
                else if (k == GCCompiler.K_VAR && !compiler.slotIsSlider[s])
                {
                    int d = compiler.slotDef[s];
                    if (d < 0) continue;
                    int dk2 = compiler.defKind[d];
                    if (dk2 == GCCompiler.DK_SCALAR) slotDisplay[s] = "= " + FormatComplex(pRe[compiler.defParam[d]], pIm[compiler.defParam[d]], 10);
                    else if (dk2 == GCCompiler.DK_LIST) slotDisplay[s] = "= " + ListString(compiler.defList[d]);
                }
                else if (k == GCCompiler.K_POINTDEF)
                {
                    int d = compiler.slotDef[s];
                    if (d >= 0 && compiler.defKind[d] == GCCompiler.DK_POINT)
                    {
                        int p = compiler.defParam[d];
                        slotDisplay[s] = "= (" + FormatNum(pRe[p], 6) + ", " + FormatNum(pRe[p + 1], 6) + ")";
                    }
                    else if (d >= 0 && compiler.defKind[d] == GCCompiler.DK_POINTLIST)
                    {
                        slotDisplay[s] = lLen[compiler.defList[d]] + " points";
                    }
                }
            }
        }

        string ValueString(int pid)
        {
            if (pid < 0) return "";
            int B = ProgBroadcast(pid);
            if (B < 0)
            {
                double re = EvalScalar(pid);
                return FormatComplex(re, tIm, 10);
            }
            string s = "[";
            int shown = B < 8 ? B : 8;
            for (int k = 0; k < shown; k++)
            {
                RunProg(pid, 1, k, false);
                s += (k > 0 ? ", " : "") + FormatComplex(GetRe(0, 0), GetIm(0, 0), 6);
            }
            if (B > shown) s += ", …";
            return s + "]";
        }

        string ListString(int lid)
        {
            if (lid < 0) return "";
            int len = lLen[lid];
            int st = lStart[lid];
            string s = "[";
            int shown = len < 8 ? len : 8;
            for (int k = 0; k < shown; k++) s += (k > 0 ? ", " : "") + FormatComplex(lRe[st + k], lIm[st + k], 6);
            if (len > shown) s += ", … (" + len + ")";
            return s + "]";
        }

        // =================================================================== number formatting (culture-invariant)
        string FixedStr(double a, int dec)
        {
            if (dec > 15) dec = 15;
            double scale = 1;
            for (int i = 0; i < dec; i++) scale *= 10;
            double sv = System.Math.Round(a * scale);
            if (sv > 9.0e17) return "∞";
            long scaled = (long)sv;
            string digits = scaled.ToString();
            if (dec == 0) return digits;
            while (digits.Length <= dec) digits = "0" + digits;
            string ip = digits.Substring(0, digits.Length - dec);
            string fp = digits.Substring(digits.Length - dec);
            int end = fp.Length;
            while (end > 0 && fp[end - 1] == '0') end--;
            if (end == 0) return ip;
            return ip + "." + fp.Substring(0, end);
        }

        /// <summary>Format with up to sig significant digits. Uses ×10^n for very large/small values.</summary>
        public string FormatNum(double v, int sig)
        {
            if (double.IsNaN(v)) return "undefined";
            if (double.IsPositiveInfinity(v)) return "∞";
            if (double.IsNegativeInfinity(v)) return "-∞";
            if (v == 0) return "0";
            string sign = v < 0 ? "-" : "";
            double a = System.Math.Abs(v);
            int e = (int)System.Math.Floor(System.Math.Log10(a));
            if (e >= 12 || e < -6)
            {
                double mant = a / System.Math.Pow(10, e);
                string ms = FixedStr(mant, sig - 1);
                if (ms == "10") { ms = "1"; e += 1; }
                return sign + ms + "×10^" + e;
            }
            int dec = sig - 1 - e;
            if (dec < 0) dec = 0;
            string r = FixedStr(a, dec);
            if (r == "0") return "0";
            return sign + r;
        }

        /// <summary>Format with a fixed number of decimals (trailing zeros trimmed).</summary>
        public string FormatFixed(double v, int dec)
        {
            if (double.IsNaN(v)) return "0";
            string sign = v < 0 ? "-" : "";
            string r = FixedStr(System.Math.Abs(v), dec);
            if (r == "0") return "0";
            return sign + r;
        }

        public string FormatComplex(double re, double im, int sig)
        {
            if (!cplx || double.IsNaN(im) || System.Math.Abs(im) <= 1e-12 * System.Math.Max(1.0, System.Math.Abs(re)))
                return FormatNum(re, sig);
            if (double.IsNaN(re)) return "undefined";
            string ims = FormatNum(System.Math.Abs(im), sig);
            if (ims == "1") ims = "";
            if (System.Math.Abs(re) <= 1e-12 * System.Math.Abs(im)) return (im < 0 ? "-" : "") + ims + "i";
            return FormatNum(re, sig) + (im < 0 ? " - " : " + ") + ims + "i";
        }

        // =================================================================== view & styling
        public void SetSlotStyle(int s, Color c, bool visible)
        {
            if (s < 0 || s >= SLOTS) return;
            slotColor[s] = c;
            slotVisible[s] = visible;
            ApplySlotEnabled(s);
            ApplyUniforms(s);
        }

        void ApplySlotEnabled(int s)
        {
            bool v = slotVisible[s] && s < compiler.slotCount;
            SetRenderersEnabled(s, v && slotHasLines[s], v && slotHasFill[s], v && slotHasPoints[s], v && slotHasImplicit[s]);
        }

        public void SetView(double cx, double cy, double hw)
        {
            viewCx = cx; viewCy = cy; viewHw = hw;
            viewHh = hw * boardHeight / boardWidth;
            for (int s = 0; s < SLOTS; s++)
            {
                ApplyUniforms(s);
                if (!slotEverSampled[s]) continue;
                if (NeedsResample(s)) MarkDirty(s);
            }
        }

        bool NeedsResample(int s)
        {
            double ratio = viewHw / slotHwAtSample[s];
            if (ratio > 1.6 || ratio < 0.625)
            {
                if (slotHasLines[s] || slotHasFill[s] || slotHasImplicit[s]) return true;
            }
            double dist = System.Math.Abs(viewCx - slotOx[s]) + System.Math.Abs(viewCy - slotOy[s]);
            if (dist > 1000 * viewHw) return true;
            if (slotUsesDomX[s] && (viewCx - viewHw < slotDomX0[s] || viewCx + viewHw > slotDomX1[s])) return true;
            if (slotUsesDomY[s] && (viewCy - viewHh < slotDomY0[s] || viewCy + viewHh > slotDomY1[s])) return true;
            if (slotHasImplicit[s])
            {
                double gx1 = slotGx0[s] + slotGcx[s] * slotGw[s];
                double gy1 = slotGy0[s] + slotGcy[s] * slotGh[s];
                if (viewCx - viewHw < slotGx0[s] || viewCx + viewHw > gx1 || viewCy - viewHh < slotGy0[s] || viewCy + viewHh > gy1) return true;
            }
            // polar / parametric fills and lines are clamped to +-40 half-views around the origin
            if ((slotHasLines[s] || slotHasFill[s]) && dist > 20 * viewHw) return true;
            return false;
        }

        void ApplyUniforms(int s)
        {
            double k = boardWidth / (2.0 * viewHw);
            float sk = (float)(slotS[s] * k);
            Vector4 xf = new Vector4(sk, sk, (float)((slotOx[s] - viewCx) * k), (float)((slotOy[s] - viewCy) * k));
            Color c = slotColor[s];
            if (lineRenderers != null && s < lineRenderers.Length && lineRenderers[s] != null)
            {
                lineRenderers[s].GetPropertyBlock(mpb);
                mpb.SetVector("_Xform", xf);
                mpb.SetColor("_Color", c);
                mpb.SetFloat("_Dash", slotDashed[s] ? 1f : 0f);
                lineRenderers[s].SetPropertyBlock(mpb);
            }
            if (fillRenderers != null && s < fillRenderers.Length && fillRenderers[s] != null)
            {
                fillRenderers[s].GetPropertyBlock(mpb);
                mpb.SetVector("_Xform", xf);
                mpb.SetColor("_Color", c);
                fillRenderers[s].SetPropertyBlock(mpb);
            }
            if (pointRenderers != null && s < pointRenderers.Length && pointRenderers[s] != null)
            {
                pointRenderers[s].GetPropertyBlock(mpb);
                mpb.SetVector("_Xform", xf);
                mpb.SetColor("_Color", c);
                pointRenderers[s].SetPropertyBlock(mpb);
            }
            if (implicitRenderers != null && s < implicitRenderers.Length && implicitRenderers[s] != null && slotHasImplicit[s] && slotTex[s] != null)
            {
                double W = slotGw[s] + 1, H = slotGh[s] + 1;
                double ax = 1.0 / (k * slotGcx[s] * W);
                double ay = 1.0 / (k * slotGcy[s] * H);
                double bx = ((viewCx - slotGx0[s]) / slotGcx[s] + 0.5) / W;
                double by = ((viewCy - slotGy0[s]) / slotGcy[s] + 0.5) / H;
                implicitRenderers[s].GetPropertyBlock(mpb);
                mpb.SetVector("_GridXform", new Vector4((float)ax, (float)ay, (float)bx, (float)by));
                mpb.SetFloat("_CellBoard", (float)(slotGcx[s] * k));
                mpb.SetFloat("_Mode", slotImplicitMode[s]);
                mpb.SetColor("_Color", c);
                mpb.SetTexture("_MainTex", slotTex[s]);
                implicitRenderers[s].SetPropertyBlock(mpb);
            }
        }

        // =================================================================== dirty tracking & scheduling
        public void MarkAllDirty()
        {
            for (int s = 0; s < SLOTS; s++) MarkDirty(s);
        }

        public void MarkDirty(int s)
        {
            if (s < 0 || s >= SLOTS) return;
            // If s is being sampled right now, let the current pass finish (so animated
            // parameters still make progress); the dirty flag re-queues it afterwards.
            slotDirty[s] = true;
        }

        public void MarkDirtyByDefMask(long mask)
        {
            long[] masks = compiler.slotDefMask;
            int sc = compiler.slotCount;
            for (int s = 0; s < sc; s++) if ((masks[s] & mask) != 0) MarkDirty(s);
        }

        public bool IsBusy()
        {
            if (curSlot >= 0) return true;
            for (int s = 0; s < SLOTS; s++) if (slotDirty[s]) return true;
            return false;
        }

        public bool IsSlotBusy(int s) { return s == curSlot || slotDirty[s]; }

        int PickNextDirty()
        {
            for (int s = 0; s < SLOTS; s++) if (slotDirty[s]) return s;
            return -1;
        }

        /// <summary>Run sampling work for up to budgetMs milliseconds. Returns true if work remains.</summary>
        public bool Work(float budgetMs)
        {
            if (!initialized || code == null) return false;
            double t0 = Time.realtimeSinceStartupAsDouble;
            double budget = budgetMs / 1000.0;
            int guard = 0;
            while (Time.realtimeSinceStartupAsDouble - t0 < budget && guard < 100000)
            {
                guard++;
                if (curSlot < 0)
                {
                    curSlot = PickNextDirty();
                    if (curSlot < 0) return false;
                    StartSlot(curSlot);
                }
                if (restartSlot) { restartSlot = false; StartSlot(curSlot); }
                bool done = StepSlot();
                if (done)
                {
                    FinishSlot(curSlot);
                    curSlot = -1;
                }
            }
            return true;
        }

        void StartSlot(int s)
        {
            slotDirty[s] = false;
            restartSlot = false;
            jobVcx = viewCx; jobVcy = viewCy; jobHw = viewHw; jobHh = viewHh;
            segCount = 0; fillCount = 0; ptCount = 0;
            jobEntryCount = 0;
            int ec = compiler.entryCount;
            int[] es = compiler.eSlot;
            if (s < compiler.slotCount)
            {
                for (int e = 0; e < ec; e++) if (es[e] == s && jobEntryCount < jobEntries.Length) jobEntries[jobEntryCount++] = e;
            }
            jobEntryIdx = 0;
            jobStage = 0;
            nextHasImplicit = false;
            nextDashed = false;
            nextUsesDomX = false;
            nextUsesDomY = false;
            nextDomX0 = 0; nextDomX1 = 0; nextDomY0 = 0; nextDomY1 = 0;
        }

        bool nextHasImplicit;
        bool nextDashed;
        bool nextUsesDomX;
        bool nextUsesDomY;
        double nextDomX0;
        double nextDomX1;
        double nextDomY0;
        double nextDomY1;

        bool StepSlot()
        {
            if (jobEntryIdx >= jobEntryCount) return true;
            int e = jobEntries[jobEntryIdx];
            int kind = compiler.eKind[e];
            bool entryDone;
            if (kind == GCCompiler.E_POINTS || kind == GCCompiler.E_VALUEPOINT) entryDone = PointsStep(e);
            else if (kind == GCCompiler.E_IMPLICIT) entryDone = ImplicitStep(e);
            else entryDone = CurveStep(e);
            if (entryDone) { jobEntryIdx++; jobStage = 0; }
            return jobEntryIdx >= jobEntryCount;
        }

        // =================================================================== points
        bool PointsStep(int e)
        {
            int pa = compiler.eProgA[e];
            int pb = compiler.eProgB[e];
            bool valuePoint = compiler.eKind[e] == GCCompiler.E_VALUEPOINT;
            int ba = ProgBroadcast(pa);
            int bb = valuePoint ? -1 : ProgBroadcast(pb);
            int B;
            bool kvec;
            if (ba < 0 && bb < 0) { B = 1; kvec = false; }
            else { B = ba < 0 ? bb : (bb < 0 ? ba : (ba < bb ? ba : bb)); kvec = true; }
            for (int k0 = 0; k0 < B; k0 += CHUNK)
            {
                int n = B - k0; if (n > CHUNK) n = CHUNK;
                if (!kvec) n = 1;
                RunProg(pa, n, k0, kvec);
                Materialize(0, n);
                for (int i = 0; i < n; i++) { tmpA[i] = sRe[0][i]; }
                if (valuePoint)
                {
                    for (int i = 0; i < n; i++)
                    {
                        double re = tmpA[i], im = sIm[0][i];
                        if (System.Math.Abs(im) <= 1e-12 * System.Math.Max(1, System.Math.Abs(re))) continue;
                        AddPoint(re, im);
                    }
                    continue;
                }
                RunProg(pb, n, k0, kvec);
                Materialize(0, n);
                for (int i = 0; i < n; i++)
                {
                    double x = tmpA[i], y = sRe[0][i];
                    if (cplx && (System.Math.Abs(sIm[0][i]) > 1e-9 * System.Math.Max(1, System.Math.Abs(y)))) continue;
                    AddPoint(x, y);
                }
            }
            return true;
        }

        void AddPoint(double x, double y)
        {
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) return;
            if (ptCount >= MAX_POINTS) return;
            ptX[ptCount] = x; ptY[ptCount] = y; ptCount++;
        }

        // =================================================================== curves (explicit, polar, parametric)
        bool CurveStep(int e)
        {
            int kind = compiler.eKind[e];
            if (jobStage == 0)
            {
                int ba = ProgBroadcast(compiler.eProgA[e]);
                int bb = kind == GCCompiler.E_PARAM ? ProgBroadcast(compiler.eProgB[e]) : -1;
                int B = (ba < 0 && bb < 0) ? 1 : (ba < 0 ? bb : (bb < 0 ? ba : (ba < bb ? ba : bb)));
                if (B <= 0) return true;
                if (B > 64) B = 64;
                jobB = B;
                jobK = 0;
                // domain
                if (kind == GCCompiler.E_EXPLICIT_Y) { jobLo = jobVcx - 2 * jobHw; jobHi = jobVcx + 2 * jobHw; nextUsesDomX = true; nextDomX0 = jobLo; nextDomX1 = jobHi; }
                else if (kind == GCCompiler.E_EXPLICIT_X) { jobLo = jobVcy - 2 * jobHh; jobHi = jobVcy + 2 * jobHh; nextUsesDomY = true; nextDomY0 = jobLo; nextDomY1 = jobHi; }
                else
                {
                    double lo = compiler.eDomLoDef[e], hi = compiler.eDomHiDef[e];
                    double degScale = (kind == GCCompiler.E_POLAR && compiler.degreeMode) ? System.Math.PI / 180.0 : 1.0;
                    if (compiler.eDomLo[e] >= 0) lo = EvalScalar(compiler.eDomLo[e]) * degScale;
                    if (compiler.eDomHi[e] >= 0) hi = EvalScalar(compiler.eDomHi[e]) * degScale;
                    if (double.IsNaN(lo) || double.IsNaN(hi) || double.IsInfinity(lo) || double.IsInfinity(hi) || hi <= lo) return true;
                    jobLo = lo; jobHi = hi;
                }
                int rel = compiler.eRel[e];
                if (rel == GCCompiler.R_LT || rel == GCCompiler.R_GT) nextDashed = true;
                jobStage = 1;
                return false;
            }
            if (jobStage == 1)
            {
                // initialise samples for broadcast index jobK
                int N0 = baseSamples;
                if (kind == GCCompiler.E_POLAR || kind == GCCompiler.E_PARAM)
                {
                    double span = jobHi - jobLo;
                    if (kind == GCCompiler.E_POLAR) N0 = (int)(baseSamples * System.Math.Max(1.0, span / (2 * System.Math.PI)) * 0.5);
                    if (N0 < baseSamples) N0 = baseSamples;
                    if (N0 > maxCurvePoints / 2) N0 = maxCurvePoints / 2;
                }
                cm = N0 + 1;
                double step = (jobHi - jobLo) / N0;
                for (int i = 0; i < cm; i++) cu[i] = jobLo + step * i;
                jobCursor = 0;
                jobLevel = 0;
                jobStage = 2;
                return false;
            }
            if (jobStage == 2)
            {
                int n = cm - jobCursor; if (n > CHUNK) n = CHUNK;
                EvalCurve(e, kind, cu, jobCursor, n, cpx, cpy);
                jobCursor += n;
                if (jobCursor >= cm) jobStage = 3;
                return false;
            }
            if (jobStage == 3)
            {
                // plan refinement
                mc = 0;
                if (jobLevel < refineLevels)
                {
                    for (int i = 0; i < cm - 1; i++)
                    {
                        bool rf = NeedsRefine(i);
                        refineFlag[i] = rf;
                        if (rf) { mu[mc] = 0.5 * (cu[i] + cu[i + 1]); mc++; }
                    }
                }
                if (mc == 0 || cm + mc > maxCurvePoints)
                {
                    jobStage = 5;
                    return false;
                }
                jobCursor = 0;
                jobStage = 4;
                return false;
            }
            if (jobStage == 4)
            {
                int n = mc - jobCursor; if (n > CHUNK) n = CHUNK;
                EvalCurve(e, kind, mu, jobCursor, n, mpx, mpy);
                jobCursor += n;
                if (jobCursor >= mc)
                {
                    // merge
                    int o = 0, m = 0;
                    for (int i = 0; i < cm; i++)
                    {
                        nu[o] = cu[i]; npx[o] = cpx[i]; npy[o] = cpy[i]; o++;
                        if (i < cm - 1 && refineFlag[i]) { nu[o] = mu[m]; npx[o] = mpx[m]; npy[o] = mpy[m]; o++; m++; }
                    }
                    double[] t = cu; cu = nu; nu = t;
                    t = cpx; cpx = npx; npx = t;
                    t = cpy; cpy = npy; npy = t;
                    cm = o;
                    jobLevel++;
                    jobStage = 3;
                }
                return false;
            }
            // stage 5: emit
            EmitCurve(e, kind);
            jobK++;
            if (jobK < jobB) { jobStage = 1; return false; }
            return true;
        }

        void EvalCurve(int e, int kind, double[] us, int off, int n, double[] outX, double[] outY)
        {
            int pa = compiler.eProgA[e];
            int varSlot = kind == GCCompiler.E_EXPLICIT_Y ? GCCompiler.VAR_X : kind == GCCompiler.E_EXPLICIT_X ? GCCompiler.VAR_Y :
                          kind == GCCompiler.E_POLAR ? GCCompiler.VAR_THETA : GCCompiler.VAR_T;
            bool degTheta = kind == GCCompiler.E_POLAR && compiler.degreeMode;
            if (degTheta)
            {
                double[] V = vRe[varSlot];
                vSc[varSlot] = false;
                for (int i = 0; i < n; i++) V[i] = us[off + i] * (180.0 / System.Math.PI);
            }
            else SetVarVector(varSlot, us, off, n);
            RunProg(pa, n, jobK, false);
            Materialize(0, n);
            double[] R = sRe[0];
            double[] I = sIm[0];
            if (cplx) for (int i = 0; i < n; i++) if (System.Math.Abs(I[i]) > 1e-9 * System.Math.Max(1.0, System.Math.Abs(R[i]))) R[i] = double.NaN;
            if (kind == GCCompiler.E_EXPLICIT_Y)
            {
                System.Array.Copy(us, off, outX, off, n);
                System.Array.Copy(R, 0, outY, off, n);
                return;
            }
            if (kind == GCCompiler.E_EXPLICIT_X)
            {
                System.Array.Copy(R, 0, outX, off, n);
                System.Array.Copy(us, off, outY, off, n);
                return;
            }
            if (kind == GCCompiler.E_POLAR)
            {
                for (int i = 0; i < n; i++)
                {
                    double th = us[off + i];
                    double r = R[i];
                    outX[off + i] = r * System.Math.Cos(th);
                    outY[off + i] = r * System.Math.Sin(th);
                }
                return;
            }
            // parametric: second program
            System.Array.Copy(R, 0, outX, off, n);
            SetVarVector(varSlot, us, off, n);
            RunProg(compiler.eProgB[e], n, jobK, false);
            Materialize(0, n);
            R = sRe[0]; I = sIm[0];
            for (int i = 0; i < n; i++)
            {
                double y = R[i];
                if (cplx && System.Math.Abs(I[i]) > 1e-9 * System.Math.Max(1.0, System.Math.Abs(y))) y = double.NaN;
                outY[off + i] = y;
            }
        }

        bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        bool NeedsRefine(int i)
        {
            double x0 = cpx[i], y0 = cpy[i], x1 = cpx[i + 1], y1 = cpy[i + 1];
            bool f0 = Finite(x0) && Finite(y0);
            bool f1 = Finite(x1) && Finite(y1);
            if (f0 != f1) return true;
            if (!f0) return false;
            double top = jobVcy + 1.2 * jobHh, bot = jobVcy - 1.2 * jobHh;
            double rgt = jobVcx + 1.2 * jobHw, lft = jobVcx - 1.2 * jobHw;
            if ((y0 > top && y1 > top) || (y0 < bot && y1 < bot) || (x0 > rgt && x1 > rgt) || (x0 < lft && x1 < lft)) return false;
            double dx = (x1 - x0) / jobHw, dy = (y1 - y0) / jobHh;
            double len2 = dx * dx + dy * dy;
            if (len2 > 0.0009) return true; // > 3% of half-view
            if (len2 < 1e-10) return false;
            // turning angle at ends
            if (i > 0 && Finite(cpx[i - 1]) && Finite(cpy[i - 1]))
            {
                double ax = (x0 - cpx[i - 1]) / jobHw, ay = (y0 - cpy[i - 1]) / jobHh;
                double al2 = ax * ax + ay * ay;
                if (al2 > 1e-12)
                {
                    double dot = (ax * dx + ay * dy) / System.Math.Sqrt(al2 * len2);
                    if (dot < 0.995) return true;
                }
            }
            if (i + 2 < cm && Finite(cpx[i + 2]) && Finite(cpy[i + 2]))
            {
                double bx = (cpx[i + 2] - x1) / jobHw, by = (cpy[i + 2] - y1) / jobHh;
                double bl2 = bx * bx + by * by;
                if (bl2 > 1e-12)
                {
                    double dot = (bx * dx + by * dy) / System.Math.Sqrt(bl2 * len2);
                    if (dot < 0.995) return true;
                }
            }
            return false;
        }

        double SegLenView(int i)
        {
            if (i < 0 || i + 1 >= cm) return -1;
            double x0 = cpx[i], y0 = cpy[i], x1 = cpx[i + 1], y1 = cpy[i + 1];
            if (!(Finite(x0) && Finite(y0) && Finite(x1) && Finite(y1))) return -1;
            double dx = (x1 - x0) / jobHw, dy = (y1 - y0) / jobHh;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        double ClampX(double x) { double lim = 40 * jobHw; if (x > jobVcx + lim) return jobVcx + lim; if (x < jobVcx - lim) return jobVcx - lim; return x; }
        double ClampY(double y) { double lim = 40 * jobHh; if (y > jobVcy + lim) return jobVcy + lim; if (y < jobVcy - lim) return jobVcy - lim; return y; }

        void EmitCurve(int e, int kind)
        {
            int rel = compiler.eRel[e];
            bool fill = rel != GCCompiler.R_EQ && rel != GCCompiler.R_NONE;
            bool above = rel == GCCompiler.R_GT || rel == GCCompiler.R_GE;
            double arc = 0;
            bool prevDrawn = false;
            double top = jobVcy + 1.05 * jobHh, bot = jobVcy - 1.05 * jobHh;
            double rgt = jobVcx + 1.05 * jobHw, lft = jobVcx - 1.05 * jobHw;
            double farR = 40 * System.Math.Max(jobHw, jobHh) + System.Math.Abs(jobVcx) + System.Math.Abs(jobVcy);
            for (int i = 0; i < cm - 1; i++)
            {
                double L = SegLenView(i);
                if (L < 0) { prevDrawn = false; continue; }
                // discontinuity: long segment between short neighbours after refinement
                if (L > 0.08)
                {
                    double Lp = SegLenView(i - 1), Ln = SegLenView(i + 1);
                    bool jump = (Lp < 0 || L > 6 * Lp) && (Ln < 0 || L > 6 * Ln);
                    if (jump && jobLevel >= 2) { prevDrawn = false; continue; }
                }
                double x0 = cpx[i], y0 = cpy[i], x1 = cpx[i + 1], y1 = cpy[i + 1];
                if (fill) AddFill(kind, above, x0, y0, x1, y1, farR);
                bool off = (y0 > top && y1 > top) || (y0 < bot && y1 < bot) || (x0 > rgt && x1 > rgt) || (x0 < lft && x1 < lft);
                double segLenGraph = L * jobHw; // approx graph units (x-scaled)
                if (!prevDrawn) arc = 0;
                if (segCount < MAX_SEG && !off)
                {
                    segAx[segCount] = ClampX(x0); segAy[segCount] = ClampY(y0);
                    segBx[segCount] = ClampX(x1); segBy[segCount] = ClampY(y1);
                    segArcA[segCount] = arc;
                    segArcB[segCount] = arc + segLenGraph;
                    segCount++;
                }
                arc += segLenGraph;
                prevDrawn = true;
            }
        }

        void AddFill(int kind, bool above, double x0, double y0, double x1, double y1, double farR)
        {
            if (fillCount >= MAX_FILL) return;
            int b = fillCount * 8;
            if (kind == GCCompiler.E_EXPLICIT_Y)
            {
                double edge = above ? jobVcy + 40 * jobHh : jobVcy - 40 * jobHh;
                fillQ[b] = x0; fillQ[b + 1] = ClampY(y0); fillQ[b + 2] = x1; fillQ[b + 3] = ClampY(y1);
                fillQ[b + 4] = x1; fillQ[b + 5] = edge; fillQ[b + 6] = x0; fillQ[b + 7] = edge;
            }
            else if (kind == GCCompiler.E_EXPLICIT_X)
            {
                double edge = above ? jobVcx + 40 * jobHw : jobVcx - 40 * jobHw;
                fillQ[b] = ClampX(x0); fillQ[b + 1] = y0; fillQ[b + 2] = ClampX(x1); fillQ[b + 3] = y1;
                fillQ[b + 4] = edge; fillQ[b + 5] = y1; fillQ[b + 6] = edge; fillQ[b + 7] = y0;
            }
            else if (kind == GCCompiler.E_POLAR)
            {
                if (!above)
                {
                    fillQ[b] = 0; fillQ[b + 1] = 0; fillQ[b + 2] = ClampX(x0); fillQ[b + 3] = ClampY(y0);
                    fillQ[b + 4] = ClampX(x1); fillQ[b + 5] = ClampY(y1); fillQ[b + 6] = 0; fillQ[b + 7] = 0;
                }
                else
                {
                    double r0 = System.Math.Sqrt(x0 * x0 + y0 * y0), r1 = System.Math.Sqrt(x1 * x1 + y1 * y1);
                    double s0 = r0 > 1e-12 ? farR / r0 : 0, s1 = r1 > 1e-12 ? farR / r1 : 0;
                    fillQ[b] = ClampX(x0); fillQ[b + 1] = ClampY(y0); fillQ[b + 2] = ClampX(x1); fillQ[b + 3] = ClampY(y1);
                    fillQ[b + 4] = x1 * s1; fillQ[b + 5] = y1 * s1; fillQ[b + 6] = x0 * s0; fillQ[b + 7] = y0 * s0;
                }
            }
            else return;
            fillCount++;
        }

        // =================================================================== implicit
        bool ImplicitStep(int e)
        {
            int pid = compiler.eProgA[e];
            if (jobStage == 0)
            {
                int B = ProgBroadcast(pid);
                if (B == 0) return true;
                if (B < 0) B = 1;
                if (B > 16) B = 16;
                jobB = B; jobK = 0;
                gridW = implicitRes;
                gridH = (int)System.Math.Round(gridW * jobHh / jobHw);
                if (gridH < 8) gridH = 8;
                if (gridH > MAX_GRID - 1) gridH = MAX_GRID - 1;
                double mx = 1.3 * jobHw, my = 1.3 * jobHh;
                gridX0 = jobVcx - mx; gridY0 = jobVcy - my;
                gridCx = 2 * mx / gridW; gridCy = 2 * my / gridH;
                for (int i = 0; i <= gridW; i++) gridXs[i] = gridX0 + i * gridCx;
                gridRegion = compiler.eRegion[e];
                int rel = compiler.eRel[e];
                gridMode = gridRegion ? 3 : (rel == GCCompiler.R_EQ ? 0 : ((rel == GCCompiler.R_LT || rel == GCCompiler.R_GT) ? 2 : 1));
                jobCursor = 0;
                jobStage = 1;
                return false;
            }
            int W1 = gridW + 1;
            if (jobStage == 1)
            {
                // two rows per step
                for (int rep = 0; rep < 2 && jobCursor <= gridH; rep++)
                {
                    int j = jobCursor;
                    SetVarVector(GCCompiler.VAR_X, gridXs, 0, W1);
                    SetVarScalar(GCCompiler.VAR_Y, gridY0 + j * gridCy);
                    RunProg(pid, W1, jobK, false);
                    Materialize(0, W1);
                    double[] R = sRe[0], I = sIm[0];
                    int rowBase = j * W1;
                    int rel = compiler.eRel[e];
                    bool negate = rel == GCCompiler.R_LT || rel == GCCompiler.R_LE;
                    for (int i = 0; i < W1; i++)
                    {
                        double v = R[i];
                        if (cplx && System.Math.Abs(I[i]) > 1e-9 * System.Math.Max(1.0, System.Math.Abs(v))) v = double.NaN;
                        if (gridRegion) v = double.IsNaN(v) ? double.NaN : (v == 1.0 ? 1.0 : -1.0);
                        else if (negate) v = -v;
                        gridF[rowBase + i] = v;
                    }
                    jobCursor++;
                }
                if (jobCursor > gridH) jobStage = 2;
                return false;
            }
            if (jobStage == 2)
            {
                ComputeDistance(W1, gridH + 1);
                jobK++;
                if (jobK < jobB) { jobCursor = 0; jobStage = 1; return false; }
                jobStage = 3;
                return false;
            }
            // stage 3: write texture
            WriteImplicitTexture(curSlot, W1, gridH + 1);
            nextHasImplicit = true;
            return true;
        }

        const double BIG = 6.0;

        void ComputeDistance(int W, int H)
        {
            int total = W * H;
            for (int j = 0; j < H; j++)
            {
                for (int i = 0; i < W; i++)
                {
                    int id = j * W + i;
                    double f = gridF[id];
                    if (double.IsNaN(f) || double.IsInfinity(f))
                    {
                        gridDk[id] = double.IsPositiveInfinity(f) ? BIG : -BIG;
                        gridVk[id] = 0;
                        gridGX[id] = 0; gridGY[id] = 0;
                        continue;
                    }
                    if (gridRegion)
                    {
                        gridDk[id] = f * 0.5;
                        gridVk[id] = 1;
                        gridGX[id] = 0; gridGY[id] = 0;
                        continue;
                    }
                    double fl = i > 0 ? gridF[id - 1] : double.NaN;
                    double fr = i < W - 1 ? gridF[id + 1] : double.NaN;
                    double fd = j > 0 ? gridF[id - W] : double.NaN;
                    double fu = j < H - 1 ? gridF[id + W] : double.NaN;
                    double gx, gy;
                    bool okl = Finite(fl), okr = Finite(fr), okd = Finite(fd), oku = Finite(fu);
                    if (okl && okr) gx = 0.5 * (fr - fl); else if (okr) gx = fr - f; else if (okl) gx = f - fl; else gx = 0;
                    if (okd && oku) gy = 0.5 * (fu - fd); else if (oku) gy = fu - f; else if (okd) gy = f - fd; else gy = 0;
                    double g = System.Math.Sqrt(gx * gx + gy * gy);
                    double d;
                    if (g > 1e-300) d = f / g; else d = f > 0 ? BIG : (f < 0 ? -BIG : 0);
                    if (d > BIG) d = BIG; else if (d < -BIG) d = -BIG;
                    gridDk[id] = d;
                    gridVk[id] = 1;
                    if (g > 1e-300) { gridGX[id] = gx / g; gridGY[id] = gy / g; } else { gridGX[id] = 0; gridGY[id] = 0; }
                }
            }
            if (!gridRegion)
            {
                // discontinuity detection: sign change between far-away values
                for (int j = 0; j < H; j++)
                {
                    for (int i = 0; i < W; i++)
                    {
                        int id = j * W + i;
                        double a = gridDk[id];
                        if (i < W - 1)
                        {
                            double b = gridDk[id + 1];
                            if (a * b < 0 && System.Math.Abs(a) > 1.5 && System.Math.Abs(b) > 1.5) { gridVk[id] = 0; gridVk[id + 1] = 0; }
                        }
                        if (j < H - 1)
                        {
                            double b = gridDk[id + W];
                            if (a * b < 0 && System.Math.Abs(a) > 1.5 && System.Math.Abs(b) > 1.5) { gridVk[id] = 0; gridVk[id + W] = 0; }
                        }
                    }
                }
            }
            // combine with previous broadcast layers
            if (jobK == 0)
            {
                System.Array.Copy(gridDk, gridD, total);
                System.Array.Copy(gridVk, gridV, total);
                return;
            }
            bool eq = gridMode == 0;
            for (int id = 0; id < total; id++)
            {
                double a = gridD[id], b = gridDk[id];
                double va = gridV[id], vb = gridVk[id];
                if (eq)
                {
                    if (vb > 0 && (va <= 0 || System.Math.Abs(b) < System.Math.Abs(a))) { gridD[id] = b; gridV[id] = vb; }
                }
                else
                {
                    if (vb > 0 && (va <= 0 || b > a)) { gridD[id] = b; gridV[id] = vb; }
                }
            }
        }

        void WriteImplicitTexture(int s, int W, int H)
        {
            Texture2D tex = slotTex[s];
            if (tex == null || tex.width != W || tex.height != H)
            {
                tex = new Texture2D(W, H, TextureFormat.RGBAHalf, false, true);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                slotTex[s] = tex;
                slotTexData[s] = new Color[W * H];
            }
            Color[] data = slotTexData[s];
            int total = W * H;
            for (int id = 0; id < total; id++)
            {
                data[id] = new Color((float)gridD[id], (float)gridV[id], (float)gridGX[id], (float)gridGY[id]);
            }
            tex.SetPixels(data);
            tex.Apply(false, false);
            slotGx0[s] = gridX0; slotGy0[s] = gridY0;
            slotGcx[s] = gridCx; slotGcy[s] = gridCy;
            slotGw[s] = gridW; slotGh[s] = gridH;
            slotImplicitMode[s] = gridMode;
        }

        // =================================================================== finish: build meshes
        void FinishSlot(int s)
        {
            slotOx[s] = jobVcx; slotOy[s] = jobVcy; slotS[s] = jobHw;
            slotHwAtSample[s] = jobHw;
            slotEverSampled[s] = true;
            slotUsesDomX[s] = nextUsesDomX; slotDomX0[s] = nextDomX0; slotDomX1[s] = nextDomX1;
            slotUsesDomY[s] = nextUsesDomY; slotDomY0[s] = nextDomY0; slotDomY1[s] = nextDomY1;
            slotDashed[s] = nextDashed;
            slotHasImplicit[s] = nextHasImplicit;
            dbgInfo = "segments=" + segCount + " fills=" + fillCount + " points=" + ptCount + " implicit=" + (nextHasImplicit ? "yes" : "no");
            if (dbgSlotInfo != null && s < dbgSlotInfo.Length) dbgSlotInfo[s] = dbgInfo;
            BuildLineMesh(s);
            BuildFillMesh(s);
            BuildPointMesh(s);
            ApplySlotEnabled(s);
            ApplyUniforms(s);
        }

        void BuildLineMesh(int s)
        {
            Mesh m = lineMeshes[s];
            slotHasLines[s] = segCount > 0;
            if (m == null) return;
            m.Clear();
            if (segCount == 0) return;
            double ox = jobVcx, oy = jobVcy, inv = 1.0 / jobHw;
            int nv = segCount * 4;
            for (int q = 0; q < segCount; q++)
            {
                float ax = (float)((segAx[q] - ox) * inv), ay = (float)((segAy[q] - oy) * inv);
                float bx = (float)((segBx[q] - ox) * inv), by = (float)((segBy[q] - oy) * inv);
                float a0 = (float)(segArcA[q] * inv), a1 = (float)(segArcB[q] * inv);
                Vector3 pa = new Vector3(ax, ay, 0);
                int v = q * 4;
                mVerts[v] = pa; mVerts[v + 1] = pa; mVerts[v + 2] = pa; mVerts[v + 3] = pa;
                mUv0[v] = new Vector4(bx, by, -1, 0);
                mUv0[v + 1] = new Vector4(bx, by, 1, 0);
                mUv0[v + 2] = new Vector4(bx, by, -1, 1);
                mUv0[v + 3] = new Vector4(bx, by, 1, 1);
                Vector4 arcv = new Vector4(a0, a1, 0, 0);
                mUv1[v] = arcv; mUv1[v + 1] = arcv; mUv1[v + 2] = arcv; mUv1[v + 3] = arcv;
            }
            m.SetVertices(mVerts, 0, nv);
            m.SetUVs(0, mUv0, 0, nv);
            m.SetUVs(1, mUv1, 0, nv);
            m.SetTriangles(mTriLine, 0, segCount * 6, 0, false, 0);
            m.bounds = new Bounds(Vector3.zero, new Vector3(boardWidth, boardHeight, 0.2f));
        }

        void BuildFillMesh(int s)
        {
            Mesh m = fillMeshes[s];
            slotHasFill[s] = fillCount > 0;
            if (m == null) return;
            m.Clear();
            if (fillCount == 0) return;
            double ox = jobVcx, oy = jobVcy, inv = 1.0 / jobHw;
            int nv = fillCount * 4;
            for (int q = 0; q < fillCount; q++)
            {
                int b = q * 8;
                int v = q * 4;
                for (int c = 0; c < 4; c++)
                {
                    mVerts[v + c] = new Vector3((float)((fillQ[b + c * 2] - ox) * inv), (float)((fillQ[b + c * 2 + 1] - oy) * inv), 0);
                    mUv0[v + c] = Vector4.zero;
                }
            }
            m.SetVertices(mVerts, 0, nv);
            m.SetUVs(0, mUv0, 0, nv);
            m.SetTriangles(mTriQuad, 0, fillCount * 6, 0, false, 0);
            m.bounds = new Bounds(Vector3.zero, new Vector3(boardWidth, boardHeight, 0.2f));
        }

        void BuildPointMesh(int s)
        {
            Mesh m = pointMeshes[s];
            slotHasPoints[s] = ptCount > 0;
            if (m == null) return;
            m.Clear();
            if (ptCount == 0) return;
            double ox = jobVcx, oy = jobVcy, inv = 1.0 / jobHw;
            int nv = ptCount * 4;
            for (int q = 0; q < ptCount; q++)
            {
                Vector3 p = new Vector3((float)((ptX[q] - ox) * inv), (float)((ptY[q] - oy) * inv), 0);
                int v = q * 4;
                mVerts[v] = p; mVerts[v + 1] = p; mVerts[v + 2] = p; mVerts[v + 3] = p;
                mUv0[v] = new Vector4(-1, -1, 0, 0);
                mUv0[v + 1] = new Vector4(1, -1, 0, 0);
                mUv0[v + 2] = new Vector4(1, 1, 0, 0);
                mUv0[v + 3] = new Vector4(-1, 1, 0, 0);
            }
            m.SetVertices(mVerts, 0, nv);
            m.SetUVs(0, mUv0, 0, nv);
            m.SetTriangles(mTriQuad, 0, ptCount * 6, 0, false, 0);
            m.bounds = new Bounds(Vector3.zero, new Vector3(boardWidth, boardHeight, 0.2f));
        }

        /// <summary>Clears geometry for slots that no longer exist.</summary>
        public void ClearSlot(int s)
        {
            if (s < 0 || s >= SLOTS) return;
            slotDirty[s] = false;
            if (s == curSlot) { curSlot = -1; }
            slotHasLines[s] = false; slotHasFill[s] = false; slotHasPoints[s] = false; slotHasImplicit[s] = false;
            if (lineMeshes[s] != null) lineMeshes[s].Clear();
            if (fillMeshes[s] != null) fillMeshes[s].Clear();
            if (pointMeshes[s] != null) pointMeshes[s].Clear();
            slotDisplay[s] = "";
            SetRenderersEnabled(s, false, false, false, false);
        }

        // =================================================================== trace query (used by controller for tap-to-inspect)
        /// <summary>Evaluates a value-slot program for display (scalar only).</summary>
        public double EvalExplicitAt(int e, double x)
        {
            if (code == null) return double.NaN;
            int kind = compiler.eKind[e];
            if (kind != GCCompiler.E_EXPLICIT_Y && kind != GCCompiler.E_EXPLICIT_X) return double.NaN;
            SetVarScalar(kind == GCCompiler.E_EXPLICIT_Y ? GCCompiler.VAR_X : GCCompiler.VAR_Y, x);
            RunProg(compiler.eProgA[e], 1, 0, false);
            double re = GetRe(0, 0);
            if (cplx && System.Math.Abs(GetIm(0, 0)) > 1e-9 * System.Math.Max(1.0, System.Math.Abs(re))) return double.NaN;
            return re;
        }
    }
}
