using UdonSharp;
using UnityEngine;

namespace GraphCalc
{
    /// <summary>
    /// Tokenizer, parser, classifier and bytecode compiler for Desmos-style expressions.
    /// Runs only when expression text changes. All output lives in public arrays that
    /// GCEngine reads directly (vectorised stack-machine bytecode).
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GCCompiler : UdonSharpBehaviour
    {
        // ------------------------------------------------------------------ limits
        public const int MAX_SLOTS = 16;
        public const int MAX_TOK = 1024;
        public const int MAX_NODES = 12000;
        public const int MAX_ARGS = 8000;
        public const int MAX_CODE = 80000;
        public const int MAX_CONST = 16000;
        public const int MAX_PROGS = 512;
        public const int MAX_DEFS = 64;
        public const int MAX_PARAMS = 256;
        public const int MAX_LISTS = 128;
        public const int MAX_ENTRIES = 64;
        public const int MAX_ENV = 512;
        public const int MAX_VARS = 32;
        public const int MAX_FRAMES = 64;
        public const int HDR_SIZE = 10;
        public const int MAX_SRC = 8;
        public const int MAX_FUNCS = 16;
        public const int MAX_FPARAMS = 6;
        public const int MAX_UNDEF = 16;

        // ------------------------------------------------------------------ tokens
        public const int T_END = 0;
        public const int T_NUM = 1;
        public const int T_IDENT = 2;
        public const int T_FUNC = 3;
        public const int T_PLUS = 4;
        public const int T_MINUS = 5;
        public const int T_STAR = 6;
        public const int T_SLASH = 7;
        public const int T_CARET = 8;
        public const int T_LPAREN = 9;
        public const int T_RPAREN = 10;
        public const int T_LBRACK = 11;
        public const int T_RBRACK = 12;
        public const int T_LBRACE = 13;
        public const int T_RBRACE = 14;
        public const int T_COMMA = 15;
        public const int T_EQ = 16;
        public const int T_LT = 17;
        public const int T_GT = 18;
        public const int T_LE = 19;
        public const int T_GE = 20;
        public const int T_BAR = 21;
        public const int T_BANG = 22;
        public const int T_PRIME = 23;
        public const int T_TILDE = 24;
        public const int T_COLON = 25;
        public const int T_DOTS = 26;
        public const int T_DOT = 27;
        public const int T_UNDERSCORE = 28;
        public const int T_SQRT = 29;
        public const int T_SUM = 30;
        public const int T_PROD = 31;
        public const int T_INT = 32;
        public const int T_DDX = 33;

        // ------------------------------------------------------------------ AST nodes
        public const int N_NUM = 1;
        public const int N_IDENT = 2;
        public const int N_BIN = 3;
        public const int N_NEG = 4;
        public const int N_FACT = 5;
        public const int N_BUILTIN = 6;
        public const int N_CALL = 7;
        public const int N_TUPLE = 8;
        public const int N_LIST = 9;
        public const int N_RANGE = 10;
        public const int N_INDEX = 11;
        public const int N_REL = 12;
        public const int N_AND = 13;
        public const int N_PIECE = 14;
        public const int N_DERIV = 15;
        public const int N_SUM = 16;
        public const int N_PROD = 17;
        public const int N_INT = 18;
        public const int N_MEMBER = 19;
        public const int N_REG = 20;

        public const int B_ADD = 1;
        public const int B_SUB = 2;
        public const int B_MUL = 3;
        public const int B_DIV = 4;
        public const int B_POW = 5;

        public const int R_NONE = -1;
        public const int R_EQ = 0;
        public const int R_LT = 1;
        public const int R_LE = 2;
        public const int R_GT = 3;
        public const int R_GE = 4;

        // ------------------------------------------------------------------ opcodes
        public const int OP_HDR = 1;
        public const int OP_CONST = 2;
        public const int OP_VAR = 3;
        public const int OP_PARAM = 4;
        public const int OP_LISTVAR = 5;
        public const int OP_LISTLIT = 6;
        public const int OP_RANGE = 7;
        public const int OP_ADD = 8;
        public const int OP_SUB = 9;
        public const int OP_MUL = 10;
        public const int OP_DIV = 11;
        public const int OP_POW = 12;
        public const int OP_NEG = 13;
        public const int OP_FN1 = 14;
        public const int OP_FN2 = 15;
        public const int OP_CMP = 16;
        public const int OP_AND = 17;
        public const int OP_SELECT = 18;
        public const int OP_DERIV = 19;
        public const int OP_SUM = 20;
        public const int OP_PROD = 21;
        public const int OP_INT = 22;
        public const int OP_REDUCE = 23;
        public const int OP_INDEX = 24;

        // ------------------------------------------------------------------ builtin function ids
        public const int F_SIN = 1;
        public const int F_COS = 2;
        public const int F_TAN = 3;
        public const int F_CSC = 4;
        public const int F_SEC = 5;
        public const int F_COT = 6;
        public const int F_ASIN = 7;
        public const int F_ACOS = 8;
        public const int F_ATAN = 9;
        public const int F_ACSC = 10;
        public const int F_ASEC = 11;
        public const int F_ACOT = 12;
        public const int F_SINH = 13;
        public const int F_COSH = 14;
        public const int F_TANH = 15;
        public const int F_CSCH = 16;
        public const int F_SECH = 17;
        public const int F_COTH = 18;
        public const int F_ASINH = 19;
        public const int F_ACOSH = 20;
        public const int F_ATANH = 21;
        public const int F_SQRT = 22;
        public const int F_CBRT = 23;
        public const int F_EXP = 24;
        public const int F_LN = 25;
        public const int F_LOG10 = 26;
        public const int F_ABS = 27;
        public const int F_SIGN = 28;
        public const int F_FLOOR = 29;
        public const int F_CEIL = 30;
        public const int F_ROUND = 31;
        public const int F_RE = 32;
        public const int F_IM = 33;
        public const int F_CONJ = 34;
        public const int F_ARG = 35;
        public const int F_ERF = 36;
        public const int F_FACT = 37;
        public const int F_LAST1 = 37;

        public const int F_MOD = 50;
        public const int F_ATAN2 = 51;
        public const int F_LOGB = 52;
        public const int F_NCR = 53;
        public const int F_NPR = 54;
        public const int F_NTHROOT = 55;
        public const int F_ROUND2 = 56;

        public const int F_TOTAL = 80;
        public const int F_MEAN = 81;
        public const int F_LENGTH = 82;
        public const int F_MIN = 83;
        public const int F_MAX = 84;
        public const int F_STDEV = 85;
        public const int F_VAR = 86;
        public const int F_MEDIAN = 87;
        public const int F_GCD = 88;
        public const int F_LCM = 89;

        // ------------------------------------------------------------------ slot / entry / def kinds
        public const int K_EMPTY = 0;
        public const int K_ERROR = 1;
        public const int K_NOTE = 2;
        public const int K_VALUE = 3;
        public const int K_VAR = 4;
        public const int K_FUNC = 5;
        public const int K_PLOT = 6;
        public const int K_REGRESSION = 7;
        public const int K_TABLE = 8;
        public const int K_POINTDEF = 9;

        public const int E_EXPLICIT_Y = 1;
        public const int E_EXPLICIT_X = 2;
        public const int E_POLAR = 3;
        public const int E_PARAM = 4;
        public const int E_POINTS = 5;
        public const int E_IMPLICIT = 6;
        public const int E_VALUEPOINT = 7;

        public const int DK_SCALAR = 1;
        public const int DK_LIST = 2;
        public const int DK_POINT = 3;
        public const int DK_POINTLIST = 4;
        public const int DK_REG = 5;

        const int DS_NONE = 0;
        const int DS_VISITING = 1;
        const int DS_OK = 2;
        const int DS_ERR = 3;

        public const int FV_X = 1;
        public const int FV_Y = 2;
        public const int FV_T = 4;
        public const int FV_THETA = 8;
        public const int FV_R = 16;

        public const int VAR_X = 0;
        public const int VAR_Y = 1;
        public const int VAR_T = 2;
        public const int VAR_THETA = 3;
        public const int FIRST_BOUND_VAR = 4;

        const int EK_NODE = 1;
        const int EK_SLOT = 2;

        public const char TABLE_MARK = '\u0002';
        public const char TBL_ROW = '\u001D';
        public const char TBL_COL = '\u001F';

        // ------------------------------------------------------------------ tokenizer state
        int[] tkType;
        double[] tkNum;
        string[] tkStr;
        int[] tkPos;
        int tkCount;
        int readEnd;

        string[] kwName;
        int[] kwTok;
        int[] kwId;
        string[] kwStr;
        int kwCount;

        // ------------------------------------------------------------------ parser state
        int tp;
        bool perr;
        string perrMsg;
        int absDepth;
        int intDepth;

        // AST pool
        int[] nType;
        double[] nNum;
        string[] nStr;
        int[] nA;
        int[] nB;
        int[] nC;
        int[] nD;
        int[] nArgStart;
        int[] nArgCount;
        int nodeCount;
        int[] argPool;
        int argCount;
        int[] argStk;
        int argSp;

        // functions
        string[] funcName;
        int[] funcSlot;
        int[] funcBody;
        string[] funcParam; // funcIdx*MAX_FPARAMS + p
        int[] funcParamCount;
        int funcCount;

        // ------------------------------------------------------------------ compile output (read by engine)
        [HideInInspector] public int[] code;
        [HideInInspector] public int codeLen;
        [HideInInspector] public double[] cst; // pairs re,im
        [HideInInspector] public int cstLen;
        [HideInInspector] public int[] progStart;
        [HideInInspector] public int[] progEnd;
        [HideInInspector] public int progCount;
        [HideInInspector] public bool complexMode;
        [HideInInspector] public bool degreeMode;

        [HideInInspector] public int slotCount;
        [HideInInspector] public int[] slotKind;
        [HideInInspector] public string[] slotError;
        [HideInInspector] public string[] slotUndef;
        [HideInInspector] public long[] slotDefMask;
        [HideInInspector] public int[] slotProg;
        [HideInInspector] public int[] slotDef;
        [HideInInspector] public bool[] slotIsSlider;
        [HideInInspector] public int[] slotSliderCst;
        [HideInInspector] public double[] slotSliderValue;
        [HideInInspector] public string[] slotDefName;
        [HideInInspector] public bool[] slotPlottable;
        int[] slotRoot;

        [HideInInspector] public int entryCount;
        [HideInInspector] public int[] eSlot;
        [HideInInspector] public int[] eKind;
        [HideInInspector] public int[] eProgA;
        [HideInInspector] public int[] eProgB;
        [HideInInspector] public int[] eRel;
        [HideInInspector] public int[] eDomLo;
        [HideInInspector] public int[] eDomHi;
        [HideInInspector] public double[] eDomLoDef;
        [HideInInspector] public double[] eDomHiDef;
        [HideInInspector] public bool[] eRegion;

        [HideInInspector] public int defCount;
        [HideInInspector] public string[] defName;
        [HideInInspector] public int[] defSlot;
        [HideInInspector] public int[] defKind;
        [HideInInspector] public int[] defProg;
        [HideInInspector] public int[] defProgB;
        [HideInInspector] public int[] defParam;
        [HideInInspector] public int[] defList;
        [HideInInspector] public int[] defRegParamStart;
        [HideInInspector] public int[] defRegParamCount;
        [HideInInspector] public string[] defRegParamNames;
        [HideInInspector] public int[] evalOrder;
        [HideInInspector] public int evalCount;
        [HideInInspector] public int paramCount;
        [HideInInspector] public int listCount;
        int[] defNode;      // rhs AST (or -1 for table literal columns)
        int[] defNode2;     // regression rhs
        int[] defState;
        long[] defDeps;
        long[] defAllDeps;
        bool[] defIsSliderDef;
        int[] defColumnSlot; // table column index or -1
        int[] defColumnIdx;
        string[] defColumnCells; // joined cells string for literal columns
        int[] defTableRows;

        // regression parameters
        string[] regParamName;
        int[] regParamDef;
        int[] regParamId;
        int regParamCount;

        // compile state
        bool cerr;
        string cerrMsg;
        string cerrUndef;
        int allowFree;
        bool polarImplicit;
        int boundNext;
        int[] bcStack;
        int[] bcElemDepth;
        int bcTop;
        string[] envName;
        int[] envKind;
        int[] envNode;
        int[] envFrame;
        int[] envSlot;
        int envTop;
        int callDepth;

        // scan state
        int scanFree;
        long scanDefs;
        string[] scanUndefNames;
        int scanUndefCount;

        bool initialized;

        // =================================================================== init
        public void Init()
        {
            if (initialized) return;
            initialized = true;
            tkType = new int[MAX_TOK];
            tkNum = new double[MAX_TOK];
            tkStr = new string[MAX_TOK];
            tkPos = new int[MAX_TOK];

            nType = new int[MAX_NODES];
            nNum = new double[MAX_NODES];
            nStr = new string[MAX_NODES];
            nA = new int[MAX_NODES];
            nB = new int[MAX_NODES];
            nC = new int[MAX_NODES];
            nD = new int[MAX_NODES];
            nArgStart = new int[MAX_NODES];
            nArgCount = new int[MAX_NODES];
            argPool = new int[MAX_ARGS];
            argStk = new int[MAX_ARGS];

            funcName = new string[MAX_FUNCS];
            funcSlot = new int[MAX_FUNCS];
            funcBody = new int[MAX_FUNCS];
            funcParam = new string[MAX_FUNCS * MAX_FPARAMS];
            funcParamCount = new int[MAX_FUNCS];

            code = new int[MAX_CODE];
            cst = new double[MAX_CONST * 2];
            progStart = new int[MAX_PROGS];
            progEnd = new int[MAX_PROGS];

            slotKind = new int[MAX_SLOTS];
            slotError = new string[MAX_SLOTS];
            slotUndef = new string[MAX_SLOTS];
            slotDefMask = new long[MAX_SLOTS];
            slotProg = new int[MAX_SLOTS];
            slotDef = new int[MAX_SLOTS];
            slotIsSlider = new bool[MAX_SLOTS];
            slotSliderCst = new int[MAX_SLOTS];
            slotSliderValue = new double[MAX_SLOTS];
            slotDefName = new string[MAX_SLOTS];
            slotPlottable = new bool[MAX_SLOTS];
            slotRoot = new int[MAX_SLOTS];

            eSlot = new int[MAX_ENTRIES];
            eKind = new int[MAX_ENTRIES];
            eProgA = new int[MAX_ENTRIES];
            eProgB = new int[MAX_ENTRIES];
            eRel = new int[MAX_ENTRIES];
            eDomLo = new int[MAX_ENTRIES];
            eDomHi = new int[MAX_ENTRIES];
            eDomLoDef = new double[MAX_ENTRIES];
            eDomHiDef = new double[MAX_ENTRIES];
            eRegion = new bool[MAX_ENTRIES];

            defName = new string[MAX_DEFS];
            defSlot = new int[MAX_DEFS];
            defKind = new int[MAX_DEFS];
            defProg = new int[MAX_DEFS];
            defProgB = new int[MAX_DEFS];
            defParam = new int[MAX_DEFS];
            defList = new int[MAX_DEFS];
            defRegParamStart = new int[MAX_DEFS];
            defRegParamCount = new int[MAX_DEFS];
            defRegParamNames = new string[MAX_DEFS];
            evalOrder = new int[MAX_DEFS];
            defNode = new int[MAX_DEFS];
            defNode2 = new int[MAX_DEFS];
            defState = new int[MAX_DEFS];
            defDeps = new long[MAX_DEFS];
            defAllDeps = new long[MAX_DEFS];
            defIsSliderDef = new bool[MAX_DEFS];
            defColumnSlot = new int[MAX_DEFS];
            defColumnIdx = new int[MAX_DEFS];
            defColumnCells = new string[MAX_DEFS];
            defTableRows = new int[MAX_DEFS];

            regParamName = new string[MAX_PARAMS];
            regParamDef = new int[MAX_PARAMS];
            regParamId = new int[MAX_PARAMS];

            bcStack = new int[MAX_FRAMES];
            bcElemDepth = new int[MAX_FRAMES];
            envName = new string[MAX_ENV];
            envKind = new int[MAX_ENV];
            envNode = new int[MAX_ENV];
            envFrame = new int[MAX_ENV];
            envSlot = new int[MAX_ENV];

            scanUndefNames = new string[MAX_UNDEF];

            InitKeywords();
        }

        void AddKw(string name, int tok, int id, string str)
        {
            kwName[kwCount] = name;
            kwTok[kwCount] = tok;
            kwId[kwCount] = id;
            kwStr[kwCount] = str;
            kwCount++;
        }

        void InitKeywords()
        {
            kwName = new string[128];
            kwTok = new int[128];
            kwId = new int[128];
            kwStr = new string[128];
            kwCount = 0;
            AddKw("sin", T_FUNC, F_SIN, ""); AddKw("cos", T_FUNC, F_COS, ""); AddKw("tan", T_FUNC, F_TAN, "");
            AddKw("csc", T_FUNC, F_CSC, ""); AddKw("sec", T_FUNC, F_SEC, ""); AddKw("cot", T_FUNC, F_COT, "");
            AddKw("arcsin", T_FUNC, F_ASIN, ""); AddKw("arccos", T_FUNC, F_ACOS, ""); AddKw("arctan", T_FUNC, F_ATAN, "");
            AddKw("arccsc", T_FUNC, F_ACSC, ""); AddKw("arcsec", T_FUNC, F_ASEC, ""); AddKw("arccot", T_FUNC, F_ACOT, "");
            AddKw("asin", T_FUNC, F_ASIN, ""); AddKw("acos", T_FUNC, F_ACOS, ""); AddKw("atan", T_FUNC, F_ATAN, "");
            AddKw("sinh", T_FUNC, F_SINH, ""); AddKw("cosh", T_FUNC, F_COSH, ""); AddKw("tanh", T_FUNC, F_TANH, "");
            AddKw("csch", T_FUNC, F_CSCH, ""); AddKw("sech", T_FUNC, F_SECH, ""); AddKw("coth", T_FUNC, F_COTH, "");
            AddKw("arcsinh", T_FUNC, F_ASINH, ""); AddKw("arccosh", T_FUNC, F_ACOSH, ""); AddKw("arctanh", T_FUNC, F_ATANH, "");
            AddKw("arsinh", T_FUNC, F_ASINH, ""); AddKw("arcosh", T_FUNC, F_ACOSH, ""); AddKw("artanh", T_FUNC, F_ATANH, "");
            AddKw("asinh", T_FUNC, F_ASINH, ""); AddKw("acosh", T_FUNC, F_ACOSH, ""); AddKw("atanh", T_FUNC, F_ATANH, "");
            AddKw("sqrt", T_FUNC, F_SQRT, ""); AddKw("cbrt", T_FUNC, F_CBRT, ""); AddKw("nthroot", T_FUNC, F_NTHROOT, "");
            AddKw("exp", T_FUNC, F_EXP, ""); AddKw("ln", T_FUNC, F_LN, ""); AddKw("log", T_FUNC, F_LOG10, "");
            AddKw("abs", T_FUNC, F_ABS, ""); AddKw("sign", T_FUNC, F_SIGN, ""); AddKw("sgn", T_FUNC, F_SIGN, "");
            AddKw("floor", T_FUNC, F_FLOOR, ""); AddKw("ceil", T_FUNC, F_CEIL, ""); AddKw("round", T_FUNC, F_ROUND, "");
            AddKw("mod", T_FUNC, F_MOD, ""); AddKw("nCr", T_FUNC, F_NCR, ""); AddKw("nPr", T_FUNC, F_NPR, "");
            AddKw("gcd", T_FUNC, F_GCD, ""); AddKw("lcm", T_FUNC, F_LCM, "");
            AddKw("min", T_FUNC, F_MIN, ""); AddKw("max", T_FUNC, F_MAX, "");
            AddKw("total", T_FUNC, F_TOTAL, ""); AddKw("mean", T_FUNC, F_MEAN, ""); AddKw("median", T_FUNC, F_MEDIAN, "");
            AddKw("length", T_FUNC, F_LENGTH, ""); AddKw("stdev", T_FUNC, F_STDEV, ""); AddKw("stddev", T_FUNC, F_STDEV, "");
            AddKw("var", T_FUNC, F_VAR, "");
            AddKw("real", T_FUNC, F_RE, ""); AddKw("imag", T_FUNC, F_IM, ""); AddKw("conj", T_FUNC, F_CONJ, "");
            AddKw("arg", T_FUNC, F_ARG, ""); AddKw("erf", T_FUNC, F_ERF, "");
            AddKw("sum", T_SUM, 0, ""); AddKw("prod", T_PROD, 0, ""); AddKw("int", T_INT, 0, "");
            AddKw("infinity", T_IDENT, 0, "∞");
            AddKw("pi", T_IDENT, 0, "π"); AddKw("tau", T_IDENT, 0, "τ");
            AddKw("theta", T_IDENT, 0, "θ"); AddKw("alpha", T_IDENT, 0, "α"); AddKw("beta", T_IDENT, 0, "β");
            AddKw("gamma", T_IDENT, 0, "γ"); AddKw("delta", T_IDENT, 0, "δ"); AddKw("epsilon", T_IDENT, 0, "ε");
            AddKw("lambda", T_IDENT, 0, "λ"); AddKw("sigma", T_IDENT, 0, "σ"); AddKw("phi", T_IDENT, 0, "φ");
            AddKw("omega", T_IDENT, 0, "ω"); AddKw("rho", T_IDENT, 0, "ρ");
        }

        // =================================================================== helpers
        bool IsAsciiLetter(char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'); }
        bool IsDigitC(char c) { return c >= '0' && c <= '9'; }
        bool IsGreek(char c)
        {
            if (c >= 'α' && c <= 'ω') return c != 'ς';
            if (c >= 'Α' && c <= 'Ω') return c != 'Σ' && c != 'Π' && c != '΢';
            return false;
        }
        bool IsLetterC(char c) { return IsAsciiLetter(c) || IsGreek(c); }

        bool MatchAt(string s, int i, string kw)
        {
            int L = kw.Length;
            if (i + L > s.Length) return false;
            for (int k = 0; k < L; k++) if (s[i + k] != kw[k]) return false;
            return true;
        }

        double Pow10(int e)
        {
            double r = 1.0;
            for (int i = 0; i < e; i++) r *= 10.0;
            return r;
        }

        // =================================================================== tokenizer
        void AddTok(int type, double num, string str, int pos)
        {
            if (tkCount >= MAX_TOK - 1) { PErr("Expression too long"); return; }
            tkType[tkCount] = type;
            tkNum[tkCount] = num;
            tkStr[tkCount] = str;
            tkPos[tkCount] = pos;
            tkCount++;
        }

        // Reads a variable name (single letter / greek / greek keyword, optional subscript) starting at j.
        string ReadVarName(string s, int j)
        {
            int n = s.Length;
            readEnd = j;
            if (j >= n) return "";
            string nm = "";
            // greek keyword (e.g. "theta")
            int best = -1; int bestLen = 0;
            for (int k = 0; k < kwCount; k++)
            {
                if (kwTok[k] != T_IDENT) continue;
                int L = kwName[k].Length;
                if (L > bestLen && MatchAt(s, j, kwName[k])) { best = k; bestLen = L; }
            }
            if (best >= 0) { nm = kwStr[best]; j += bestLen; }
            else
            {
                char c = s[j];
                if (!IsLetterC(c) && c != 'π' && c != 'τ') return "";
                nm = c.ToString();
                j++;
            }
            if (j < n && s[j] == '_')
            {
                int k2 = j + 1;
                string sub = "";
                if (k2 < n && s[k2] == '{')
                {
                    k2++;
                    while (k2 < n && s[k2] != '}') { char d = s[k2]; if (IsAsciiLetter(d) || IsDigitC(d)) sub += d.ToString(); k2++; }
                    if (k2 < n) k2++;
                }
                else
                {
                    while (k2 < n && (IsAsciiLetter(s[k2]) || IsDigitC(s[k2]))) { sub += s[k2].ToString(); k2++; }
                }
                if (sub.Length > 0) { nm = nm + "_" + sub; j = k2; }
            }
            readEnd = j;
            return nm;
        }

        void Tokenize(string s)
        {
            tkCount = 0;
            int n = s.Length;
            int i = 0;
            while (i < n && !perr)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == ' ') { i++; continue; }
                int st = i;
                if (IsDigitC(c) || (c == '.' && i + 1 < n && IsDigitC(s[i + 1])))
                {
                    long mant = 0; int dec = 0; int digits = 0; int extra = 0; bool seenDot = false;
                    while (i < n)
                    {
                        char d = s[i];
                        if (IsDigitC(d))
                        {
                            if (digits < 17)
                            {
                                mant = mant * 10 + (d - '0');
                                if (mant != 0) digits++;
                                if (seenDot) dec++;
                            }
                            else if (!seenDot) extra++;
                            i++;
                        }
                        else if (d == '.' && !seenDot && !(i + 1 < n && s[i + 1] == '.')) { seenDot = true; i++; }
                        else break;
                    }
                    double v = mant;
                    if (extra > 0) v *= Pow10(extra);
                    if (dec > 0) v /= Pow10(dec);
                    AddTok(T_NUM, v, "", st);
                    continue;
                }
                // special unicode first
                if (c == 'π') { AddTok(T_IDENT, 0, "π", st); i++; continue; }
                if (c == 'τ') { AddTok(T_IDENT, 0, "τ", st); i++; continue; }
                if (c == '∞') { AddTok(T_IDENT, 0, "∞", st); i++; continue; }
                if (IsLetterC(c))
                {
                    if (c == 'd' && i + 3 < n && s[i + 1] == '/' && s[i + 2] == 'd')
                    {
                        string vn = ReadVarName(s, i + 3);
                        if (vn != "") { AddTok(T_DDX, 0, vn, st); i = readEnd; continue; }
                    }
                    if (IsAsciiLetter(c))
                    {
                        int best = -1; int bestLen = 0;
                        for (int k = 0; k < kwCount; k++)
                        {
                            int L = kwName[k].Length;
                            if (L > bestLen && MatchAt(s, i, kwName[k])) { best = k; bestLen = L; }
                        }
                        if (best >= 0)
                        {
                            int tt = kwTok[best];
                            if (tt == T_IDENT)
                            {
                                string g = ReadVarName(s, i); // handles subscripts on greek words
                                AddTok(T_IDENT, 0, g, st); i = readEnd; continue;
                            }
                            AddTok(tt, kwId[best], kwName[best], st);
                            i += bestLen;
                            continue;
                        }
                    }
                    string nm = ReadVarName(s, i);
                    if (nm == "") { PErr("Unexpected '" + c.ToString() + "'"); return; }
                    AddTok(T_IDENT, 0, nm, st);
                    i = readEnd;
                    continue;
                }
                i++;
                if (c == '+') AddTok(T_PLUS, 0, "", st);
                else if (c == '-' || c == '−') AddTok(T_MINUS, 0, "", st);
                else if (c == '*' || c == '·' || c == '×' || c == '⋅') AddTok(T_STAR, 0, "", st);
                else if (c == '/' || c == '÷') AddTok(T_SLASH, 0, "", st);
                else if (c == '^') AddTok(T_CARET, 0, "", st);
                else if (c == '²') { AddTok(T_CARET, 0, "", st); AddTok(T_NUM, 2, "", st); }
                else if (c == '³') { AddTok(T_CARET, 0, "", st); AddTok(T_NUM, 3, "", st); }
                else if (c == '(') AddTok(T_LPAREN, 0, "", st);
                else if (c == ')') AddTok(T_RPAREN, 0, "", st);
                else if (c == '[') AddTok(T_LBRACK, 0, "", st);
                else if (c == ']') AddTok(T_RBRACK, 0, "", st);
                else if (c == '{') AddTok(T_LBRACE, 0, "", st);
                else if (c == '}') AddTok(T_RBRACE, 0, "", st);
                else if (c == ',') AddTok(T_COMMA, 0, "", st);
                else if (c == '=') { if (i < n && s[i] == '=') i++; AddTok(T_EQ, 0, "", st); }
                else if (c == '<') { if (i < n && s[i] == '=') { i++; AddTok(T_LE, 0, "", st); } else AddTok(T_LT, 0, "", st); }
                else if (c == '>') { if (i < n && s[i] == '=') { i++; AddTok(T_GE, 0, "", st); } else AddTok(T_GT, 0, "", st); }
                else if (c == '≤') AddTok(T_LE, 0, "", st);
                else if (c == '≥') AddTok(T_GE, 0, "", st);
                else if (c == '|') AddTok(T_BAR, 0, "", st);
                else if (c == '!') AddTok(T_BANG, 0, "", st);
                else if (c == '\'' || c == '′') AddTok(T_PRIME, 0, "", st);
                else if (c == '~') AddTok(T_TILDE, 0, "", st);
                else if (c == ':') AddTok(T_COLON, 0, "", st);
                else if (c == '…') AddTok(T_DOTS, 0, "", st);
                else if (c == '.')
                {
                    if (i + 1 < n && s[i] == '.' && s[i + 1] == '.') { i += 2; AddTok(T_DOTS, 0, "", st); }
                    else AddTok(T_DOT, 0, "", st);
                }
                else if (c == '_') AddTok(T_UNDERSCORE, 0, "", st);
                else if (c == '√') AddTok(T_SQRT, F_SQRT, "", st);
                else if (c == '∛') AddTok(T_FUNC, F_CBRT, "cbrt", st);
                else if (c == '∫') AddTok(T_INT, 0, "", st);
                else if (c == 'Σ' || c == '∑') AddTok(T_SUM, 0, "", st);
                else if (c == 'Π' || c == '∏') AddTok(T_PROD, 0, "", st);
                else { PErr("Unexpected '" + c.ToString() + "'"); return; }
            }
            AddTok(T_END, 0, "", n);
        }

        // =================================================================== parser
        void PErr(string m) { if (!perr) { perr = true; perrMsg = m; } }

        int NewNode(int type, int a, int b, int c, double num, string str)
        {
            if (nodeCount >= MAX_NODES) { PErr("Too many expressions"); return 0; }
            int id = nodeCount++;
            nType[id] = type; nA[id] = a; nB[id] = b; nC[id] = c; nD[id] = -1;
            nNum[id] = num; nStr[id] = str; nArgStart[id] = 0; nArgCount[id] = 0;
            return id;
        }

        void PushArg(int node)
        {
            if (argSp >= MAX_ARGS) { PErr("Too many arguments"); return; }
            argStk[argSp++] = node;
        }

        // moves args from argStk[mark..argSp) into the pool for node
        void TakeArgs(int node, int mark)
        {
            int cnt = argSp - mark;
            if (argCount + cnt > MAX_ARGS) { PErr("Too many arguments"); argSp = mark; return; }
            nArgStart[node] = argCount;
            nArgCount[node] = cnt;
            for (int i = 0; i < cnt; i++) argPool[argCount + i] = argStk[mark + i];
            argCount += cnt;
            argSp = mark;
        }

        int Arg(int node, int i) { return argPool[nArgStart[node] + i]; }

        bool Expect(int t, string what)
        {
            if (tkType[tp] != t) { PErr("Expected " + what); return false; }
            tp++;
            return true;
        }

        bool IsRelTok(int t) { return t == T_EQ || t == T_LT || t == T_GT || t == T_LE || t == T_GE; }
        int RelFromTok(int t)
        {
            if (t == T_LT) return R_LT;
            if (t == T_LE) return R_LE;
            if (t == T_GT) return R_GT;
            if (t == T_GE) return R_GE;
            return R_EQ;
        }

        bool IsUserFunc(string name)
        {
            for (int i = 0; i < funcCount; i++) if (funcName[i] == name) return true;
            return false;
        }

        int ParseTop()
        {
            tp = 0; absDepth = 0; intDepth = 0;
            int e = ParseRelation();
            if (perr) return -1;
            if (tkType[tp] != T_END)
            {
                int t = tkType[tp];
                if (t == T_RPAREN) PErr("Unmatched ')'");
                else if (t == T_RBRACK) PErr("Unmatched ']'");
                else if (t == T_RBRACE) PErr("Unmatched '}'");
                else if (t == T_COMMA) PErr("Unexpected ','");
                else PErr("Unexpected symbol");
                return -1;
            }
            return e;
        }

        [RecursiveMethod]
        int ParseRelation()
        {
            int left = ParseExpr();
            if (perr) return -1;
            int t = tkType[tp];
            if (t == T_TILDE)
            {
                tp++;
                int right = ParseExpr();
                if (perr) return -1;
                return NewNode(N_REG, 0, left, right, 0, "");
            }
            if (!IsRelTok(t)) return left;
            int result = -1;
            while (IsRelTok(tkType[tp]))
            {
                int op = RelFromTok(tkType[tp]);
                tp++;
                int right = ParseExpr();
                if (perr) return -1;
                int rel = NewNode(N_REL, op, left, right, 0, "");
                result = result < 0 ? rel : NewNode(N_AND, 0, result, rel, 0, "");
                left = right;
            }
            return result;
        }

        [RecursiveMethod]
        int ParseExpr()
        {
            int left = ParseTerm();
            if (perr) return -1;
            while (true)
            {
                int t = tkType[tp];
                if (t != T_PLUS && t != T_MINUS) break;
                tp++;
                int right = ParseTerm();
                if (perr) return -1;
                left = NewNode(N_BIN, t == T_PLUS ? B_ADD : B_SUB, left, right, 0, "");
            }
            return left;
        }

        bool IsTerminatorTok(int t)
        {
            return t == T_END || t == T_RPAREN || t == T_RBRACK || t == T_RBRACE || t == T_COMMA ||
                   t == T_PLUS || t == T_MINUS || IsRelTok(t) || t == T_COLON || t == T_BAR || t == T_TILDE;
        }

        bool IsDifferentialHere()
        {
            if (intDepth <= 0) return false;
            if (tkType[tp] != T_IDENT || tkStr[tp] != "d") return false;
            if (tkType[tp + 1] != T_IDENT) return false;
            return IsTerminatorTok(tkType[tp + 2]);
        }

        bool StartsImplicit(int t)
        {
            if (t == T_BAR) return absDepth == 0;
            return t == T_NUM || t == T_IDENT || t == T_FUNC || t == T_LPAREN || t == T_LBRACK ||
                   t == T_LBRACE || t == T_SQRT || t == T_SUM || t == T_PROD || t == T_INT || t == T_DDX;
        }

        [RecursiveMethod]
        int ParseTerm()
        {
            int left = ParseUnary();
            if (perr) return -1;
            while (true)
            {
                int t = tkType[tp];
                if (t == T_STAR || t == T_SLASH)
                {
                    tp++;
                    int right = ParseUnary();
                    if (perr) return -1;
                    left = NewNode(N_BIN, t == T_STAR ? B_MUL : B_DIV, left, right, 0, "");
                }
                else if (StartsImplicit(t))
                {
                    if (IsDifferentialHere()) break;
                    int right = ParsePower();
                    if (perr) return -1;
                    left = NewNode(N_BIN, B_MUL, left, right, 0, "");
                }
                else break;
            }
            return left;
        }

        [RecursiveMethod]
        int ParseUnary()
        {
            int t = tkType[tp];
            if (t == T_MINUS)
            {
                tp++;
                int c = ParseUnary();
                if (perr) return -1;
                if (nType[c] == N_NUM) { nNum[c] = -nNum[c]; return c; }
                return NewNode(N_NEG, 0, c, -1, 0, "");
            }
            if (t == T_PLUS) { tp++; return ParseUnary(); }
            return ParsePower();
        }

        [RecursiveMethod]
        int ParsePower()
        {
            int b = ParsePostfix();
            if (perr) return -1;
            if (tkType[tp] == T_CARET)
            {
                tp++;
                int e = ParseUnary();
                if (perr) return -1;
                return NewNode(N_BIN, B_POW, b, e, 0, "");
            }
            return b;
        }

        [RecursiveMethod]
        int ParsePostfix()
        {
            int p = ParsePrimary();
            if (perr) return -1;
            while (true)
            {
                int t = tkType[tp];
                if (t == T_BANG) { tp++; p = NewNode(N_FACT, 0, p, -1, 0, ""); }
                else if (t == T_LBRACK)
                {
                    int pt = nType[p];
                    if (pt != N_IDENT && pt != N_LIST && pt != N_RANGE && pt != N_INDEX && pt != N_CALL) break;
                    tp++;
                    int idx = ParseExpr();
                    if (perr) return -1;
                    if (!Expect(T_RBRACK, "']'")) return -1;
                    p = NewNode(N_INDEX, 0, p, idx, 0, "");
                }
                else if (t == T_DOT && tkType[tp + 1] == T_IDENT && (tkStr[tp + 1] == "x" || tkStr[tp + 1] == "y"))
                {
                    int comp = tkStr[tp + 1] == "x" ? 0 : 1;
                    tp += 2;
                    p = NewNode(N_MEMBER, comp, p, -1, 0, "");
                }
                else break;
            }
            return p;
        }

        // argument for _ and ^ positions: {expr}, (expr), -primary or a single primary
        [RecursiveMethod]
        int ParseScriptArg()
        {
            int t = tkType[tp];
            if (t == T_LBRACE)
            {
                tp++;
                int e = ParseExpr();
                if (perr) return -1;
                if (!Expect(T_RBRACE, "'}'")) return -1;
                return e;
            }
            if (t == T_LPAREN)
            {
                tp++;
                int e = ParseExpr();
                if (perr) return -1;
                if (!Expect(T_RPAREN, "')'")) return -1;
                return e;
            }
            if (t == T_MINUS)
            {
                tp++;
                int c = ParseScriptArg();
                if (perr) return -1;
                if (nType[c] == N_NUM) { nNum[c] = -nNum[c]; return c; }
                return NewNode(N_NEG, 0, c, -1, 0, "");
            }
            if (t == T_NUM) { tp++; return NewNode(N_NUM, 0, -1, -1, tkNum[tp - 1], ""); }
            if (t == T_IDENT) { tp++; return NewNode(N_IDENT, 0, -1, -1, 0, tkStr[tp - 1]); }
            PErr("Expected a value");
            return -1;
        }

        int InverseTrig(int fid)
        {
            if (fid == F_SIN) return F_ASIN;
            if (fid == F_COS) return F_ACOS;
            if (fid == F_TAN) return F_ATAN;
            if (fid == F_CSC) return F_ACSC;
            if (fid == F_SEC) return F_ASEC;
            if (fid == F_COT) return F_ACOT;
            if (fid == F_SINH) return F_ASINH;
            if (fid == F_COSH) return F_ACOSH;
            if (fid == F_TANH) return F_ATANH;
            return -1;
        }

        bool AllowsBareArg(int fid)
        {
            return (fid >= F_SIN && fid <= F_ATANH) || fid == F_LN || fid == F_LOG10 || fid == F_EXP || fid == F_SQRT;
        }

        // function argument without parentheses: "sin 2x"
        [RecursiveMethod]
        int ParseBareArg()
        {
            int a = ParsePower();
            if (perr) return -1;
            while (tkType[tp] == T_NUM || tkType[tp] == T_IDENT)
            {
                if (IsDifferentialHere()) break;
                int b = ParsePower();
                if (perr) return -1;
                a = NewNode(N_BIN, B_MUL, a, b, 0, "");
            }
            return a;
        }

        [RecursiveMethod]
        int ParseCallArgs(int node)
        {
            // assumes current token is '('
            tp++;
            int mark = argSp;
            if (tkType[tp] != T_RPAREN)
            {
                while (true)
                {
                    int a = ParseRelation();
                    if (perr) { argSp = mark; return -1; }
                    PushArg(a);
                    if (tkType[tp] == T_COMMA) { tp++; continue; }
                    break;
                }
            }
            if (!Expect(T_RPAREN, "')'")) { argSp = mark; return -1; }
            TakeArgs(node, mark);
            return node;
        }

        [RecursiveMethod]
        int ParsePrimary()
        {
            int t = tkType[tp];
            if (t == T_NUM) { tp++; return NewNode(N_NUM, 0, -1, -1, tkNum[tp - 1], ""); }
            if (t == T_IDENT)
            {
                string name = tkStr[tp];
                tp++;
                int primes = 0;
                while (tkType[tp] == T_PRIME) { primes++; tp++; }
                if (IsUserFunc(name) && tkType[tp] == T_LPAREN)
                {
                    int call = NewNode(N_CALL, 0, -1, -1, 0, name);
                    nD[call] = primes;
                    ParseCallArgs(call);
                    if (perr) return -1;
                    return call;
                }
                if (primes > 0) { PErr("' can only follow a function name"); return -1; }
                return NewNode(N_IDENT, 0, -1, -1, 0, name);
            }
            if (t == T_FUNC || t == T_SQRT)
            {
                int fid = (int)tkNum[tp];
                tp++;
                int baseNode = -1;
                int powNode = -1;
                if (fid == F_LOG10 && tkType[tp] == T_UNDERSCORE)
                {
                    tp++;
                    baseNode = ParseScriptArg();
                    if (perr) return -1;
                }
                if (tkType[tp] == T_CARET && t == T_FUNC)
                {
                    tp++;
                    powNode = ParseScriptArg();
                    if (perr) return -1;
                    if (nType[powNode] == N_NUM && nNum[powNode] == -1.0)
                    {
                        int inv = InverseTrig(fid);
                        if (inv > 0) { fid = inv; powNode = -1; }
                    }
                }
                int node = NewNode(N_BUILTIN, fid, baseNode, powNode, 0, "");
                if (tkType[tp] == T_LPAREN)
                {
                    ParseCallArgs(node);
                    if (perr) return -1;
                }
                else
                {
                    if (!AllowsBareArg(fid)) { PErr("Expected '(' after function"); return -1; }
                    int mark = argSp;
                    int a = ParseBareArg();
                    if (perr) { argSp = mark; return -1; }
                    PushArg(a);
                    TakeArgs(node, mark);
                }
                return node;
            }
            if (t == T_LPAREN)
            {
                tp++;
                int first = ParseRelation();
                if (perr) return -1;
                if (tkType[tp] == T_COMMA)
                {
                    int mark = argSp;
                    PushArg(first);
                    while (tkType[tp] == T_COMMA)
                    {
                        tp++;
                        int e = ParseExpr();
                        if (perr) { argSp = mark; return -1; }
                        PushArg(e);
                    }
                    if (!Expect(T_RPAREN, "')'")) { argSp = mark; return -1; }
                    int tup = NewNode(N_TUPLE, 0, -1, -1, 0, "");
                    TakeArgs(tup, mark);
                    return tup;
                }
                if (!Expect(T_RPAREN, "')'")) return -1;
                return first;
            }
            if (t == T_LBRACK) return ParseList();
            if (t == T_LBRACE) return ParsePiecewise();
            if (t == T_BAR)
            {
                tp++;
                absDepth++;
                int e = ParseExpr();
                absDepth--;
                if (perr) return -1;
                if (!Expect(T_BAR, "closing '|'")) return -1;
                int node = NewNode(N_BUILTIN, F_ABS, -1, -1, 0, "");
                int mark = argSp;
                PushArg(e);
                TakeArgs(node, mark);
                return node;
            }
            if (t == T_SUM || t == T_PROD) return ParseSumProd(t == T_SUM ? N_SUM : N_PROD);
            if (t == T_INT) return ParseIntegral();
            if (t == T_DDX)
            {
                string v = tkStr[tp];
                tp++;
                int body = ParseTerm();
                if (perr) return -1;
                return NewNode(N_DERIV, 0, body, -1, 0, v);
            }
            if (t == T_END) PErr("Incomplete expression");
            else if (t == T_RPAREN) PErr("Unexpected ')'");
            else if (IsRelTok(t)) PErr("Missing value before comparison");
            else PErr("Unexpected symbol");
            return -1;
        }

        [RecursiveMethod]
        int ParseList()
        {
            tp++; // [
            int mark = argSp;
            if (tkType[tp] == T_RBRACK)
            {
                tp++;
                int empty = NewNode(N_LIST, 0, -1, -1, 0, "");
                TakeArgs(empty, mark);
                return empty;
            }
            bool isRange = false;
            while (true)
            {
                if (tkType[tp] == T_DOTS)
                {
                    tp++;
                    if (tkType[tp] == T_COMMA) tp++;
                    isRange = true;
                    int last = ParseExpr();
                    if (perr) { argSp = mark; return -1; }
                    PushArg(last);
                    break;
                }
                int e = ParseExpr();
                if (perr) { argSp = mark; return -1; }
                PushArg(e);
                if (tkType[tp] == T_COMMA) { tp++; continue; }
                if (tkType[tp] == T_DOTS) continue;
                break;
            }
            if (!Expect(T_RBRACK, "']'")) { argSp = mark; return -1; }
            int cnt = argSp - mark;
            if (isRange)
            {
                if (cnt < 2 || cnt > 3) { argSp = mark; PErr("Ranges look like [1...10] or [1,3,...,11]"); return -1; }
                int r = NewNode(N_RANGE, cnt, -1, -1, 0, "");
                TakeArgs(r, mark);
                return r;
            }
            int l = NewNode(N_LIST, 0, -1, -1, 0, "");
            TakeArgs(l, mark);
            return l;
        }

        bool IsConditionNode(int n) { return nType[n] == N_REL || nType[n] == N_AND; }

        [RecursiveMethod]
        int ParsePiecewise()
        {
            tp++; // {
            int mark = argSp;
            int pairs = 0;
            bool hasDefault = false;
            if (tkType[tp] == T_RBRACE) { PErr("Empty { }"); return -1; }
            while (true)
            {
                int cond = ParseRelation();
                if (perr) { argSp = mark; return -1; }
                if (tkType[tp] == T_COLON)
                {
                    tp++;
                    int val = ParseExpr();
                    if (perr) { argSp = mark; return -1; }
                    PushArg(cond); PushArg(val); pairs++;
                }
                else if (IsConditionNode(cond))
                {
                    PushArg(cond); PushArg(NewNode(N_NUM, 0, -1, -1, 1.0, "")); pairs++;
                }
                else
                {
                    PushArg(cond);
                    hasDefault = true;
                    if (tkType[tp] != T_RBRACE) { argSp = mark; PErr("The default value must be last"); return -1; }
                    break;
                }
                if (tkType[tp] == T_COMMA) { tp++; continue; }
                break;
            }
            if (!Expect(T_RBRACE, "'}'")) { argSp = mark; return -1; }
            if (pairs == 0)
            {
                // {expr} without a condition is plain grouping (as in e^{-x})
                int grouped = argStk[mark];
                argSp = mark;
                return grouped;
            }
            int node = NewNode(N_PIECE, pairs, hasDefault ? 1 : 0, -1, 0, "");
            TakeArgs(node, mark);
            return node;
        }

        [RecursiveMethod]
        int ParseSumProd(int nodeType)
        {
            tp++;
            string v; int lo; int hi; int body;
            if (tkType[tp] == T_LPAREN)
            {
                tp++;
                if (tkType[tp] != T_IDENT) { PErr("Expected index variable"); return -1; }
                v = tkStr[tp]; tp++;
                if (!Expect(T_EQ, "'='")) return -1;
                lo = ParseExpr(); if (perr) return -1;
                if (!Expect(T_COMMA, "','")) return -1;
                hi = ParseExpr(); if (perr) return -1;
                if (!Expect(T_COMMA, "','")) return -1;
                body = ParseExpr(); if (perr) return -1;
                if (!Expect(T_RPAREN, "')'")) return -1;
            }
            else
            {
                if (!Expect(T_UNDERSCORE, "'_' after sum")) return -1;
                int close = T_RBRACE;
                if (tkType[tp] == T_LPAREN) close = T_RPAREN;
                else if (tkType[tp] != T_LBRACE) { PErr("Expected _{n=...}"); return -1; }
                tp++;
                if (tkType[tp] != T_IDENT) { PErr("Expected index variable"); return -1; }
                v = tkStr[tp]; tp++;
                if (!Expect(T_EQ, "'='")) return -1;
                lo = ParseExpr(); if (perr) return -1;
                if (!Expect(close, "closing bracket")) return -1;
                if (!Expect(T_CARET, "'^' upper limit")) return -1;
                hi = ParseScriptArg(); if (perr) return -1;
                body = ParseTerm(); if (perr) return -1;
            }
            int node = NewNode(nodeType, 0, lo, hi, 0, v);
            nD[node] = body;
            return node;
        }

        [RecursiveMethod]
        int ParseIntegral()
        {
            tp++;
            if (!Expect(T_UNDERSCORE, "'_' lower limit")) return -1;
            int lo = ParseScriptArg(); if (perr) return -1;
            if (!Expect(T_CARET, "'^' upper limit")) return -1;
            int hi = ParseScriptArg(); if (perr) return -1;
            intDepth++;
            int body = ParseExpr();
            intDepth--;
            if (perr) return -1;
            if (tkType[tp] != T_IDENT || tkStr[tp] != "d" || tkType[tp + 1] != T_IDENT) { PErr("Integral needs dx (or dt...) at the end"); return -1; }
            string v = tkStr[tp + 1];
            tp += 2;
            int node = NewNode(N_INT, 0, lo, hi, 0, v);
            nD[node] = body;
            return node;
        }

        // =================================================================== constant folding (table cells)
        [RecursiveMethod]
        double FoldConst(int n)
        {
            int t = nType[n];
            if (t == N_NUM) return nNum[n];
            if (t == N_NEG) return -FoldConst(nB[n]);
            if (t == N_IDENT)
            {
                string s = nStr[n];
                if (s == "π") return System.Math.PI;
                if (s == "e") return System.Math.E;
                if (s == "τ") return 2.0 * System.Math.PI;
                return double.NaN;
            }
            if (t == N_BIN)
            {
                double a = FoldConst(nB[n]);
                double b = FoldConst(nC[n]);
                int op = nA[n];
                if (op == B_ADD) return a + b;
                if (op == B_SUB) return a - b;
                if (op == B_MUL) return a * b;
                if (op == B_DIV) return a / b;
                if (op == B_POW) return System.Math.Pow(a, b);
            }
            if (t == N_BUILTIN && nArgCount[n] == 1 && nA[n] == F_SQRT) return System.Math.Sqrt(FoldConst(Arg(n, 0)));
            return double.NaN;
        }

        // =================================================================== build entry point
        void ResetAll()
        {
            nodeCount = 0; argCount = 0; argSp = 0;
            funcCount = 0;
            codeLen = 0; cstLen = 0; progCount = 0;
            entryCount = 0; defCount = 0; evalCount = 0;
            paramCount = 0; listCount = 0; regParamCount = 0;
            envTop = 0; bcTop = 0; callDepth = 0;
            for (int s = 0; s < MAX_SLOTS; s++)
            {
                slotKind[s] = K_EMPTY;
                slotError[s] = "";
                slotUndef[s] = "";
                slotDefMask[s] = 0;
                slotProg[s] = -1;
                slotDef[s] = -1;
                slotIsSlider[s] = false;
                slotSliderCst[s] = -1;
                slotSliderValue[s] = 0;
                slotDefName[s] = "";
                slotPlottable[s] = false;
                slotRoot[s] = -1;
            }
        }

        void SlotErr(int s, string msg)
        {
            slotKind[s] = K_ERROR;
            slotError[s] = msg;
        }

        /// <summary>Compile all expressions. texts[0..count).</summary>
        public void Build(string[] texts, int count, bool complex, bool degrees)
        {
            Init();
            ResetAll();
            complexMode = complex;
            degreeMode = degrees;
            slotCount = count > MAX_SLOTS ? MAX_SLOTS : count;

            // ---- pass 1: function name prepass
            for (int s = 0; s < slotCount; s++)
            {
                string txt = texts[s];
                if (txt == null || txt.Length == 0) continue;
                if (txt[0] == TABLE_MARK || txt[0] == '"') continue;
                perr = false;
                Tokenize(txt);
                if (perr) continue;
                if (tkCount >= 5 && tkType[0] == T_IDENT && tkType[1] == T_LPAREN && tkType[2] == T_IDENT)
                {
                    int j = 3;
                    bool ok = true;
                    while (tkType[j] == T_COMMA && tkType[j + 1] == T_IDENT) j += 2;
                    if (tkType[j] != T_RPAREN || tkType[j + 1] != T_EQ) ok = false;
                    string nm = tkStr[0];
                    if (ok && nm != "x" && nm != "y" && nm != "r" && nm != "e" && nm != "π" && nm != "i" && funcCount < MAX_FUNCS && !IsUserFunc(nm))
                    {
                        funcName[funcCount] = nm;
                        funcSlot[funcCount] = s;
                        funcBody[funcCount] = -1;
                        funcParamCount[funcCount] = 0;
                        funcCount++;
                    }
                }
            }

            // ---- pass 2: parse
            for (int s = 0; s < slotCount; s++)
            {
                string txt = texts[s];
                if (txt == null) txt = "";
                bool blank = true;
                for (int k = 0; k < txt.Length; k++) if (txt[k] != ' ') { blank = false; break; }
                if (blank) { slotKind[s] = K_EMPTY; continue; }
                if (txt[0] == '"') { slotKind[s] = K_NOTE; continue; }
                if (txt[0] == TABLE_MARK) { slotKind[s] = K_TABLE; continue; }
                perr = false; perrMsg = "";
                Tokenize(txt);
                int root = -1;
                if (!perr) root = ParseTop();
                if (perr) { SlotErr(s, perrMsg); continue; }
                slotRoot[s] = root;
                slotKind[s] = K_PLOT; // provisional
            }

            // ---- pass 3: structural classification & name registration
            for (int s = 0; s < slotCount; s++)
            {
                if (slotKind[s] == K_TABLE) { RegisterTable(s, texts[s]); continue; }
                if (slotKind[s] != K_PLOT) continue;
                int root = slotRoot[s];
                int rt = nType[root];
                if (rt == N_REG)
                {
                    slotKind[s] = K_REGRESSION;
                    int d = NewDef("", s);
                    if (d < 0) { SlotErr(s, "Too many definitions"); continue; }
                    defKind[d] = DK_REG;
                    defNode[d] = nB[root];
                    defNode2[d] = nC[root];
                    slotDef[s] = d;
                    continue;
                }
                if (rt == N_REL && nA[root] == R_EQ)
                {
                    int L = nB[root];
                    int R = nC[root];
                    if (nType[L] == N_CALL && IsFuncDefPattern(L))
                    {
                        int fi = FindFunc(nStr[L]);
                        if (fi < 0 || funcSlot[fi] != s) { SlotErr(s, "'" + nStr[L] + "' is defined more than once"); continue; }
                        funcBody[fi] = R;
                        int pc = nArgCount[L];
                        if (pc > MAX_FPARAMS) { SlotErr(s, "Too many parameters"); continue; }
                        funcParamCount[fi] = pc;
                        for (int p = 0; p < pc; p++) funcParam[fi * MAX_FPARAMS + p] = nStr[Arg(L, p)];
                        slotKind[s] = K_FUNC;
                        slotDefName[s] = nStr[L];
                        continue;
                    }
                    if (nType[L] == N_IDENT)
                    {
                        string nm = nStr[L];
                        if (nm != "x" && nm != "y" && nm != "r" && nm != "θ")
                        {
                            if (IsConstName(nm)) { SlotErr(s, "Can't redefine '" + nm + "'"); continue; }
                            if (FindDef(nm) >= 0 || IsUserFunc(nm))
                            {
                                SlotErr(s, "'" + nm + "' is defined more than once");
                                int other = FindDef(nm);
                                if (other >= 0 && defSlot[other] >= 0) SlotErr(defSlot[other], "'" + nm + "' is defined more than once");
                                continue;
                            }
                            int d = NewDef(nm, s);
                            if (d < 0) { SlotErr(s, "Too many definitions"); continue; }
                            defNode[d] = R;
                            slotDef[s] = d;
                            slotDefName[s] = nm;
                            slotKind[s] = K_VAR;
                            if (nType[R] == N_NUM) { defIsSliderDef[d] = true; slotIsSlider[s] = true; slotSliderValue[s] = nNum[R]; }
                            continue;
                        }
                    }
                }
            }
            // remove defs whose slot errored (duplicates)
            for (int d = 0; d < defCount; d++)
            {
                int s = defSlot[d];
                if (s >= 0 && slotKind[s] == K_ERROR) { defState[d] = DS_ERR; defName[d] = "\u0000"; }
            }

            // ---- pass 4: regression parameter discovery
            for (int d = 0; d < defCount; d++)
            {
                if (defKind[d] != DK_REG || defState[d] == DS_ERR) continue;
                BeginScan();
                ScanNode(defNode[d], 0);
                ScanNode(defNode2[d], 0);
                defRegParamStart[d] = paramCount;
                int cnt = 0;
                string names = "";
                for (int u = 0; u < scanUndefCount; u++)
                {
                    string nm = scanUndefNames[u];
                    if (FindRegParam(nm) >= 0) continue;
                    if (paramCount >= MAX_PARAMS || regParamCount >= MAX_PARAMS) break;
                    regParamName[regParamCount] = nm;
                    regParamDef[regParamCount] = d;
                    regParamId[regParamCount] = paramCount;
                    regParamCount++;
                    paramCount++;
                    cnt++;
                    names += (names.Length > 0 ? "," : "") + nm;
                }
                defRegParamCount[d] = cnt;
                defRegParamNames[d] = names;
            }

            // ---- pass 5: dependency scan for defs
            for (int d = 0; d < defCount; d++)
            {
                if (defState[d] == DS_ERR) continue;
                BeginScan();
                if (defNode[d] >= 0) ScanNode(defNode[d], 0);
                if (defKind[d] == DK_REG && defNode2[d] >= 0) ScanNode(defNode2[d], 0);
                defDeps[d] = scanDefs & (-1L ^ (1L << d));
                int s = defSlot[d];
                if (cerr && s >= 0) { SlotErr(s, cerrMsg); defState[d] = DS_ERR; continue; }
                if (defKind[d] != DK_REG && scanFree != 0 && s >= 0 && defColumnSlot[d] < 0)
                {
                    SlotErr(s, FreeVarMessage(scanFree) + " can't be used in a definition");
                    defState[d] = DS_ERR;
                    continue;
                }
                if (defKind[d] != DK_REG && scanUndefCount > 0 && s >= 0)
                {
                    SlotErr(s, "'" + scanUndefNames[0] + "' is not defined");
                    slotUndef[s] = scanUndefNames[0];
                    defState[d] = DS_ERR;
                    continue;
                }
            }

            // ---- pass 6: topological compile of defs
            for (int d = 0; d < defCount; d++) if (defState[d] == DS_NONE) VisitDef(d);

            // ---- pass 7: plots & values
            for (int s = 0; s < slotCount; s++)
            {
                if (slotKind[s] == K_PLOT) CompilePlotSlot(s);
                else if (slotKind[s] == K_REGRESSION && defState[slotDef[s]] == DS_OK) CompileRegressionModel(s);
                else if (slotKind[s] == K_TABLE) CompileTableEntries(s);
                else if (slotKind[s] == K_POINTDEF) CompilePointDefEntry(s);
            }

            // ---- pass 8: dependency masks for dirty tracking
            for (int i = 0; i < evalCount; i++)
            {
                int d = evalOrder[i];
                long all = defDeps[d];
                for (int j = 0; j < defCount; j++) if ((defDeps[d] & (1L << j)) != 0) all |= defAllDeps[j];
                defAllDeps[d] = all;
            }
            for (int s = 0; s < slotCount; s++)
            {
                long m = slotDefMask[s];
                long all = m;
                for (int j = 0; j < defCount; j++) if ((m & (1L << j)) != 0) all |= defAllDeps[j];
                int sd = slotDef[s];
                if (sd >= 0) all |= (1L << sd) | defAllDeps[sd];
                for (int d = 0; d < defCount; d++) if (defSlot[d] == s) all |= (1L << d) | defAllDeps[d];
                slotDefMask[s] = all;
            }
        }

        string FreeVarMessage(int fv)
        {
            if ((fv & FV_X) != 0) return "x";
            if ((fv & FV_Y) != 0) return "y";
            if ((fv & FV_T) != 0) return "t";
            if ((fv & FV_THETA) != 0) return "θ";
            return "r";
        }

        bool IsConstName(string nm)
        {
            return nm == "π" || nm == "e" || nm == "τ" || nm == "∞" || nm == "i";
        }

        bool IsFuncDefPattern(int L)
        {
            int pc = nArgCount[L];
            if (pc == 0 || nD[L] > 0) return false;
            for (int p = 0; p < pc; p++)
            {
                int a = Arg(L, p);
                if (nType[a] != N_IDENT) return false;
                for (int q = 0; q < p; q++) if (nStr[Arg(L, q)] == nStr[a]) return false;
            }
            return true;
        }

        int FindFunc(string nm)
        {
            for (int i = 0; i < funcCount; i++) if (funcName[i] == nm) return i;
            return -1;
        }

        int FindDef(string nm)
        {
            if (nm == null || nm.Length == 0) return -1;
            for (int d = 0; d < defCount; d++) if (defName[d] == nm) return d;
            return -1;
        }

        int FindRegParam(string nm)
        {
            for (int i = 0; i < regParamCount; i++) if (regParamName[i] == nm) return i;
            return -1;
        }

        int NewDef(string name, int slot)
        {
            if (defCount >= MAX_DEFS) return -1;
            int d = defCount++;
            defName[d] = name;
            defSlot[d] = slot;
            defKind[d] = 0;
            defProg[d] = -1;
            defProgB[d] = -1;
            defParam[d] = -1;
            defList[d] = -1;
            defNode[d] = -1;
            defNode2[d] = -1;
            defState[d] = DS_NONE;
            defDeps[d] = 0;
            defAllDeps[d] = 0;
            defIsSliderDef[d] = false;
            defColumnSlot[d] = -1;
            defColumnIdx[d] = -1;
            defColumnCells[d] = "";
            defTableRows[d] = 0;
            defRegParamStart[d] = 0;
            defRegParamCount[d] = 0;
            defRegParamNames[d] = "";
            return d;
        }

        // =================================================================== tables
        int[] tblColDef;     // slot*MAX_TCOLS + c
        int[] tblColCount;
        public const int MAX_TCOLS = 6;

        void RegisterTable(int s, string txt)
        {
            if (tblColDef == null) { tblColDef = new int[MAX_SLOTS * MAX_TCOLS]; tblColCount = new int[MAX_SLOTS]; }
            tblColCount[s] = 0;
            string body = txt.Substring(1);
            string[] rows = body.Split(new char[] { TBL_ROW });
            if (rows.Length == 0) { SlotErr(s, "Empty table"); return; }
            string[] headers = rows[0].Split(new char[] { TBL_COL });
            int cols = headers.Length;
            if (cols > MAX_TCOLS) cols = MAX_TCOLS;
            int nrows = rows.Length - 1;
            // trim trailing empty rows
            while (nrows > 0)
            {
                string r = rows[nrows];
                bool empty = true;
                for (int k = 0; k < r.Length; k++) { char ch = r[k]; if (ch != ' ' && ch != TBL_COL) { empty = false; break; } }
                if (!empty) break;
                nrows--;
            }
            for (int c = 0; c < cols; c++)
            {
                string h = headers[c];
                string nm = "";
                int hnode = -1;
                perr = false;
                Tokenize(h);
                if (!perr && tkCount == 2 && tkType[0] == T_IDENT) nm = tkStr[0];
                else if (!perr && tkCount > 1)
                {
                    hnode = ParseTop();
                    if (perr) { SlotErr(s, "Column " + (c + 1) + ": " + perrMsg); return; }
                }
                if (nm == "x" || nm == "y") nm = "";
                if (nm.Length > 0 && (FindDef(nm) >= 0 || IsUserFunc(nm) || IsConstName(nm)))
                {
                    SlotErr(s, "'" + nm + "' is defined more than once");
                    return;
                }
                int d = NewDef(nm, s);
                if (d < 0) { SlotErr(s, "Too many definitions"); return; }
                defKind[d] = DK_LIST;
                defColumnSlot[d] = s;
                defColumnIdx[d] = c;
                defNode[d] = hnode;
                defTableRows[d] = nrows;
                string cells = "";
                for (int r = 1; r <= nrows; r++)
                {
                    string[] cs = rows[r].Split(new char[] { TBL_COL });
                    string cell = c < cs.Length ? cs[c] : "";
                    cells += (r > 1 ? TBL_ROW.ToString() : "") + cell;
                }
                defColumnCells[d] = cells;
                tblColDef[s * MAX_TCOLS + c] = d;
                tblColCount[s]++;
            }
        }

        void CompileTableColumn(int d)
        {
            int s = defSlot[d];
            if (defNode[d] >= 0)
            {
                // computed column
                allowFree = 0; polarImplicit = false;
                int pid = CompileProgram(defNode[d], -1);
                if (cerr) { FailDef(d, cerrMsg); return; }
                if (code[progStart[pid] + 1] == 0) { FailDef(d, "Column formula must use a list (another column)"); return; }
                defProg[d] = pid;
                defList[d] = AllocList(1);
                return;
            }
            // literal cells
            int pid2 = BeginProgram();
            int pos = codeLen;
            int nrows = defTableRows[d];
            Emit(OP_LISTLIT); Emit(nrows); AddSource(pos);
            string[] cells = nrows > 0 ? defColumnCells[d].Split(new char[] { TBL_ROW }) : new string[0];
            for (int r = 0; r < nrows; r++)
            {
                double v = double.NaN;
                string cell = r < cells.Length ? cells[r] : "";
                if (cell.Trim().Length > 0)
                {
                    perr = false;
                    Tokenize(cell);
                    if (!perr)
                    {
                        int saveNodes = nodeCount;
                        int saveArgs = argCount;
                        int n = ParseTop();
                        if (!perr) v = FoldConst(n);
                        nodeCount = saveNodes;
                        argCount = saveArgs;
                    }
                }
                int st = BeginSub();
                EmitConst(v, 0);
                EndSub(st);
            }
            EndProgram(pid2);
            defProg[d] = pid2;
            defList[d] = AllocList(1);
        }

        void CompileTableEntries(int s)
        {
            if (slotKind[s] == K_ERROR || tblColDef == null) return;
            int cols = tblColCount[s];
            if (cols < 2) return;
            int d0 = tblColDef[s * MAX_TCOLS];
            if (defState[d0] != DS_OK) return;
            for (int c = 1; c < cols; c++)
            {
                int dc = tblColDef[s * MAX_TCOLS + c];
                if (defState[dc] != DS_OK) continue;
                if (entryCount >= MAX_ENTRIES) return;
                int pa = BeginProgram();
                int pos = codeLen; Emit(OP_LISTVAR); Emit(defList[d0]); AddSource(pos);
                EndProgram(pa);
                int pb = BeginProgram();
                pos = codeLen; Emit(OP_LISTVAR); Emit(defList[dc]); AddSource(pos);
                EndProgram(pb);
                int e = NewEntry(s, E_POINTS, pa, pb, R_NONE);
                if (e < 0) return;
                slotPlottable[s] = true;
                slotDefMask[s] |= (1L << d0) | (1L << dc);
            }
        }

        // =================================================================== def compile (topological)
        [RecursiveMethod]
        void VisitDef(int d)
        {
            if (defState[d] != DS_NONE) return;
            defState[d] = DS_VISITING;
            long deps = defDeps[d];
            for (int j = 0; j < defCount; j++)
            {
                if ((deps & (1L << j)) == 0) continue;
                if (defState[j] == DS_VISITING)
                {
                    FailDef(d, "Circular definition involving '" + DefLabel(j) + "'");
                    return;
                }
                if (defState[j] == DS_NONE) VisitDef(j);
                if (defState[j] == DS_ERR)
                {
                    FailDef(d, "'" + DefLabel(j) + "' has an error");
                    return;
                }
            }
            CompileDef(d);
            if (defState[d] == DS_VISITING)
            {
                defState[d] = DS_OK;
                if (evalCount < MAX_DEFS) evalOrder[evalCount++] = d;
            }
        }

        string DefLabel(int d)
        {
            if (defName[d] != null && defName[d].Length > 0) return defName[d];
            return "expression " + (defSlot[d] + 1);
        }

        void FailDef(int d, string msg)
        {
            defState[d] = DS_ERR;
            int s = defSlot[d];
            if (s >= 0 && slotKind[s] != K_ERROR) SlotErr(s, msg);
        }

        int AllocParam(int n)
        {
            if (paramCount + n > MAX_PARAMS) return -1;
            int p = paramCount;
            paramCount += n;
            return p;
        }

        int AllocList(int n)
        {
            if (listCount + n > MAX_LISTS) return -1;
            int l = listCount;
            listCount += n;
            return l;
        }

        void CompileDef(int d)
        {
            int s = defSlot[d];
            if (defColumnSlot[d] >= 0) { CompileTableColumn(d); return; }
            if (defKind[d] == DK_REG) { CompileRegression(d); return; }
            int rhs = defNode[d];
            allowFree = 0; polarImplicit = false;
            if (defIsSliderDef[d])
            {
                int pid = BeginProgram();
                slotSliderCst[s] = EmitConst(nNum[rhs], 0);
                EndProgram(pid);
                defProg[d] = pid;
                defKind[d] = DK_SCALAR;
                defParam[d] = AllocParam(1);
                slotProg[s] = pid;
                return;
            }
            envTop = 0; callDepth = 0;
            bool isPoint = IsPointNode(rhs, 0);
            if (isPoint)
            {
                int pa = CompileProgram(rhs, 0);
                if (cerr) { FailDef(d, cerrMsg); return; }
                int pb = CompileProgram(rhs, 1);
                if (cerr) { FailDef(d, cerrMsg); return; }
                defProg[d] = pa;
                defProgB[d] = pb;
                bool isList = code[progStart[pa] + 1] > 0 || code[progStart[pb] + 1] > 0;
                if (isList) { defKind[d] = DK_POINTLIST; defList[d] = AllocList(2); }
                else { defKind[d] = DK_POINT; defParam[d] = AllocParam(2); }
                slotKind[s] = K_POINTDEF;
                slotProg[s] = pa;
                return;
            }
            int pid2 = CompileProgram(rhs, -1);
            if (cerr) { FailDef(d, cerrMsg); if (cerrUndef.Length > 0) slotUndef[s] = cerrUndef; return; }
            defProg[d] = pid2;
            slotProg[s] = pid2;
            if (code[progStart[pid2] + 1] > 0) { defKind[d] = DK_LIST; defList[d] = AllocList(1); }
            else { defKind[d] = DK_SCALAR; defParam[d] = AllocParam(1); }
            if (defList[d] < 0 && defParam[d] < 0) FailDef(d, "Too many variables");
        }

        void CompileRegression(int d)
        {
            int L = defNode[d];
            int R = defNode2[d];
            allowFree = 0; polarImplicit = false;
            int diff = NewNode(N_BIN, B_SUB, L, R, 0, "");
            int pid = CompileProgram(diff, -1);
            if (cerr) { FailDef(d, cerrMsg); return; }
            if (code[progStart[pid] + 1] == 0) { FailDef(d, "Regression needs list data (e.g. y_1 ~ m x_1 + b)"); return; }
            if (defRegParamCount[d] == 0) { FailDef(d, "Regression has no parameters to fit"); return; }
            int lhs = CompileProgram(L, -1);
            if (cerr) { cerr = false; lhs = -1; }
            defProgB[d] = lhs;
            defProg[d] = pid;
            defKind[d] = DK_REG;
            slotProg[defSlot[d]] = pid;
        }

        void CompileRegressionModel(int s)
        {
            int d = slotDef[s];
            int R = defNode2[d];
            // find the first list def referenced in R to substitute with x
            BeginScan();
            ScanNode(R, 0);
            int listDef = -1;
            for (int j = 0; j < defCount; j++)
            {
                if ((scanDefs & (1L << j)) == 0) continue;
                if (defKind[j] == DK_LIST && defState[j] == DS_OK && defName[j].Length > 0) { listDef = j; break; }
            }
            slotDefMask[s] |= (1L << d);
            if (listDef < 0) return;
            allowFree = FV_X; polarImplicit = false;
            envTop = 0;
            envName[0] = defName[listDef]; envKind[0] = EK_SLOT; envSlot[0] = VAR_X; envNode[0] = -1; envFrame[0] = 0;
            envTop = 1;
            int pid = CompileProgramEnv(R, -1, 1);
            envTop = 0;
            if (cerr) { cerr = false; return; }
            if (code[progStart[pid] + 1] > 0) return; // still list-valued; skip model curve
            NewEntry(s, E_EXPLICIT_Y, pid, -1, R_EQ);
            slotPlottable[s] = true;
        }

        void CompilePointDefEntry(int s)
        {
            int d = slotDef[s];
            if (d < 0 || defState[d] != DS_OK) return;
            int pa = BeginProgram();
            if (defKind[d] == DK_POINT) { Emit(OP_PARAM); Emit(defParam[d]); }
            else { int pos = codeLen; Emit(OP_LISTVAR); Emit(defList[d]); AddSource(pos); }
            EndProgram(pa);
            int pb = BeginProgram();
            if (defKind[d] == DK_POINT) { Emit(OP_PARAM); Emit(defParam[d] + 1); }
            else { int pos = codeLen; Emit(OP_LISTVAR); Emit(defList[d] + 1); AddSource(pos); }
            EndProgram(pb);
            NewEntry(s, E_POINTS, pa, pb, R_NONE);
            slotPlottable[s] = true;
        }

        int NewEntry(int s, int kind, int pa, int pb, int rel)
        {
            if (entryCount >= MAX_ENTRIES) return -1;
            int e = entryCount++;
            eSlot[e] = s; eKind[e] = kind; eProgA[e] = pa; eProgB[e] = pb; eRel[e] = rel;
            eDomLo[e] = -1; eDomHi[e] = -1; eDomLoDef[e] = 0; eDomHiDef[e] = 1; eRegion[e] = false;
            return e;
        }

        // =================================================================== plot classification
        int FlipRel(int r)
        {
            if (r == R_LT) return R_GT;
            if (r == R_GT) return R_LT;
            if (r == R_LE) return R_GE;
            if (r == R_GE) return R_LE;
            return r;
        }

        int ScanFree(int node)
        {
            BeginScan();
            ScanNode(node, 0);
            return scanFree;
        }

        bool IsIdent(int node, string nm) { return node >= 0 && nType[node] == N_IDENT && nStr[node] == nm; }

        void CompilePlotSlot(int s)
        {
            int root = slotRoot[s];
            envTop = 0; callDepth = 0;
            BeginScan();
            ScanNode(root, 0);
            if (cerr) { SlotErr(s, cerrMsg); return; }
            int fvAll = scanFree;
            slotDefMask[s] = scanDefs;
            if (scanUndefCount > 0)
            {
                SlotErr(s, "'" + scanUndefNames[0] + "' is not defined");
                slotUndef[s] = scanUndefNames[0];
                return;
            }
            for (int j = 0; j < defCount; j++)
            {
                if ((scanDefs & (1L << j)) != 0 && defState[j] != DS_OK)
                {
                    SlotErr(s, "'" + DefLabel(j) + "' has an error");
                    return;
                }
            }
            int rt = nType[root];
            if (rt == N_REL)
            {
                int op = nA[root];
                int L = nB[root];
                int R = nC[root];
                int fvL = ScanFree(L);
                int fvR = ScanFree(R);
                bool rDefined = FindDef("r") >= 0;
                if (IsIdent(L, "y") && (fvR & FV_Y) == 0) { PlotExplicit(s, E_EXPLICIT_Y, R, op, FV_X); return; }
                if (IsIdent(R, "y") && (fvL & FV_Y) == 0) { PlotExplicit(s, E_EXPLICIT_Y, L, FlipRel(op), FV_X); return; }
                if (IsIdent(L, "x") && (fvR & FV_X) == 0) { PlotExplicit(s, E_EXPLICIT_X, R, op, FV_Y); return; }
                if (IsIdent(R, "x") && (fvL & FV_X) == 0) { PlotExplicit(s, E_EXPLICIT_X, L, FlipRel(op), FV_Y); return; }
                if (!rDefined && IsIdent(L, "r") && (fvR & (FV_X | FV_Y | FV_R)) == 0) { PlotPolar(s, R, op); return; }
                if (!rDefined && IsIdent(R, "r") && (fvL & (FV_X | FV_Y | FV_R)) == 0) { PlotPolar(s, L, FlipRel(op)); return; }
                if ((fvAll & FV_T) != 0) { SlotErr(s, "t only works in (x(t), y(t)) curves"); return; }
                if ((fvAll & (FV_X | FV_Y | FV_R | FV_THETA)) == 0) { SlotErr(s, "Equation needs x or y"); return; }
                if (IsPointNode(L, 0) || IsPointNode(R, 0)) { SlotErr(s, "Can't compare points"); return; }
                polarImplicit = (fvAll & (FV_R | FV_THETA)) != 0;
                allowFree = FV_X | FV_Y;
                int diff = NewNode(N_BIN, B_SUB, L, R, 0, "");
                int pid = CompileProgram(diff, -1);
                polarImplicit = false;
                if (cerr) { SlotErr(s, cerrMsg); return; }
                NewEntry(s, E_IMPLICIT, pid, -1, op);
                slotPlottable[s] = true;
                return;
            }
            if (rt == N_AND)
            {
                if ((fvAll & (FV_X | FV_Y | FV_R | FV_THETA)) == 0) { SlotErr(s, "Inequality needs x or y"); return; }
                polarImplicit = (fvAll & (FV_R | FV_THETA)) != 0;
                allowFree = FV_X | FV_Y;
                int pid = CompileProgram(root, -1);
                polarImplicit = false;
                if (cerr) { SlotErr(s, cerrMsg); return; }
                int e = NewEntry(s, E_IMPLICIT, pid, -1, R_GT);
                if (e >= 0) eRegion[e] = true;
                slotPlottable[s] = true;
                return;
            }
            // plain expression
            if (IsPointNode(root, 0))
            {
                if ((fvAll & FV_T) != 0)
                {
                    if ((fvAll & ~FV_T) != 0) { SlotErr(s, "Parametric curves only use t"); return; }
                    PlotParametric(s, root);
                    return;
                }
                if (fvAll != 0) { SlotErr(s, "Points can't use " + FreeVarMessage(fvAll)); return; }
                allowFree = 0;
                int pa = CompileProgram(root, 0);
                if (cerr) { SlotErr(s, cerrMsg); return; }
                int pb = CompileProgram(root, 1);
                if (cerr) { SlotErr(s, cerrMsg); return; }
                NewEntry(s, E_POINTS, pa, pb, R_NONE);
                slotPlottable[s] = true;
                return;
            }
            if (fvAll == 0)
            {
                allowFree = 0;
                int pid = CompileProgram(root, -1);
                if (cerr) { SlotErr(s, cerrMsg); if (cerrUndef.Length > 0) slotUndef[s] = cerrUndef; return; }
                slotKind[s] = K_VALUE;
                slotProg[s] = pid;
                if (complexMode)
                {
                    NewEntry(s, E_VALUEPOINT, pid, -1, R_NONE);
                    slotPlottable[s] = true;
                }
                return;
            }
            if (fvAll == FV_X) { PlotExplicit(s, E_EXPLICIT_Y, root, R_EQ, FV_X); return; }
            if ((fvAll & FV_Y) != 0) { SlotErr(s, "Add 'y =' or an equation to graph this"); return; }
            if ((fvAll & FV_T) != 0) { SlotErr(s, "t is used in (x(t), y(t)) curves"); return; }
            SlotErr(s, FreeVarMessage(fvAll) + " is used in r = f(θ) curves");
        }

        void PlotExplicit(int s, int kind, int rhs, int rel, int free)
        {
            if (IsPointNode(rhs, 0)) { SlotErr(s, "Can't set " + (kind == E_EXPLICIT_Y ? "y" : "x") + " to a point"); return; }
            allowFree = free; polarImplicit = false;
            int pid = CompileProgram(rhs, -1);
            if (cerr) { SlotErr(s, cerrMsg); return; }
            NewEntry(s, kind, pid, -1, rel);
            slotPlottable[s] = true;
        }

        // strips a trailing {a <= var <= b} restriction and returns the stripped node; sets domLoNode/domHiNode
        int domLoNode;
        int domHiNode;

        int StripDomain(int node, string v)
        {
            domLoNode = -1; domHiNode = -1;
            if (nType[node] != N_BIN || nA[node] != B_MUL) return node;
            int pc = nC[node];
            if (nType[pc] != N_PIECE || nB[pc] != 0) return node;
            int pairs = nA[pc];
            for (int p = 0; p < pairs; p++)
            {
                int cond = Arg(pc, p * 2);
                int val = Arg(pc, p * 2 + 1);
                if (nType[val] != N_NUM || nNum[val] != 1.0) return node;
                if (!ExtractBounds(cond, v)) return node;
            }
            return nB[node];
        }

        [RecursiveMethod]
        bool ExtractBounds(int cond, string v)
        {
            if (nType[cond] == N_AND) return ExtractBounds(nB[cond], v) && ExtractBounds(nC[cond], v);
            if (nType[cond] != N_REL) return false;
            int op = nA[cond];
            int L = nB[cond], R = nC[cond];
            if (op == R_EQ) return false;
            bool less = op == R_LT || op == R_LE;
            if (IsIdent(L, v)) { if (less) domHiNode = R; else domLoNode = R; return true; }
            if (IsIdent(R, v)) { if (less) domLoNode = L; else domHiNode = L; return true; }
            return false;
        }

        void PlotParametric(int s, int root)
        {
            int body = StripDomain(root, "t");
            allowFree = FV_T; polarImplicit = false;
            int pa = CompileProgram(body, 0);
            if (cerr) { SlotErr(s, cerrMsg); return; }
            int pb = CompileProgram(body, 1);
            if (cerr) { SlotErr(s, cerrMsg); return; }
            int e = NewEntry(s, E_PARAM, pa, pb, R_NONE);
            if (e < 0) return;
            eDomLoDef[e] = 0; eDomHiDef[e] = 1;
            allowFree = 0;
            if (domLoNode >= 0) { eDomLo[e] = CompileProgram(domLoNode, -1); if (cerr) { SlotErr(s, "Domain: " + cerrMsg); return; } }
            if (domHiNode >= 0) { eDomHi[e] = CompileProgram(domHiNode, -1); if (cerr) { SlotErr(s, "Domain: " + cerrMsg); return; } }
            slotPlottable[s] = true;
        }

        void PlotPolar(int s, int rhs, int rel)
        {
            int body = StripDomain(rhs, "θ");
            int dl = domLoNode, dh = domHiNode;
            allowFree = FV_THETA; polarImplicit = false;
            int pid = CompileProgram(body, -1);
            if (cerr) { SlotErr(s, cerrMsg); return; }
            int e = NewEntry(s, E_POLAR, pid, -1, rel);
            if (e < 0) return;
            eDomLoDef[e] = 0; eDomHiDef[e] = 12.0 * System.Math.PI;
            allowFree = 0;
            if (dl >= 0) { eDomLo[e] = CompileProgram(dl, -1); if (cerr) { SlotErr(s, "Domain: " + cerrMsg); return; } }
            if (dh >= 0) { eDomHi[e] = CompileProgram(dh, -1); if (cerr) { SlotErr(s, "Domain: " + cerrMsg); return; } }
            slotPlottable[s] = true;
        }

        // =================================================================== scanning (dependencies, free vars)
        void BeginScan()
        {
            scanFree = 0; scanDefs = 0; scanUndefCount = 0;
            cerr = false; cerrMsg = ""; cerrUndef = "";
            envTop = 0; callDepth = 0;
        }

        void AddUndef(string nm)
        {
            for (int i = 0; i < scanUndefCount; i++) if (scanUndefNames[i] == nm) return;
            if (scanUndefCount < MAX_UNDEF) scanUndefNames[scanUndefCount++] = nm;
        }

        void PushEnvSlot(string nm, int slot)
        {
            if (envTop >= MAX_ENV) { CErr("Expression too deeply nested"); return; }
            envName[envTop] = nm; envKind[envTop] = EK_SLOT; envSlot[envTop] = slot; envNode[envTop] = -1; envFrame[envTop] = 0;
            envTop++;
        }

        void PushEnvNode(string nm, int node, int frame)
        {
            if (envTop >= MAX_ENV) { CErr("Expression too deeply nested"); return; }
            envName[envTop] = nm; envKind[envTop] = EK_NODE; envNode[envTop] = node; envFrame[envTop] = frame; envSlot[envTop] = -1;
            envTop++;
        }

        int FindEnv(string nm, int envLim)
        {
            for (int e = envLim - 1; e >= 0; e--) if (envName[e] == nm) return e;
            return -1;
        }

        [RecursiveMethod]
        void ScanName(string nm, int envLim)
        {
            int e = FindEnv(nm, envLim);
            if (e >= 0)
            {
                if (envKind[e] == EK_NODE) ScanNode(envNode[e], envFrame[e]);
                return;
            }
            int d = FindDef(nm);
            if (d >= 0) { scanDefs |= (1L << d); return; }
            int rp = FindRegParam(nm);
            if (rp >= 0) { scanDefs |= (1L << regParamDef[rp]); return; }
            if (nm == "x") { scanFree |= FV_X; return; }
            if (nm == "y") { scanFree |= FV_Y; return; }
            if (nm == "t") { scanFree |= FV_T; return; }
            if (nm == "θ") { scanFree |= FV_THETA; return; }
            if (nm == "r") { scanFree |= FV_R; return; }
            if (IsConstName(nm)) return;
            if (IsUserFunc(nm)) { CErr("'" + nm + "' is a function; use " + nm + "(...)"); return; }
            AddUndef(nm);
        }

        [RecursiveMethod]
        void ScanNode(int node, int envLim)
        {
            if (node < 0 || cerr) return;
            int t = nType[node];
            if (t == N_NUM) return;
            if (t == N_IDENT) { ScanName(nStr[node], envLim); return; }
            if (t == N_CALL)
            {
                int fi = FindFunc(nStr[node]);
                if (fi < 0 || funcBody[fi] < 0) { CErr("'" + nStr[node] + "' has an error"); return; }
                int pc = funcParamCount[fi];
                if (nArgCount[node] != pc) { CErr(nStr[node] + " takes " + pc + " argument" + (pc == 1 ? "" : "s")); return; }
                callDepth++;
                if (callDepth > 24) { CErr("Functions can't call themselves"); callDepth--; return; }
                int save = envTop;
                for (int p = 0; p < pc; p++) PushEnvNode(funcParam[fi * MAX_FPARAMS + p], Arg(node, p), envLim);
                ScanNode(funcBody[fi], envTop);
                envTop = save;
                callDepth--;
                return;
            }
            if (t == N_DERIV)
            {
                ScanName(nStr[node], envLim);
                int save = envTop;
                PushEnvSlot(nStr[node], 0);
                ScanNode(nB[node], envTop);
                envTop = save;
                return;
            }
            if (t == N_SUM || t == N_PROD || t == N_INT)
            {
                ScanNode(nB[node], envLim);
                ScanNode(nC[node], envLim);
                int save = envTop;
                PushEnvSlot(nStr[node], 0);
                ScanNode(nD[node], envTop);
                envTop = save;
                return;
            }
            if (nB[node] >= 0 && t != N_PIECE && t != N_RANGE && t != N_LIST && t != N_TUPLE) ScanNode(nB[node], envLim);
            if (nC[node] >= 0 && t != N_PIECE && t != N_RANGE && t != N_LIST && t != N_TUPLE) ScanNode(nC[node], envLim);
            int ac = nArgCount[node];
            for (int i = 0; i < ac; i++) ScanNode(Arg(node, i), envLim);
        }

        // =================================================================== point type inference
        [RecursiveMethod]
        bool IsPointNode(int node, int envLim)
        {
            if (node < 0) return false;
            int t = nType[node];
            if (t == N_TUPLE) return true;
            if (t == N_IDENT)
            {
                string nm = nStr[node];
                int e = FindEnv(nm, envLim);
                if (e >= 0) return envKind[e] == EK_NODE && IsPointNode(envNode[e], envFrame[e]);
                int d = FindDef(nm);
                if (d >= 0) return defKind[d] == DK_POINT || defKind[d] == DK_POINTLIST || (defState[d] != DS_OK && defNode[d] >= 0 && defKind[d] != DK_REG && defColumnSlot[d] < 0 && nType[defNode[d]] == N_TUPLE);
                return false;
            }
            if (t == N_BIN)
            {
                int op = nA[node];
                if (op == B_ADD || op == B_SUB || op == B_MUL) return IsPointNode(nB[node], envLim) || IsPointNode(nC[node], envLim);
                if (op == B_DIV) return IsPointNode(nB[node], envLim);
                return false;
            }
            if (t == N_NEG) return IsPointNode(nB[node], envLim);
            if (t == N_LIST) return nArgCount[node] > 0 && IsPointNode(Arg(node, 0), envLim);
            if (t == N_INDEX) return IsPointNode(nB[node], envLim);
            if (t == N_PIECE)
            {
                int pairs = nA[node];
                for (int p = 0; p < pairs; p++) if (IsPointNode(Arg(node, p * 2 + 1), envLim)) return true;
                if (nB[node] == 1) return IsPointNode(Arg(node, pairs * 2), envLim);
                return false;
            }
            if (t == N_CALL)
            {
                int fi = FindFunc(nStr[node]);
                if (fi < 0 || funcBody[fi] < 0) return false;
                if (callDepth > 24) return false;
                int pc = funcParamCount[fi];
                if (nArgCount[node] != pc) return false;
                callDepth++;
                int save = envTop;
                for (int p = 0; p < pc; p++) PushEnvNode(funcParam[fi * MAX_FPARAMS + p], Arg(node, p), envLim);
                bool r = IsPointNode(funcBody[fi], envTop);
                envTop = save;
                callDepth--;
                return r;
            }
            if (t == N_DERIV || t == N_INT || t == N_SUM)
            {
                int body = t == N_DERIV ? nB[node] : nD[node];
                int save = envTop;
                PushEnvSlot(nStr[node], 0);
                bool r = IsPointNode(body, envTop);
                envTop = save;
                return r;
            }
            return false;
        }

        // =================================================================== code emission
        void CErr(string m)
        {
            if (!cerr) { cerr = true; cerrMsg = m; }
        }

        void Emit(int v)
        {
            if (codeLen >= MAX_CODE) { CErr("Program too large"); return; }
            code[codeLen++] = v;
        }

        int EmitConst(double re, double im)
        {
            if (cstLen + 2 > cst.Length) { CErr("Too many constants"); return 0; }
            int idx = cstLen;
            cst[cstLen++] = re;
            cst[cstLen++] = im;
            Emit(OP_CONST);
            Emit(idx);
            return idx;
        }

        int BeginProgram()
        {
            if (progCount >= MAX_PROGS) { CErr("Too many programs"); return 0; }
            int pid = progCount++;
            progStart[pid] = codeLen;
            progEnd[pid] = codeLen;
            bcTop = 0;
            BeginFrame();
            boundNext = FIRST_BOUND_VAR;
            return pid;
        }

        void EndProgram(int pid)
        {
            EndFrame();
            progEnd[pid] = codeLen;
        }

        void BeginFrame()
        {
            if (bcTop >= MAX_FRAMES) { CErr("Expression too deeply nested"); return; }
            int h = codeLen;
            Emit(OP_HDR);
            Emit(0);
            for (int i = 0; i < MAX_SRC; i++) Emit(0);
            bcStack[bcTop] = h;
            bcElemDepth[bcTop] = 0;
            bcTop++;
        }

        void EndFrame() { if (bcTop > 0) bcTop--; }

        void AddSource(int pos)
        {
            if (bcTop <= 0) return;
            if (bcElemDepth[bcTop - 1] > 0) { CErr("Lists can't contain lists"); return; }
            int h = bcStack[bcTop - 1];
            int c = code[h + 1];
            if (c < MAX_SRC) { code[h + 2 + c] = pos; code[h + 1] = c + 1; }
        }

        int BeginSub() { Emit(0); return codeLen; }
        void EndSub(int start) { code[start - 1] = codeLen - start; }

        int AllocSlot()
        {
            if (boundNext >= MAX_VARS) { CErr("Too many nested sums/integrals/derivatives"); return FIRST_BOUND_VAR; }
            return boundNext++;
        }

        int CompileProgram(int node, int comp)
        {
            envTop = 0;
            return CompileProgramEnv(node, comp, 0);
        }

        int CompileProgramEnv(int node, int comp, int envLim)
        {
            cerr = false; cerrMsg = ""; cerrUndef = "";
            callDepth = 0;
            int pid = BeginProgram();
            CompileNode(node, comp, envLim);
            EndProgram(pid);
            return pid;
        }

        // =================================================================== node compilation
        [RecursiveMethod]
        void CompileName(string nm, int comp, int envLim)
        {
            int e = FindEnv(nm, envLim);
            if (e >= 0)
            {
                if (envKind[e] == EK_NODE) { CompileNode(envNode[e], comp, envFrame[e]); return; }
                if (comp >= 0) { CErr("'" + nm + "' is a number, not a point"); return; }
                Emit(OP_VAR); Emit(envSlot[e]);
                return;
            }
            int d = FindDef(nm);
            if (d >= 0)
            {
                if (defState[d] != DS_OK) { CErr("'" + nm + "' has an error"); return; }
                int k = defKind[d];
                if (k == DK_SCALAR)
                {
                    if (comp >= 0) { CErr("'" + nm + "' is a number, not a point"); return; }
                    Emit(OP_PARAM); Emit(defParam[d]);
                }
                else if (k == DK_LIST)
                {
                    if (comp >= 0) { CErr("'" + nm + "' is a list of numbers, not points"); return; }
                    int pos = codeLen; Emit(OP_LISTVAR); Emit(defList[d]); AddSource(pos);
                }
                else if (k == DK_POINT)
                {
                    if (comp < 0) { CErr("'" + nm + "' is a point; use " + nm + ".x or " + nm + ".y"); return; }
                    Emit(OP_PARAM); Emit(defParam[d] + comp);
                }
                else if (k == DK_POINTLIST)
                {
                    if (comp < 0) { CErr("'" + nm + "' is a list of points; use " + nm + ".x or " + nm + ".y"); return; }
                    int pos = codeLen; Emit(OP_LISTVAR); Emit(defList[d] + comp); AddSource(pos);
                }
                else CErr("'" + nm + "' can't be used here");
                return;
            }
            int rp = FindRegParam(nm);
            if (rp >= 0)
            {
                if (comp >= 0) { CErr("'" + nm + "' is a number"); return; }
                Emit(OP_PARAM); Emit(regParamId[rp]);
                return;
            }
            bool isFree = nm == "x" || nm == "y" || nm == "t" || nm == "θ" || nm == "r";
            if (isFree)
            {
                if (comp >= 0) { CErr("'" + nm + "' is a number, not a point"); return; }
                if (polarImplicit && nm == "r")
                {
                    Emit(OP_VAR); Emit(VAR_X); Emit(OP_VAR); Emit(VAR_X); Emit(OP_MUL);
                    Emit(OP_VAR); Emit(VAR_Y); Emit(OP_VAR); Emit(VAR_Y); Emit(OP_MUL);
                    Emit(OP_ADD); Emit(OP_FN1); Emit(F_SQRT);
                    return;
                }
                if (polarImplicit && nm == "θ")
                {
                    Emit(OP_VAR); Emit(VAR_Y); Emit(OP_VAR); Emit(VAR_X); Emit(OP_FN2); Emit(F_ATAN2);
                    if (degreeMode) { EmitConst(180.0 / System.Math.PI, 0); Emit(OP_MUL); }
                    return;
                }
                int bit = nm == "x" ? FV_X : nm == "y" ? FV_Y : nm == "t" ? FV_T : nm == "θ" ? FV_THETA : FV_R;
                if ((allowFree & bit) == 0)
                {
                    if (allowFree == 0) CErr(nm + " can't be used here");
                    else CErr(nm + " can't be used in this kind of graph");
                    return;
                }
                int slot = nm == "x" ? VAR_X : nm == "y" ? VAR_Y : nm == "t" ? VAR_T : VAR_THETA;
                Emit(OP_VAR); Emit(slot);
                return;
            }
            if (nm == "π") { if (comp >= 0) { CErr("π is a number"); return; } EmitConst(System.Math.PI, 0); return; }
            if (nm == "τ") { if (comp >= 0) { CErr("τ is a number"); return; } EmitConst(2.0 * System.Math.PI, 0); return; }
            if (nm == "e") { if (comp >= 0) { CErr("e is a number"); return; } EmitConst(System.Math.E, 0); return; }
            if (nm == "∞") { if (comp >= 0) { CErr("∞ is a number"); return; } EmitConst(double.PositiveInfinity, 0); return; }
            if (nm == "i")
            {
                if (!complexMode) { CErr("i needs complex mode (C button)"); return; }
                if (comp >= 0) { CErr("i is a number"); return; }
                EmitConst(0, 1);
                return;
            }
            CErr("'" + nm + "' is not defined");
            cerrUndef = nm;
        }

        bool IsReducer(int fid) { return fid >= F_TOTAL && fid <= F_LCM; }
        bool IsFn1(int fid) { return fid >= F_SIN && fid <= F_LAST1; }
        bool IsTrig(int fid) { return fid >= F_SIN && fid <= F_COT; }
        bool IsInvTrig(int fid) { return fid >= F_ASIN && fid <= F_ACOT; }

        [RecursiveMethod]
        void CompileNode(int node, int comp, int envLim)
        {
            if (cerr) return;
            if (node < 0) { CErr("Missing value"); return; }
            int t = nType[node];
            if (t == N_NUM)
            {
                if (comp >= 0) { CErr("Expected a point, found a number"); return; }
                EmitConst(nNum[node], 0);
                return;
            }
            if (t == N_IDENT) { CompileName(nStr[node], comp, envLim); return; }
            if (t == N_BIN) { CompileBin(node, comp, envLim); return; }
            if (t == N_NEG) { CompileNode(nB[node], comp, envLim); Emit(OP_NEG); return; }
            if (t == N_TUPLE)
            {
                if (comp < 0) { CErr("Points can't be used as numbers here"); return; }
                if (nArgCount[node] != 2) { CErr("Only 2D points (x, y) are supported"); return; }
                CompileNode(Arg(node, comp), -1, envLim);
                return;
            }
            if (t == N_LIST)
            {
                int m = nArgCount[node];
                int pos = codeLen;
                Emit(OP_LISTLIT); Emit(m); AddSource(pos);
                if (bcTop > 0) bcElemDepth[bcTop - 1]++;
                for (int i = 0; i < m; i++)
                {
                    int st = BeginSub();
                    CompileNode(Arg(node, i), comp, envLim);
                    EndSub(st);
                    if (cerr) break;
                }
                if (bcTop > 0) bcElemDepth[bcTop - 1]--;
                return;
            }
            if (t == N_RANGE)
            {
                if (comp >= 0) { CErr("Ranges contain numbers, not points"); return; }
                int m = nArgCount[node];
                int pos = codeLen;
                Emit(OP_RANGE); Emit(m); AddSource(pos);
                if (bcTop > 0) bcElemDepth[bcTop - 1]++;
                for (int i = 0; i < m; i++)
                {
                    int st = BeginSub();
                    CompileNode(Arg(node, i), -1, envLim);
                    EndSub(st);
                    if (cerr) break;
                }
                if (bcTop > 0) bcElemDepth[bcTop - 1]--;
                return;
            }
            if (t == N_INDEX)
            {
                CompileNode(nC[node], -1, envLim);
                Emit(OP_INDEX);
                int st = BeginSub();
                BeginFrame();
                CompileNode(nB[node], comp, envLim);
                EndFrame();
                EndSub(st);
                return;
            }
            if (t == N_REL)
            {
                if (comp >= 0) { CErr("Comparison used as a point"); return; }
                CompileNode(nB[node], -1, envLim);
                CompileNode(nC[node], -1, envLim);
                Emit(OP_CMP); Emit(nA[node]);
                return;
            }
            if (t == N_AND)
            {
                CompileNode(nB[node], -1, envLim);
                CompileNode(nC[node], -1, envLim);
                Emit(OP_AND);
                return;
            }
            if (t == N_PIECE) { CompilePiece(node, 0, comp, envLim); return; }
            if (t == N_FACT)
            {
                if (comp >= 0) { CErr("Factorial of a point"); return; }
                CompileNode(nB[node], -1, envLim);
                Emit(OP_FN1); Emit(F_FACT);
                return;
            }
            if (t == N_MEMBER)
            {
                if (comp >= 0) { CErr("." + (nA[node] == 0 ? "x" : "y") + " gives a number"); return; }
                if (!IsPointNode(nB[node], envLim)) { CErr(".x and .y only work on points"); return; }
                CompileNode(nB[node], nA[node], envLim);
                return;
            }
            if (t == N_BUILTIN) { CompileBuiltin(node, comp, envLim); return; }
            if (t == N_CALL) { CompileCall(node, comp, envLim); return; }
            if (t == N_DERIV)
            {
                CompileName(nStr[node], -1, envLim);
                int slot = AllocSlot();
                Emit(OP_DERIV); Emit(slot);
                int st = BeginSub();
                int save = envTop;
                PushEnvSlot(nStr[node], slot);
                CompileNode(nB[node], comp, envTop);
                envTop = save;
                EndSub(st);
                return;
            }
            if (t == N_SUM || t == N_PROD || t == N_INT)
            {
                if (t == N_PROD && comp >= 0) { CErr("Product of points"); return; }
                CompileNode(nB[node], -1, envLim);
                CompileNode(nC[node], -1, envLim);
                int slot = AllocSlot();
                Emit(t == N_SUM ? OP_SUM : t == N_PROD ? OP_PROD : OP_INT); Emit(slot);
                int st = BeginSub();
                int save = envTop;
                PushEnvSlot(nStr[node], slot);
                CompileNode(nD[node], comp, envTop);
                envTop = save;
                EndSub(st);
                return;
            }
            if (t == N_REG) { CErr("~ only works at the top level"); return; }
            CErr("Unsupported expression");
        }

        [RecursiveMethod]
        void CompileBin(int node, int comp, int envLim)
        {
            int op = nA[node];
            int L = nB[node];
            int R = nC[node];
            if (comp >= 0)
            {
                bool lp = IsPointNode(L, envLim);
                bool rp = IsPointNode(R, envLim);
                if (op == B_ADD || op == B_SUB)
                {
                    if (!(lp && rp)) { CErr("Can't add a point and a number"); return; }
                    CompileNode(L, comp, envLim);
                    CompileNode(R, comp, envLim);
                    Emit(op == B_ADD ? OP_ADD : OP_SUB);
                    return;
                }
                if (op == B_MUL)
                {
                    if (lp && rp) { CErr("Can't multiply two points"); return; }
                    if (lp) { CompileNode(L, comp, envLim); CompileNode(R, -1, envLim); }
                    else { CompileNode(L, -1, envLim); CompileNode(R, comp, envLim); }
                    Emit(OP_MUL);
                    return;
                }
                if (op == B_DIV)
                {
                    if (!lp || rp) { CErr("Can only divide a point by a number"); return; }
                    CompileNode(L, comp, envLim);
                    CompileNode(R, -1, envLim);
                    Emit(OP_DIV);
                    return;
                }
                CErr("Can't raise a point to a power");
                return;
            }
            CompileNode(L, -1, envLim);
            CompileNode(R, -1, envLim);
            if (op == B_ADD) Emit(OP_ADD);
            else if (op == B_SUB) Emit(OP_SUB);
            else if (op == B_MUL) Emit(OP_MUL);
            else if (op == B_DIV) Emit(OP_DIV);
            else Emit(OP_POW);
        }

        [RecursiveMethod]
        void CompilePiece(int node, int pairIdx, int comp, int envLim)
        {
            int pairs = nA[node];
            if (pairIdx >= pairs)
            {
                if (nB[node] == 1) CompileNode(Arg(node, pairs * 2), comp, envLim);
                else if (comp >= 0) EmitConst(double.NaN, 0);
                else EmitConst(double.NaN, 0);
                return;
            }
            CompileNode(Arg(node, pairIdx * 2), -1, envLim);
            CompileNode(Arg(node, pairIdx * 2 + 1), comp, envLim);
            CompilePiece(node, pairIdx + 1, comp, envLim);
            Emit(OP_SELECT);
        }

        [RecursiveMethod]
        void CompileBuiltin(int node, int comp, int envLim)
        {
            int fid = nA[node];
            int argc = nArgCount[node];
            int baseNode = nB[node];
            int powNode = nC[node];
            if (comp >= 0) { CErr("Functions return numbers, not points"); return; }
            if (argc == 0) { CErr("Function needs an argument"); return; }
            if (IsReducer(fid))
            {
                Emit(OP_REDUCE); Emit(fid);
                int st = BeginSub();
                BeginFrame();
                if (argc == 1) CompileNode(Arg(node, 0), -1, envLim);
                else
                {
                    int pos = codeLen;
                    Emit(OP_LISTLIT); Emit(argc); AddSource(pos);
                    bcElemDepth[bcTop - 1]++;
                    for (int i = 0; i < argc; i++)
                    {
                        int s2 = BeginSub();
                        CompileNode(Arg(node, i), -1, envLim);
                        EndSub(s2);
                    }
                    bcElemDepth[bcTop - 1]--;
                }
                EndFrame();
                EndSub(st);
            }
            else if (fid == F_LOG10 && baseNode >= 0)
            {
                if (argc != 1) { CErr("log takes one argument"); return; }
                CompileNode(Arg(node, 0), -1, envLim);
                CompileNode(baseNode, -1, envLim);
                Emit(OP_FN2); Emit(F_LOGB);
            }
            else if (IsFn1(fid))
            {
                if (argc == 2 && fid == F_ROUND)
                {
                    CompileNode(Arg(node, 0), -1, envLim);
                    CompileNode(Arg(node, 1), -1, envLim);
                    Emit(OP_FN2); Emit(F_ROUND2);
                }
                else if (argc == 2 && fid == F_ATAN)
                {
                    CompileNode(Arg(node, 0), -1, envLim);
                    CompileNode(Arg(node, 1), -1, envLim);
                    Emit(OP_FN2); Emit(F_ATAN2);
                    if (degreeMode) { EmitConst(180.0 / System.Math.PI, 0); Emit(OP_MUL); }
                }
                else if (argc == 2 && fid == F_LOG10)
                {
                    CompileNode(Arg(node, 0), -1, envLim);
                    CompileNode(Arg(node, 1), -1, envLim);
                    Emit(OP_FN2); Emit(F_LOGB);
                }
                else
                {
                    if (argc != 1) { CErr("Function takes one argument"); return; }
                    CompileNode(Arg(node, 0), -1, envLim);
                    if (degreeMode && IsTrig(fid)) { EmitConst(System.Math.PI / 180.0, 0); Emit(OP_MUL); }
                    Emit(OP_FN1); Emit(fid);
                    if (degreeMode && IsInvTrig(fid)) { EmitConst(180.0 / System.Math.PI, 0); Emit(OP_MUL); }
                }
            }
            else
            {
                if (argc != 2) { CErr("Function takes two arguments"); return; }
                CompileNode(Arg(node, 0), -1, envLim);
                CompileNode(Arg(node, 1), -1, envLim);
                Emit(OP_FN2); Emit(fid);
            }
            if (powNode >= 0)
            {
                CompileNode(powNode, -1, envLim);
                Emit(OP_POW);
            }
        }

        [RecursiveMethod]
        void CompileCall(int node, int comp, int envLim)
        {
            string nm = nStr[node];
            int fi = FindFunc(nm);
            if (fi < 0 || funcBody[fi] < 0) { CErr("'" + nm + "' has an error"); return; }
            int pc = funcParamCount[fi];
            int argc = nArgCount[node];
            if (argc != pc) { CErr(nm + " takes " + pc + " argument" + (pc == 1 ? "" : "s")); return; }
            callDepth++;
            if (callDepth > 24) { CErr("Functions can't call themselves"); callDepth--; return; }
            int primes = nD[node];
            if (primes <= 0)
            {
                int save = envTop;
                for (int p = 0; p < pc; p++) PushEnvNode(funcParam[fi * MAX_FPARAMS + p], Arg(node, p), envLim);
                CompileNode(funcBody[fi], comp, envTop);
                envTop = save;
            }
            else
            {
                CompileNode(Arg(node, 0), -1, envLim);
                EmitDerivChain(fi, node, primes, comp, envLim);
            }
            callDepth--;
        }

        [RecursiveMethod]
        void EmitDerivChain(int fi, int node, int remaining, int comp, int envLim)
        {
            int slot = AllocSlot();
            Emit(OP_DERIV); Emit(slot);
            int st = BeginSub();
            if (remaining > 1)
            {
                Emit(OP_VAR); Emit(slot);
                EmitDerivChain(fi, node, remaining - 1, comp, envLim);
            }
            else
            {
                int save = envTop;
                int pc = funcParamCount[fi];
                PushEnvSlot(funcParam[fi * MAX_FPARAMS], slot);
                for (int p = 1; p < pc; p++) PushEnvNode(funcParam[fi * MAX_FPARAMS + p], Arg(node, p), envLim);
                CompileNode(funcBody[fi], comp, envTop);
                envTop = save;
            }
            EndSub(st);
        }

        // =================================================================== runtime helpers for controller
        /// <summary>Fast path: change a slider's value without recompiling.</summary>
        public bool SetSliderValue(int slot, double v)
        {
            if (slot < 0 || slot >= slotCount) return false;
            if (!slotIsSlider[slot]) return false;
            int ci = slotSliderCst[slot];
            if (ci < 0) return false;
            cst[ci] = v;
            cst[ci + 1] = 0;
            slotSliderValue[slot] = v;
            return true;
        }

        public string GetRegParamName(int d, int i)
        {
            string names = defRegParamNames[d];
            if (names == null) return "";
            string[] parts = names.Split(new char[] { ',' });
            return i < parts.Length ? parts[i] : "";
        }
    }
}
