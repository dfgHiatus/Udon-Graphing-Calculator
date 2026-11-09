// Anti-aliased point markers. Each point is a quad: position.xy = centre (sample-local), uv0.xy = corner (+-1).
Shader "GraphCalc/Point"
{
    Properties
    {
        _Color ("Color", Color) = (0,0,0,1)
        _Radius ("Radius (m)", Float) = 0.011
        _Xform ("Xform", Vector) = (1,1,0,0)
        _BoardHalf ("Board half size (m)", Vector) = (1,0.75,0,0)
        _ZOffset ("Z offset", Float) = -0.003
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Radius;
            float4 _Xform;
            float4 _BoardHalf;
            float _ZOffset;

            struct appdata { float4 vertex : POSITION; float4 uv0 : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 corner : TEXCOORD0; float2 board : TEXCOORD1; float2 center : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float2 C = v.vertex.xy * _Xform.xy + _Xform.zw;
                float2 P = C + v.uv0.xy * _Radius * 1.5;
                o.corner = v.uv0.xy * 1.5;
                o.board = P;
                o.center = C;
                o.pos = UnityObjectToClipPos(float4(P, _ZOffset, 1));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (abs(i.center.x) > _BoardHalf.x || abs(i.center.y) > _BoardHalf.y) discard;
                float d = length(i.corner); // in radii
                float aa = max(fwidth(d), 1e-5);
                float a = saturate((1.0 - d) / aa + 0.5);
                float rim = saturate((d - 0.72) / aa + 0.5);
                float3 col = lerp(_Color.rgb, _Color.rgb * 0.75, rim);
                if (a <= 0.003) discard;
                return fixed4(col, a);
            }
            ENDCG
        }
    }
}
