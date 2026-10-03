// Contour d'un perso (Loup-garou : joueur survole / choisi) : coque inversee, epaisseur constante a l'ecran.
Shader "PiqueNique/Outline"
{
    Properties
    {
        _Color ("Couleur", Color) = (1, 0.85, 0.3, 1)
        _Width ("Epaisseur", Float) = 0.006
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END
            struct Attributes { float4 pos : POSITION; float3 normal : NORMAL; };
            struct Varyings { float4 pos : SV_POSITION; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                float3 n = TransformWorldToHClipDir(TransformObjectToWorldNormal(i.normal));
                o.pos.xy += normalize(n.xy) * _Width * o.pos.w * 2;
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
