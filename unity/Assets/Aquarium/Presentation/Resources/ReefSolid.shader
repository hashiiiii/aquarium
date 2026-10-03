Shader "Aquarium/ReefSolid"
{
 Properties { _Color("Color", Color)=(1,1,1,1) _MainTex("Texture",2D)="white"{} }
 SubShader {
  Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
  Pass {
   Tags { "LightMode"="UniversalForwardOnly" }
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
   struct V { float4 positionCS:SV_POSITION; float3 normal:TEXCOORD0; float2 uv:TEXCOORD1; float depth:TEXCOORD2; };
   TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
   CBUFFER_START(UnityPerMaterial)
   float4 _Color; float4 _MainTex_ST;
   CBUFFER_END
   V vert(A a) { V o; VertexPositionInputs p=GetVertexPositionInputs(a.positionOS.xyz); o.positionCS=p.positionCS; o.depth=p.positionWS.z; o.normal=TransformObjectToWorldNormal(a.normalOS); o.uv=TRANSFORM_TEX(a.uv,_MainTex); return o; }
   half4 frag(V i):SV_Target { half3 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).rgb*_Color.rgb; float light=.62+.38*saturate(dot(normalize(i.normal),normalize(float3(-.4,1,-.6)))); c*=light; c=lerp(c,half3(.025,.12,.16),saturate((i.depth-3)*.035)); return half4(c,1); }
   ENDHLSL
  }
 }
}
