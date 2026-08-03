Shader "Hidden/PhantomSystem/BoneDisplay"
{
    Properties
    {
        [HDR] _BaseColor ("Base Color", Color) = (0.0, 0.35, 0.45, 1.0)
        _BaseOpacity ("Base Opacity", Range(0.0, 1.0)) = 0.12

        [HDR] _RimColor ("Rim Color", Color) = (0.0, 1.0, 1.0, 1.0)
        _RimOpacity ("Rim Opacity", Range(0.0, 1.0)) = 0.9
        _RimPower ("Rim Power", Range(0.25, 8.0)) = 2.0
        _RimCutoff ("Rim Cutoff", Range(0.0, 0.99)) = 0.2
        _RimSoftness ("Rim Softness", Range(0.001, 0.5)) = 0.1

        [Enum(UnityEngine.Rendering.CullMode)]
        _Cull ("Cull Mode", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "VRCFallback" = "UnlitTransparent"
        }

        Pass
        {
            Name "BONE_RIM_XRAY"

            Cull [_Cull]
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _BaseColor;
            half _BaseOpacity;

            half4 _RimColor;
            half _RimOpacity;
            half _RimPower;
            half _RimCutoff;
            half _RimSoftness;

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.positionCS = UnityObjectToClipPos(v.vertex);
                o.positionWS = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normalWS = UnityObjectToWorldNormal(v.normal);

                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half3 normalWS = normalize(i.normalWS);
                half3 viewDirWS = normalize(UnityWorldSpaceViewDir(i.positionWS));

                half fresnel = 1.0h - saturate(abs(dot(normalWS, viewDirWS)));
                half shapedRim = pow(fresnel, _RimPower);

                half edge1 = min(1.0h, _RimCutoff + _RimSoftness);
                half rim = smoothstep(_RimCutoff, edge1, shapedRim);

                half baseAlpha = saturate(_BaseColor.a * _BaseOpacity);
                half rimAlpha = saturate(_RimColor.a * _RimOpacity * rim);

                half finalAlpha =
                    saturate(baseAlpha + rimAlpha * (1.0h - baseAlpha));

                half3 basePremultiplied = _BaseColor.rgb * baseAlpha;
                half3 rimPremultiplied =
                    _RimColor.rgb * rimAlpha * (1.0h - baseAlpha);

                half3 finalColor =
                    (basePremultiplied + rimPremultiplied)
                    / max(finalAlpha, 0.0001h);

                return half4(finalColor, finalAlpha);
            }
            ENDCG
        }
    }

    Fallback Off
}