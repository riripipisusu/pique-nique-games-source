// Contour d'un perso (Loup-garou : joueur survole / choisi). Deux materiaux du meme shader :
// - masque (_Width 0, ColorMask 0) : marque la silhouette dans le stencil ;
// - coque (Cull Front) : gonflee le long des normales lissees (rangees dans les tangentes, deformees par le squelette),
//   dessinee seulement hors de la silhouette : un trait net autour du perso, rien par-dessus.
Shader "PiqueNique/Outline"
{
    Properties
    {
        _Color ("Couleur", Color) = (1, 0.85, 0.3, 1)
        _Width ("Epaisseur", Float) = 0.006
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 1
        _ColorMask ("ColorMask", Float) = 15
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil test", Float) = 6
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass ("Stencil op", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+11" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull [_Cull]
            ColorMask [_ColorMask]
            ZWrite Off
            Stencil { Ref 77 ReadMask 255 WriteMask 255 Comp [_StencilComp] Pass [_StencilPass] }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END
            struct Attributes { float4 pos : POSITION; float4 tangent : TANGENT; };
            struct Varyings { float4 pos : SV_POSITION; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                if (_Width > 0)
                {
                    float2 n = TransformWorldToHClipDir(TransformObjectToWorldNormal(i.tangent.xyz)).xy;
                    float len = length(n);
                    if (len > 1e-5) o.pos.xy += n / len * float2(_ScreenParams.y / _ScreenParams.x, 1) * _Width * o.pos.w * 2;   // meme epaisseur en x et en y
                }
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
