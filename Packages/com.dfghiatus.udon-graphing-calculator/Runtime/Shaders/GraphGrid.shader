// Board background with procedural major/minor grid and axes.
// Uniforms are computed in Udon (GraphCalculator.UpdateGrid) in double precision.
Shader "GraphCalc/Grid"
{
    Properties
    {
        _BgColor ("Background", Color) = (1,1,1,1)
        _MajorColor ("Major grid", Color) = (0.80,0.80,0.80,1)
        _MinorColor ("Minor grid", Color) = (0.92,0.92,0.92,1)
        _AxisColor ("Axes", Color) = (0.15,0.15,0.15,1)
        _MajorX ("Major X (phase, span, minorDiv, axisU)", Vector) = (0,10,5,0.5)
        _MajorY ("Major Y (phase, span, minorDiv, axisV)", Vector) = (0,7.5,5,0.5)
        _ShowGrid ("Show grid", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _BgColor, _MajorColor, _MinorColor, _AxisColor;
            float4 _MajorX, _MajorY;
            float _ShowGrid;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // coverage of a 1D line family at integer positions of g, width in pixels
            float lineCov(float g, float widthPx)
            {
                float fw = max(fwidth(g), 1e-6);
                float d = abs(frac(g + 0.5) - 0.5) / fw;
                return saturate(widthPx * 0.5 + 0.5 - d);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 col = _BgColor.rgb;
                float gx = _MajorX.x + i.uv.x * _MajorX.y;
                float gy = _MajorY.x + i.uv.y * _MajorY.y;
                if (_ShowGrid > 0.5)
                {
                    float mx = gx * _MajorX.z;
                    float my = gy * _MajorY.z;
                    // fade minor lines when they get too dense
                    float densX = 1.0 / max(fwidth(mx), 1e-6);
                    float densY = 1.0 / max(fwidth(my), 1e-6);
                    float minorA = max(lineCov(mx, 1.0) * saturate((densX - 4.0) / 6.0), lineCov(my, 1.0) * saturate((densY - 4.0) / 6.0));
                    col = lerp(col, _MinorColor.rgb, minorA);
                    float majorA = max(lineCov(gx, 1.2), lineCov(gy, 1.2));
                    col = lerp(col, _MajorColor.rgb, majorA);
                }
                // axes
                float fwu = max(fwidth(i.uv.x), 1e-7);
                float fwv = max(fwidth(i.uv.y), 1e-7);
                float ax = saturate(1.5 - abs(i.uv.x - _MajorX.w) / fwu);
                float ay = saturate(1.5 - abs(i.uv.y - _MajorY.w) / fwv);
                col = lerp(col, _AxisColor.rgb, max(ax, ay));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
