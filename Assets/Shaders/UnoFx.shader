// Effets du Uno : equivalent URP des anciens shaders "Particles" (couleur = texture x teinte x 2, teinte neutre 0.5).
// Les animations d'origine pilotent _Tint (r, g, b, a) ; melange choisi par materiau (transparent ou additif).
Shader "PiqueNique/UnoFx"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Teinte", Color) = (0.5, 0.5, 0.5, 0.5)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination", Float) = 10
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull [_Cull]
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
            CBUFFER_END
            struct Attributes { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Tint * 2;
            }
            ENDHLSL
        }
    }
}
