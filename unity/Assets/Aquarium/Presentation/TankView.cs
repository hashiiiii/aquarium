using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aquarium.Presentation
{
    [Serializable]
    public struct CreatureVisual
    {
        public string id;
        public int speciesIndex;
        public float growth;
        public bool selected;
        public CreatureVisual(string id, int speciesIndex, float growth, bool selected = false)
        { this.id = id; this.speciesIndex = speciesIndex; this.growth = growth; this.selected = selected; }
    }

    /// <summary>Original procedural HD-2D reef. No downloaded art or scene dependencies.</summary>
    public sealed class TankView : MonoBehaviour
    {
        sealed class Swimmer
        {
            public CreatureVisual data;
            public Transform root, body, halo;
            public Vector3 target;
            public float phase, timer, facing = 1;
        }
        struct Bubble { public Transform transform; public float speed, phase, bottom; }
        readonly Dictionary<string, Swimmer> swimmers = new Dictionary<string, Swimmer>();
        readonly List<string> removed = new List<string>();
        readonly HashSet<string> present = new HashSet<string>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly List<Bubble> bubbles = new List<Bubble>();
        readonly List<Transform> weeds = new List<Transform>();
        readonly List<Transform> food = new List<Transform>();
        readonly Material[] creatureMaterials = new Material[3];
        Camera tankCamera;
        VolumeProfile atmosphereProfile;
        Material solid, transparent, bubbleMaterial, haloMaterial, foodMaterial;
        System.Random random;
        bool initialized;
        float feedTimer;
        public Action<string> CreatureSelected;
        public Camera TankCamera => tankCamera;

        public void Initialize()
        {
            if (initialized) return;
            random = new System.Random(7319);
            var solidShader = Resources.Load<Shader>("ReefSolid");
            var spriteShader = Resources.Load<Shader>("ReefSprite");
            if (solidShader == null || spriteShader == null)
                throw new InvalidOperationException("Aquarium presentation shaders are missing from Resources.");
            solid = Own(new Material(solidShader));
            transparent = Own(new Material(spriteShader));
            initialized = true;
            var camObject = new GameObject("Reef Camera");
            camObject.transform.SetParent(transform, false);
            tankCamera = camObject.AddComponent<Camera>();
            tankCamera.transform.localPosition = new Vector3(0, 5.8f, -16.5f);
            tankCamera.transform.LookAt(transform.TransformPoint(new Vector3(0, 2.65f, 1.5f)));
            tankCamera.clearFlags = CameraClearFlags.SolidColor;
            tankCamera.backgroundColor = Hex(0x061f2b);
            tankCamera.fieldOfView = 37;
            tankCamera.nearClipPlane = .1f;
            tankCamera.farClipPlane = 70;
            tankCamera.depth = 0;
            tankCamera.allowHDR = true;
            tankCamera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var volumeObject = new GameObject("Reef atmosphere");
            volumeObject.transform.SetParent(transform, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            var profile = Own(ScriptableObject.CreateInstance<VolumeProfile>());
            volume.sharedProfile = profile;
            atmosphereProfile = profile;
            var bloom = Own(profile.Add<Bloom>(true));
            bloom.threshold.Override(.95f); bloom.intensity.Override(.22f); bloom.scatter.Override(.5f);
            var vignette = Own(profile.Add<Vignette>(true));
            vignette.intensity.Override(.19f); vignette.smoothness.Override(.5f);
            var grade = Own(profile.Add<ColorAdjustments>(true));
            grade.contrast.Override(8); grade.saturation.Override(8);
            // Geometry has its own art-directed shading, so it also works without pipeline lighting settings.
            BuildEnvironment();
            var colors = new[] { Hex(0x8be5d4), Hex(0xc9abef), Hex(0xecb779) };
            for (int i = 0; i < creatureMaterials.Length; i++)
            {
                creatureMaterials[i] = Own(new Material(transparent));
                creatureMaterials[i].mainTexture = DrawCreature(i, colors[i]);
                creatureMaterials[i].renderQueue = 3010;
            }
            bubbleMaterial = Own(new Material(transparent));
            bubbleMaterial.mainTexture = DrawRing(false);
            bubbleMaterial.color = new Color(.48f, .9f, 1, .42f);
            haloMaterial = Own(new Material(transparent));
            haloMaterial.mainTexture = DrawRing(true);
            haloMaterial.color = new Color(1, .8f, .4f, .9f);
            foodMaterial = Own(new Material(solid));
            foodMaterial.color = Hex(0xffd08a);
            for (int i = 0; i < 36; i++)
            {
                var b = Quad("Drifting bubble", bubbleMaterial, new Vector3(Range(-7, 7), Range(.1f, 8), Range(-1, 7)), Vector3.one * Range(.025f, .085f));
                bubbles.Add(new Bubble { transform = b, speed = Range(.15f, .45f), phase = Range(0, 6), bottom = Range(-.1f, .3f) });
            }
            for (int i = 0; i < 18; i++)
            {
                var pellet = Primitive("Feed mote", PrimitiveType.Sphere, foodMaterial, Vector3.zero, Vector3.one * .055f);
                pellet.gameObject.SetActive(false);
                food.Add(pellet);
            }
        }

        public void SyncCreatures(IReadOnlyList<CreatureVisual> creatures)
        {
            Initialize();
            present.Clear();
            if (creatures != null) for (int i = 0; i < creatures.Count; i++)
            {
                var data = creatures[i];
                if (string.IsNullOrEmpty(data.id)) continue;
                present.Add(data.id);
                if (!swimmers.TryGetValue(data.id, out var swimmer))
                {
                    var root = new GameObject("Creature " + data.id).transform;
                    root.SetParent(transform, false);
                    root.localPosition = new Vector3(Range(-4.5f, 4.5f), Range(1.2f, 4.4f), Range(-.4f, 3.4f));
                    swimmer = new Swimmer { root = root, phase = Range(0, 6), timer = Range(1, 5), target = root.localPosition };
                    swimmer.body = Quad("Pixel creature", creatureMaterials[Wrap(data.speciesIndex)], Vector3.zero, Vector3.one);
                    swimmer.body.SetParent(root, false);
                    swimmer.halo = Quad("Selection glow", haloMaterial, Vector3.zero, Vector3.one * 1.6f);
                    swimmer.halo.SetParent(root, false);
                    swimmer.halo.localPosition = new Vector3(0, 0, .025f);
                    swimmers.Add(data.id, swimmer);
                }
                swimmer.data = data;
                swimmer.body.GetComponent<MeshRenderer>().sharedMaterial = creatureMaterials[Wrap(data.speciesIndex)];
                swimmer.halo.gameObject.SetActive(data.selected);
            }
            removed.Clear();
            foreach (var pair in swimmers) if (!present.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (var id in removed) { Destroy(swimmers[id].root.gameObject); swimmers.Remove(id); }
        }

        /// <summary>Call with a screen-space pointer after filtering UI hits; supports either input backend.</summary>
        public string PickCreature(Vector2 screenPoint)
        {
            if (!initialized) return null;
            string hit = null;
            float nearest = float.PositiveInfinity;
            foreach (var pair in swimmers)
            {
                var s = pair.Value;
                Vector3 point = tankCamera.WorldToScreenPoint(s.body.position);
                Vector3 edge = tankCamera.WorldToScreenPoint(s.body.position + tankCamera.transform.right * s.body.lossyScale.x * .47f);
                float radius = Mathf.Max(22, Mathf.Abs(edge.x - point.x));
                if (point.z > 0 && Vector2.Distance(screenPoint, point) < radius && point.z < nearest)
                { hit = pair.Key; nearest = point.z; }
            }
            if (hit != null) CreatureSelected?.Invoke(hit);
            return hit;
        }

        public void PlayFeedEffect()
        {
            Initialize();
            feedTimer = 5;
            for (int i = 0; i < food.Count; i++)
            {
                food[i].localPosition = new Vector3(Range(-3.5f, 3.5f), Range(5, 6), Range(-.2f, 2.8f));
                food[i].gameObject.SetActive(true);
            }
            foreach (var pair in swimmers)
            {
                pair.Value.target = new Vector3(Range(-3, 3), Range(3, 4.8f), Range(0, 2));
                pair.Value.timer = 5;
            }
        }

        void Update()
        {
            if (!initialized) return;
            float t = Time.time, dt = Mathf.Min(Time.deltaTime, .1f);
            foreach (var pair in swimmers)
            {
                var s = pair.Value;
                s.timer -= dt;
                if (s.timer <= 0)
                {
                    s.target = new Vector3(Range(-4.8f, 4.8f), Range(1, 4.6f), Range(-.5f, 3.6f));
                    s.timer = Range(4, 10);
                }
                Vector3 delta = s.target - s.root.localPosition;
                s.root.localPosition = Vector3.MoveTowards(s.root.localPosition, s.target, dt * (.25f + .12f * Wrap(s.data.speciesIndex)));
                if (Mathf.Abs(delta.x) > .08f) s.facing = Mathf.Sign(delta.x);
                s.root.rotation = tankCamera.transform.rotation;
                s.body.localPosition = new Vector3(0, Mathf.Sin(t * 1.8f + s.phase) * .055f, 0);
                float size = Mathf.Lerp(.66f, 1.18f, Mathf.Clamp01(s.data.growth));
                float breathe = Mathf.Sin(t * 2.8f + s.phase) * .025f;
                s.body.localScale = new Vector3(size * s.facing * (1 + breathe), size * (1 - breathe), 1);
                s.halo.localScale = Vector3.one * size * (1.4f + Mathf.Sin(t * 2) * .04f);
            }
            foreach (var b in bubbles)
            {
                var p = b.transform.localPosition;
                p.y += dt * b.speed;
                p.x += Mathf.Sin(t * .7f + b.phase) * dt * .035f;
                if (p.y > 8) p.y = b.bottom;
                b.transform.localPosition = p;
                b.transform.rotation = tankCamera.transform.rotation;
            }
            for (int i = 0; i < weeds.Count; i++) weeds[i].localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * .8f + i) * 5);
            if (feedTimer > 0)
            {
                feedTimer -= dt;
                foreach (var pellet in food)
                {
                    pellet.localPosition += Vector3.down * dt * .7f;
                    if (feedTimer <= 0) pellet.gameObject.SetActive(false);
                }
            }
        }

        void BuildEnvironment()
        {
            var sand = Mat(Hex(0x587e79));
            var sandTexture = Own(new Texture2D(64, 64, TextureFormat.RGBA32, false));
            sandTexture.filterMode = FilterMode.Point;
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            { float n = Range(.77f, 1); sandTexture.SetPixel(x, y, new Color(n, n, n)); }
            sandTexture.Apply(false, true); sand.mainTexture = sandTexture; sand.mainTextureScale = new Vector2(12, 8);
            Primitive("Sloping sand bed", PrimitiveType.Cube, sand, new Vector3(0, -.4f, 4), new Vector3(25, .8f, 19));
            var distant = Mat(Hex(0x103a45));
            var middle = Mat(Hex(0x175461));
            var stone = Mat(Hex(0x477a7a));
            var paleStone = Mat(Hex(0x60978d));
            for (int i = 0; i < 17; i++)
            {
                float x = -17 + i * 2.1f;
                Primitive("Far reef silhouette", PrimitiveType.Sphere, distant, new Vector3(x, Range(.3f, 1.3f), 13), new Vector3(Range(2, 5), Range(2, 5), 2));
                Primitive("Midwater basalt", PrimitiveType.Cube, middle, new Vector3(x, Range(.5f, 1.2f), 9), new Vector3(Range(1, 2), Range(1, 4), 1.4f));
            }
            // Broken arch frames, capital stones and stepped bases create strong diorama silhouettes.
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 5.7f;
                Primitive("Ruins plinth", PrimitiveType.Cube, stone, new Vector3(x, .18f, 5), new Vector3(3.4f, .36f, 1.7f));
                for (int pillar = -1; pillar <= 1; pillar += 2)
                {
                    float height = side < 0 || pillar > 0 ? 3.3f : 2.1f;
                    Primitive("Ancient column", PrimitiveType.Cylinder, stone, new Vector3(x + pillar * 1.05f, height / 2, 5), new Vector3(.58f, height / 2, .58f));
                    Primitive("Column capital", PrimitiveType.Cube, paleStone, new Vector3(x + pillar * 1.05f, height, 5), new Vector3(.9f, .24f, .9f));
                }
                if (side < 0) Primitive("Ancient lintel", PrimitiveType.Cube, stone, new Vector3(x, 3.55f, 5), new Vector3(3.2f, .48f, 1));
            }
            var wood = Mat(Hex(0x426064));
            for (int i = 0; i < 7; i++)
            {
                var plank = Primitive("Sunken hull rib", PrimitiveType.Cube, wood, new Vector3(-1.9f + i * .48f, .22f, 6.2f), new Vector3(.18f, .65f + Mathf.Sin(i * .45f) * .4f, 1.5f));
                plank.localRotation = Quaternion.Euler(0, 0, -14 + i * 4);
            }
            var coralColors = new[] { Hex(0xc97e89), Hex(0xd1a45f), Hex(0x826db0), Hex(0x59aba6) };
            for (int i = 0; i < 25; i++)
            {
                float x = Range(-8, 8), z = Range(-1, 7);
                if (Mathf.Abs(x) < 3.8f && z < 3) x += Mathf.Sign(x + .001f) * 3.6f;
                Primitive("Reef pebble", PrimitiveType.Sphere, stone, new Vector3(x, .1f, z), new Vector3(Range(.4f, 1.3f), Range(.2f, .6f), Range(.4f, 1)));
                var coral = Mat(coralColors[i % coralColors.Length]);
                for (int j = 0; j < 4; j++)
                {
                    float h = Range(.25f, 1);
                    var stem = Primitive("Coral finger", PrimitiveType.Capsule, coral, new Vector3(x + Range(-.32f, .32f), h * .5f, z + Range(-.2f, .2f)), new Vector3(.13f, h * .5f, .13f));
                    stem.localRotation = Quaternion.Euler(Range(-20, 20), 0, Range(-28, 28));
                }
            }
            var kelp = Mat(Hex(0x36786b));
            for (int i = 0; i < 22; i++)
            {
                float x = Range(-9, 9), z = Range(2, 9);
                var stem = new GameObject("Swaying kelp").transform;
                stem.SetParent(transform, false); stem.localPosition = new Vector3(x, 0, z); weeds.Add(stem);
                int count = random.Next(3, 7);
                for (int j = 0; j < count; j++)
                {
                    var leaf = Primitive("Kelp frond", PrimitiveType.Sphere, kelp, Vector3.zero, new Vector3(.22f, .44f, .08f));
                    leaf.SetParent(stem, false); leaf.localPosition = new Vector3(Mathf.Sin(j * .8f) * .13f, .3f + j * .45f, 0);
                    leaf.localRotation = Quaternion.Euler(0, 0, (j % 2 == 0 ? 1 : -1) * 20);
                }
            }
            var shaft = Own(new Material(transparent));
            shaft.mainTexture = DrawLight(); shaft.color = new Color(.52f, .9f, .87f, .11f); shaft.renderQueue = 2990;
            for (int i = 0; i < 5; i++)
            {
                var ray = Quad("Soft surface light", shaft, new Vector3(-6 + i * 3.3f, 4.2f, 7.4f), new Vector3(2.3f, 11, 1));
                ray.localRotation = Quaternion.Euler(0, 0, -19);
            }
        }

        Texture2D DrawCreature(int species, Color color)
        {
            const int n = 32;
            var tex = Own(new Texture2D(n, n, TextureFormat.RGBA32, false));
            tex.name = "Original pixel reef creature " + species; tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            var mask = new bool[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = x - 17, dy = y - 16;
                bool body, tail, fin;
                if (species == 1) // Moon Jelly: domed bell and four separate trailing tendrils, no fish tail.
                {
                    body = (x - 16) * (x - 16) / 100f + (y - 19) * (y - 19) / 64f < 1 && y >= 17;
                    tail = y >= 5 && y < 18 &&
                        (x == 10 + (y / 3 % 2) || x == 14 - (y / 4 % 2) ||
                         x == 19 + (y / 4 % 2) || x == 23 - (y / 3 % 2));
                    fin = y == 16 && x >= 8 && x <= 24;
                }
                else if (species == 2) // Coral Drake: long curling tail, upright neck, snout, horn and dorsal spines.
                {
                    body = (x - 16) * (x - 16) / 49f + (y - 14) * (y - 14) / 25f < 1 ||
                        (x >= 20 && x <= 24 && y >= 13 && y <= 23) ||
                        (x >= 22 && x <= 28 && y >= 20 && y <= 23);
                    tail = x >= 3 && x <= 14 && Mathf.Abs(y - (10 + .06f * (x - 5) * (x - 5))) <= 1.7f ||
                        x >= 3 && x <= 6 && y >= 10 && y <= 15;
                    fin = (x >= 20 && x <= 21 && y >= 23 && y <= 28) ||
                        (x >= 11 && x <= 19 && y >= 17 && y <= 22 && y <= 23 - x % 3) ||
                        (x >= 18 && x <= 20 && y >= 7 && y <= 12);
                }
                else // Tide Sprite: rounded luminous body, petal tail and crest.
                {
                    body = dx * dx / 81 + dy * dy / 49 < 1;
                    tail = x >= 3 && x <= 10 && Mathf.Abs(y - 16) <= (11 - x) * .8f;
                    fin = y >= 20 && y <= 26 && x >= 12 && x <= 20 && y < 28 - Mathf.Abs(x - 16);
                }
                mask[y * n + x] = body || tail || fin;
            }
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                Color c = Color.clear;
                if (mask[y * n + x])
                {
                    bool edge = x == 0 || y == 0 || x == n-1 || y == n-1 || !mask[y*n+Mathf.Max(0,x-1)] || !mask[y*n+Mathf.Min(n-1,x+1)] || !mask[Mathf.Max(0,y-1)*n+x] || !mask[Mathf.Min(n-1,y+1)*n+x];
                    c = edge ? Color.Lerp(color, Hex(0x123645), .68f) : color * (y < 15 ? .72f : y > 19 ? 1.12f : .94f);
                    c.a = 1;
                    if (x > 14 && x < 23 && y > 12 && y < 15) c = Color.Lerp(color, Color.white, .4f);
                    if (species == 1)
                    {
                        if ((x == 13 || x == 19) && y >= 19 && y <= 20) c = Hex(0x23304c);
                        if (y >= 24 && x >= 12 && x <= 19) c = Hex(0xeee3ff);
                    }
                    else
                    {
                        int eyeY = species == 2 ? 22 : 18;
                        if (x >= 23 && x <= 24 && y >= eyeY - 1 && y <= eyeY) c = Hex(0x112531);
                        if (x == 24 && y == eyeY) c = Color.white;
                        if (species == 2 && x > 12 && x < 20 && y > 12 && y < 17 && (x + y) % 4 == 0) c = Hex(0xfbe5a5);
                    }
                }
                tex.SetPixel(x, y, c);
            }
            tex.Apply(false, true); return tex;
        }

        Texture2D DrawRing(bool glow)
        {
            var tex = Own(new Texture2D(32, 32, TextureFormat.RGBA32, false)); tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            { float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 15.5f; float a = glow ? Mathf.Clamp01(1 - Mathf.Abs(d - .78f) * 9) : (d > .68f && d < .85f ? .8f : 0); tex.SetPixel(x, y, new Color(1, 1, 1, a)); }
            tex.Apply(false, true); return tex;
        }
        Texture2D DrawLight()
        {
            var tex = Own(new Texture2D(32, 32, TextureFormat.RGBA32, false)); tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            { float edge = 1 - Mathf.Abs(x - 15.5f) / 15.5f; float height = Mathf.Sin(y / 31f * Mathf.PI); tex.SetPixel(x, y, new Color(1, 1, 1, edge * edge * height)); }
            tex.Apply(false, true); return tex;
        }
        Material Mat(Color color) { var m = Own(new Material(solid)); m.color = color; return m; }
        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        Transform Primitive(string label, PrimitiveType type, Material material, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type); go.name = label; go.transform.SetParent(transform, false);
            go.transform.localPosition = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            var collider = go.GetComponent<Collider>(); if (collider) { collider.enabled = false; Destroy(collider); }
            return go.transform;
        }
        Transform Quad(string label, Material material, Vector3 position, Vector3 scale) => Primitive(label, PrimitiveType.Quad, material, position, scale);
        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
        static int Wrap(int i) => Mathf.Clamp(i, 0, 2);
        static Color Hex(uint rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        void OnDestroy()
        {
            // Components are individually owned above. Detach them first so VolumeProfile's
            // OnDisable cannot destroy the same component again when the profile is destroyed.
            if (atmosphereProfile) atmosphereProfile.components.Clear();
            foreach (var item in owned) if (item) Destroy(item);
            owned.Clear();
        }
    }
}
