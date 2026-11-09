// Implicit curves / inequality regions. Udon samples F(x,y) on a grid and stores a
// signed distance estimate (in grid cells) per node: R = d, G = validity, B/A = gradient dir.
// This shader only interpolates (bilinear) and thresholds: no expression math on the GPU.
// _Mode: 0 = curve only, 1 = shade d>0 + solid edge, 2 = shade d>0 + dashed edge, 3 = shade only.
Shader "GraphCalc/Implicit"
{
    Properties
    {
        _MainTex ("Distance field", 2D) = "black" {}
        _Color ("Color", Color) = (0,0,0,1)
        _HalfWidth ("Half width (m)", Float) = 0.0045
        _FillAlpha ("Fill alpha", Float) = 0.22
        _GridXform ("Board->UV (ax, ay, bx, by)", Vector) = (0.5,0.5,0.5,0.5)
        _CellBoard ("Cell size (m)", Float) = 0.01
        _BoardHalf ("Board half size (m)", Vector) = (1,0.75,0,0)
        _Mode ("Mode", Float) = 0
        _DashLen ("Dash period (m)", Float) = 0.03
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Color;
            float _HalfWidth;
            float _FillAlpha;
            float4 _GridXform;
            float _CellBoard;
            float4 _BoardHalf;
            float _Mode;
            float _DashLen;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 board : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.board = v.vertex.xy * 2.0 * _BoardHalf.xy; // unit quad scaled to the board
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.board * _GridXform.xy + _GridXform.zw;
                if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) discard;
                float4 s = tex2D(_MainTex, uv);
                float d = s.r;
                float valid = s.g;
                float fw = max(fwidth(d), 1e-5);
                float hwc = _HalfWidth / max(_CellBoard, 1e-6);

                float lineA = 0;
                if (_Mode < 2.5)
                {
                    lineA = saturate((hwc - abs(d)) / fw + 0.5) * saturate((valid - 0.6) * 8.0);
                    if (_Mode > 1.5)
                    {
                        float2 g = s.ba;
                        float2 t = length(g) > 1e-4 ? normalize(float2(-g.y, g.x)) : float2(1, 0);
                        float ph = frac(dot(i.board, t) / _DashLen);
                        lineA *= step(ph, 0.6);
                    }
                }
                float shade = 0;
                if (_Mode > 0.5)
                {
                    shade = saturate(d / fw + 0.5) * saturate((valid - 0.3) * 3.0) * _FillAlpha;
                }
                float a = lineA + shade * (1 - lineA);
                if (a <= 0.003) discard;
                return fixed4(_Color.rgb, a);
            }
            ENDCG
        }
    }
}
