// Translucent inequality shading (quads built by GCEngine), clipped to the board.
Shader "GraphCalc/Fill"
{
    Properties
    {
        _Color ("Color", Color) = (0,0,0,1)
        _Alpha ("Fill alpha", Float) = 0.22
        _Xform ("Xform", Vector) = (1,1,0,0)
        _BoardHalf ("Board half size (m)", Vector) = (1,0.75,0,0)
        _ZOffset ("Z offset", Float) = -0.001
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            float _Alpha;
            float4 _Xform;
            float4 _BoardHalf;
            float _ZOffset;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 board : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float2 P = v.vertex.xy * _Xform.xy + _Xform.zw;
                o.board = P;
                o.pos = UnityObjectToClipPos(float4(P, _ZOffset, 1));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (abs(i.board.x) > _BoardHalf.x || abs(i.board.y) > _BoardHalf.y) discard;
                return fixed4(_Color.rgb, _Alpha);
            }
            ENDCG
        }
    }
}
