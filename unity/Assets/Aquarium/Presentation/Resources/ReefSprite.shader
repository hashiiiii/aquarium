Shader "Aquarium/ReefSprite"
{
 Properties { _Color("Color", Color)=(1,1,1,1) _MainTex("Texture",2D)="white"{} }
 SubShader {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
  Pass {
   Tags { "LightMode"="UniversalForward" }
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
   struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
   TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
   CBUFFER_START(UnityPerMaterial)
   float4 _Color; float4 _MainTex_ST;
   CBUFFER_END
   V vert(A a) { V o; o.positionCS=TransformObjectToHClip(a.positionOS.xyz); o.uv=TRANSFORM_TEX(a.uv,_MainTex); return o; }
   half4 frag(V i):SV_Target { half4 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv)*_Color; clip(c.a-.005); return c; }
   ENDHLSL
  }
 }
}
