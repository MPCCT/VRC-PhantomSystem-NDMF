Shader "Hidden/PhantomSystem/PhantomView"
{
    Properties
    {
        [NoScaleOffset] _LeftEyeTexture("Left Eye", 2D) = "black" {}
        [NoScaleOffset] _RightEyeTexture("Right Eye", 2D) = "black" {}
        [HideInInspector] _CaptureTanHalfVerticalFov(
            "Capture Tan Half Vertical FOV",
            Float
        ) = 1.0
        _MaskSizeAngleDegrees("Mask Size Angle", Range(0.0, 90.0)) = 40.0
        _MaskFadeRatio("Mask Fade Ratio", Range(0.0, 1.0)) = 0.25
        _Opacity("Opacity", Range(0.0, 1.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "VRCFallback" = "Hidden"
        }

        Cull Off
        ZTest Always
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _LeftEyeTexture;
            sampler2D _RightEyeTexture;
            float4 _LeftEyeTexture_TexelSize;
            float4 _RightEyeTexture_TexelSize;
            float _CaptureTanHalfVerticalFov;
            float _MaskSizeAngleDegrees;
            float _MaskFadeRatio;
            float _Opacity;

            float _VRChatCameraMode;
            float _VRChatMirrorMode;
            float _VRChatFaceMirrorMode;

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Source positions are ignored and the regular quad UV supplies
                // the clip-space corners for both multipass and SPI rendering.
                float2 clipXY = v.uv * 2.0 - 1.0;
                if (_ProjectionParams.x <= 0.0)
                {
                    clipXY.y = -clipXY.y;
                }

                o.vertex = float4(clipXY, 0.0, 1.0);
                o.screenPos = ComputeNonStereoScreenPos(o.vertex);
                return o;
            }

            float2 NonStereoScreenUV(float4 screenPosition)
            {
                return screenPosition.xy / screenPosition.w;
            }

            float2 RemapCaptureFov(
                float2 screenUV,
                out float viewAngleDegrees)
            {
                float4 clipPosition = float4(
                    screenUV * 2.0 - 1.0,
                    1.0,
                    1.0
                );
                float4 cameraSpacePosition = mul(
                    unity_CameraInvProjection,
                    clipPosition
                );
                float forwardDistance = max(
                    abs(cameraSpacePosition.z),
                    1e-5
                );
                float2 raySlope =
                    cameraSpacePosition.xy / forwardDistance;
                viewAngleDegrees = degrees(atan(length(raySlope)));

                float captureAspect = _LeftEyeTexture_TexelSize.z /
                    max(_LeftEyeTexture_TexelSize.w, 1.0);
                float tanHalfVerticalFov = max(
                    _CaptureTanHalfVerticalFov,
                    1e-4
                );
                float2 captureRayExtent = float2(
                    tanHalfVerticalFov * captureAspect,
                    tanHalfVerticalFov
                );
                float2 captureNdc = raySlope / captureRayExtent;
                float2 sampleUV = captureNdc * 0.5 + 0.5;

                #if UNITY_UV_STARTS_AT_TOP
                    if (_LeftEyeTexture_TexelSize.y < 0.0)
                    {
                        sampleUV.y = 1.0 - sampleUV.y;
                    }
                #endif

                return sampleUV;
            }

            float SampleBounds(float2 uv)
            {
                return step(0.0, uv.x) * step(uv.x, 1.0) *
                    step(0.0, uv.y) * step(uv.y, 1.0);
            }

            float4 SampleCurrentEye(float2 uv)
            {
                #if defined(USING_STEREO_MATRICES)
                    if (unity_StereoEyeIndex == 1)
                    {
                        return tex2D(_RightEyeTexture, uv);
                    }
                #endif

                // Desktop and non-stereo cameras deliberately use the left image.
                return tex2D(_LeftEyeTexture, uv);
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float normalScreenView =
                    1.0 - step(0.5, abs(_VRChatCameraMode));
                normalScreenView *=
                    1.0 - step(0.5, abs(_VRChatMirrorMode));
                normalScreenView *=
                    1.0 - step(0.5, abs(_VRChatFaceMirrorMode));
                clip(normalScreenView - 0.5);

                float2 screenUV = NonStereoScreenUV(i.screenPos);
                float viewAngleDegrees;
                float2 sampleUV = RemapCaptureFov(
                    screenUV,
                    viewAngleDegrees);

                float outerAngleDegrees = max(
                    _MaskSizeAngleDegrees,
                    1e-4);
                float innerAngleDegrees = min(
                    outerAngleDegrees * (1.0 - saturate(_MaskFadeRatio)),
                    outerAngleDegrees - 1e-4);
                float mask = 1.0 - smoothstep(
                    innerAngleDegrees,
                    outerAngleDegrees,
                    viewAngleDegrees);
                mask *= SampleBounds(sampleUV);

                float4 color = SampleCurrentEye(sampleUV);
                color.a *= saturate(mask) * saturate(_Opacity);
                clip(color.a - 1e-5);
                return color;
            }
            ENDCG
        }
    }

    Fallback Off
}
