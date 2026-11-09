// Anti-aliased thick polyline segments with round caps.
// Vertex layout (built by GCEngine): position.xy = endpoint A (sample-local),
// uv0 = (B.x, B.y, side, end), uv1 = (arcStart, arcEnd) in sample-local units.
// _Xform maps sample-local -> board metres: board = p * _Xform.xy + _Xform.zw
Shader "GraphCalc/Line"
{
    Properties
    {
        _Color ("Color", Color) = (0,0,0,1)
        _HalfWidth ("Half width (m)", Float) = 0.0045
        _Xform ("Xform", Vector) = (1,1,0,0)
        _BoardHalf ("Board half size (m)", Vector) = (1,0.75,0,0)
        _Dash ("Dashed", Float) = 0
        _DashLen ("Dash period (m)", Float) = 0.03
        _ZOffset ("Z offset", Float) = -0.002
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
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

            float4 _Color;
            float _HalfWidth;
            float4 _Xform;
            float4 _BoardHalf;
            float _Dash;
            float _DashLen;
            float _ZOffset;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 local : TEXCOORD0;   // x along, y across, z segment length
                float2 board : TEXCOORD1;
                float arc : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float2 A = v.vertex.xy * _Xform.xy + _Xform.zw;
                float2 B = v.uv0.xy * _Xform.xy + _Xform.zw;
                float side = v.uv0.z;
                float e = v.uv0.w;
                float2 d = B - A;
                float L = length(d);
                float2 dir = L > 1e-7 ? d / L : float2(1, 0);
                float2 nrm = float2(-dir.y, dir.x);
                float hw = _HalfWidth * 1.6;
                float2 P = (e > 0.5 ? B : A) + nrm * side * hw + dir * (e > 0.5 ? hw : -hw);
                o.local = float3(e > 0.5 ? L + hw : -hw, side * hw, L);
                o.board = P;
                o.arc = lerp(v.uv1.x, v.uv1.y, e) * _Xform.x + (e > 0.5 ? hw : -hw);
                o.pos = UnityObjectToClipPos(float4(P, _ZOffset, 1));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (abs(i.board.x) > _BoardHalf.x || abs(i.board.y) > _BoardHalf.y) discard;
                float u = i.local.x, w = i.local.y, L = i.local.z;
                float dist = u < 0 ? length(float2(u, w)) : (u > L ? length(float2(u - L, w)) : abs(w));
                float aa = max(fwidth(dist), 1e-6);
                float a = saturate((_HalfWidth - dist) / aa + 0.5);
                if (_Dash > 0.5)
                {
                    float ph = frac(i.arc / _DashLen);
                    float fwp = max(fwidth(i.arc / _DashLen), 1e-4);
                    a *= saturate((0.6 - ph) / fwp + 0.5);
                }
                if (a <= 0.003) discard;
                return fixed4(_Color.rgb, _Color.a * a);
            }
            ENDCG
        }
    }
}
