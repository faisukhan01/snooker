// SnookerKit — runtime table construction (Physics module, CONTRACTS §2/§4).
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SnookerKit
{
    /// <summary>Builds the complete snooker table at runtime under a child root named "Table": the "Baize" box
    /// collider (top face exactly at y = 0) with a visible procedural cloth (256×256 deterministic baize texture),
    /// four cushion bodies (exact names "Cushion_N/S/E/W", compound colliders with real pocket mouths so balls can
    /// physically reach the PocketManager sensors), the wooden frame, six dark pocket cylinders at the
    /// PocketManager.Pockets positions, white markings (baulk line, segmented D arc, six spot dots) and the table
    /// lighting rig (two warm spots + one cool fill). Build() is idempotent via a guard — the original build
    /// accidentally ran it twice per scene, duplicating every collider.
    /// Also owns the shared URP/Standard material helpers used across Physics and UI (frozen CONTRACTS §2).</summary>
    [DisallowMultipleComponent]
    public class TableBuilder : MonoBehaviour
    {
        // --- pinned colors --------------------------------------------------------------------------------

        /// <summary>Cloth base color #1F6B47 (per-pixel brightness modulated by deterministic noise).</summary>
        private static readonly Color BaizeColor = new Color(0.121569f, 0.419608f, 0.278431f);
        /// <summary>Cushion cloth color (slightly darker than the bed).</summary>
        private static readonly Color CushionColor = new Color(0.094118f, 0.345098f, 0.250980f);
        /// <summary>Wooden frame color #2B1D12.</summary>
        private static readonly Color WoodColor = new Color(0.168627f, 0.113725f, 0.070588f);
        /// <summary>Pocket cylinder color (near black).</summary>
        private static readonly Color PocketColor = new Color(0.039216f, 0.039216f, 0.039216f);
        /// <summary>Warm table light color #FFF2DC.</summary>
        private static readonly Color WarmLightColor = new Color(1f, 0.949020f, 0.862745f);
        /// <summary>Slightly cool fill light color.</summary>
        private static readonly Color FillLightColor = new Color(0.862745f, 0.909804f, 0.949020f);
        /// <summary>Marking white (baulk line / D / spots).</summary>
        private static readonly Color MarkingColor = new Color(0.92f, 0.94f, 0.92f);

        // --- geometry pins --------------------------------------------------------------------------------

        /// <summary>Baize collider overshoot beyond the playing area per side (covers BedLength×BedWidth + epsilon).</summary>
        private const float BaizeOvershoot = 0.02f;
        /// <summary>Baize slab thickness (m) — the top face sits exactly at y = 0.</summary>
        private const float BaizeThickness = 0.01f;
        /// <summary>Cushion body thickness behind the nose line (m); inward faces stay at ±MaxX / ±MaxZ.</summary>
        private const float CushionThickness = 0.05f;
        /// <summary>Wooden frame height (m).</summary>
        private const float FrameHeight = 0.06f;
        /// <summary>Pocket cylinder visual height (m).</summary>
        private const float PocketVisualHeight = 0.06f;
        /// <summary>Corner pocket visual radius (m).</summary>
        private const float PocketVisualCornerRadius = 0.05f;
        /// <summary>Middle pocket visual radius (m).</summary>
        private const float PocketVisualMiddleRadius = 0.058f;
        /// <summary>Marking lift above the cloth to avoid z-fighting (m).</summary>
        private const float MarkingLift = 0.002f;
        /// <summary>Baulk line / D arc line width (m).</summary>
        private const float MarkingWidth = 0.008f;
        /// <summary>Spot dot diameter (m).</summary>
        private const float SpotDotSize = 0.012f;
        /// <summary>D arc segment count (each segment is one thin quad).</summary>
        private const int DArcSegments = 24;
        /// <summary>Spot light height above the bed (m).</summary>
        private const float LightHeight = 1.5f;

        private GameObject _tableRoot;
        private PhysicMaterial _fallbackMaterial;
        private Material _clothMaterial;
        private Material _cushionVisualMaterial;
        private Material _woodMaterial;
        private Material _pocketMaterial;
        private Material _markingMaterial;
        private bool _built;

        private static Mesh _cubeMesh;

        /// <summary>Registers the builder in the ServiceRegistry (frozen manager convention). The scene root calls
        /// Build() explicitly once the other managers are up.</summary>
        private void Awake()
        {
            ServiceRegistry.Register<TableBuilder>(this);
        }

        /// <summary>Deregisters and releases the runtime materials and the fallback PhysicMaterial.
        /// The built "Table" hierarchy dies with this GameObject.</summary>
        private void OnDestroy()
        {
            ServiceRegistry.Deregister<TableBuilder>();

            if (_fallbackMaterial != null) Destroy(_fallbackMaterial);
            if (_clothMaterial != null) Destroy(_clothMaterial);
            if (_cushionVisualMaterial != null) Destroy(_cushionVisualMaterial);
            if (_woodMaterial != null) Destroy(_woodMaterial);
            if (_pocketMaterial != null) Destroy(_pocketMaterial);
            if (_markingMaterial != null) Destroy(_markingMaterial);

            _fallbackMaterial = null;
            _clothMaterial = null;
            _cushionVisualMaterial = null;
            _woodMaterial = null;
            _pocketMaterial = null;
            _markingMaterial = null;
            _tableRoot = null;
        }

        /// <summary>Builds the whole table exactly once. Subsequent calls are no-ops (the guard fixes the original
        /// bug where Build ran twice per scene and duplicated every collider).</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;

            _tableRoot = new GameObject("Table");
            _tableRoot.transform.SetParent(transform, false);

            _clothMaterial = MakeUrp("Baize_Cloth", Color.white, 0.15f, 0f);
            _cushionVisualMaterial = MakeUrp("Cushion_Cloth", CushionColor, 0.2f, 0f);
            _woodMaterial = MakeUrp("Table_Wood", WoodColor, 0.35f, 0f);
            _pocketMaterial = MakeUrp("Pocket_Dark", PocketColor, 0.4f, 0f);
            _markingMaterial = MakeUrp("Table_Marking", MarkingColor, 0.3f, 0f);

            BuildBaize();
            BuildCushions();
            BuildFrame();
            BuildPocketVisuals();
            BuildMarkings();
            BuildLights();
        }

        // -------------------------------------------------------------------------------------------------
        // Shared material helpers (CONTRACTS §2 — used by BallManager, TrajectoryPredictor, CueController, UI)
        // -------------------------------------------------------------------------------------------------

        /// <summary>Creates a URP/Lit material (falls back to "Standard", then "Sprites/Default") with the pinned
        /// base color, smoothness and metallic values. Never throws; used across Physics and UI modules.</summary>
        public static Material MakeUrp(string name, Color baseColor, float smoothness, float metallic)
        {
            Shader shader = FindShader("Universal Render Pipeline/Lit");
            if (shader == null) shader = FindShader("Standard");
            if (shader == null) shader = FindShader("Sprites/Default");
            if (shader == null) return null; // nothing renderable found — callers must tolerate a null material

            Material mat = new Material(shader);
            mat.name = !string.IsNullOrEmpty(name) ? name : "SnookerMaterial";
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", Mathf.Clamp01(smoothness));
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", Mathf.Clamp01(metallic));
            return mat;
        }

        /// <summary>Creates a transparent unlit material: URP/Unlit forced onto its transparent surface
        /// (SetFloat("_Surface", 1) + alpha blend setup + transparent render queue), falling back to
        /// "Sprites/Default". Used for aim guides and other HUD-in-world overlays. Never throws.</summary>
        public static Material MakeTransparentUnlit(string name, Color color)
        {
            Shader shader = FindShader("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                Material mat = new Material(shader);
                mat.name = !string.IsNullOrEmpty(name) ? name : "SnookerTransparent";
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

                // Force the transparent surface: float + keyword + blend state + queue (all defensive).
                mat.SetFloat("_Surface", 1f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                mat.renderQueue = (int)RenderQueue.Transparent;
                return mat;
            }

            shader = FindShader("Sprites/Default");
            if (shader == null) return null;

            Material fallback = new Material(shader);
            fallback.name = !string.IsNullOrEmpty(name) ? name : "SnookerTransparent";
            if (fallback.HasProperty("_Color")) fallback.SetColor("_Color", color);
            fallback.renderQueue = (int)RenderQueue.Transparent;
            return fallback;
        }

        /// <summary>Shader.Find wrapped defensively (never throws; returns null when not found).</summary>
        private static Shader FindShader(string shaderName)
        {
            try
            {
                return Shader.Find(shaderName);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // -------------------------------------------------------------------------------------------------
        // Table parts
        // -------------------------------------------------------------------------------------------------

        /// <summary>Builds the "Baize" box: collider + visible cloth in one GameObject. Top face exactly at y = 0,
        /// thickness 0.01, extents covering BedLength×BedWidth plus a small overshoot under the cushion noses.</summary>
        private void BuildBaize()
        {
            float len = GameConfig.TableDims.BedLength + 2f * BaizeOvershoot;
            float wid = GameConfig.TableDims.BedWidth + 2f * BaizeOvershoot;

            GameObject baize = new GameObject("Baize");
            baize.transform.SetParent(_tableRoot.transform, false);
            baize.transform.localScale = new Vector3(len, BaizeThickness, wid);
            baize.transform.localPosition = new Vector3(0f, -BaizeThickness * 0.5f, 0f); // top face exactly y = 0

            BoxCollider col = baize.AddComponent<BoxCollider>(); // 1×1×1 collider matches the scaled cube mesh
            col.sharedMaterial = BedPhysic();

            MeshFilter mf = baize.AddComponent<MeshFilter>();
            mf.sharedMesh = CubeMesh();
            MeshRenderer mr = baize.AddComponent<MeshRenderer>();

            Texture2D clothTex = MakeBaizeTexture();
            if (clothTex != null && _clothMaterial != null)
            {
                _clothMaterial.mainTexture = clothTex;
                _clothMaterial.mainTextureScale = new Vector2(
                    GameConfig.TableDims.BedLength * 2f, GameConfig.TableDims.BedWidth * 2f);
            }
            if (mr != null) mr.sharedMaterial = _clothMaterial;
        }

        /// <summary>Procedural 256×256 cloth texture: base #1F6B47 with a deterministic integer-hash noise loop
        /// (pure integer-hash noise, no RNG — identical pixels on every device and every run). Returns null only
        /// if the texture could not be created (cosmetic degradation only).</summary>
        private static Texture2D MakeBaizeTexture()
        {
            try
            {
                const int Size = 256;
                Texture2D tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                tex.name = "BaizeNoise";
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Bilinear;

                Color32[] pixels = new Color32[Size * Size];
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        int h = x * 374761393 + y * 668265263; // deterministic integer hash
                        h = (h ^ (h >> 13)) * 1274126177;
                        h ^= h >> 16;
                        float n = ((h & 0xFFFF) / 32767.5f) - 1f; // [-1, 1]
                        float shade = 1f + n * 0.045f;
                        pixels[y * Size + x] = new Color32(
                            (byte)Mathf.Clamp(BaizeColor.r * shade * 255f, 0f, 255f),
                            (byte)Mathf.Clamp(BaizeColor.g * shade * 255f, 0f, 255f),
                            (byte)Mathf.Clamp(BaizeColor.b * shade * 255f, 0f, 255f),
                            255);
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                return tex;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Builds the four cushions with EXACT names "Cushion_N/S/E/W". Each cushion is one GameObject
        /// with a compound set of BoxColliders so the pinned pocket mouths stay physically open — with solid boxes
        /// a ball centre could never come within the corner sensor radius (0.055) of a corner pocket (min 0.065),
        /// making corner pots impossible. Inward faces remain exactly at ±MaxX / ±MaxZ, height = CushionHeight.</summary>
        private void BuildCushions()
        {
            BuildRail("Cushion_N", GameConfig.TableDims.MaxZ, 1f);
            BuildRail("Cushion_S", -GameConfig.TableDims.MaxZ, -1f);
            BuildEndRail("Cushion_E", GameConfig.TableDims.MaxX, 1f);
            BuildEndRail("Cushion_W", -GameConfig.TableDims.MaxX, -1f);
        }

        /// <summary>One long rail (N/S at Z = ±(MaxZ + half thickness)): two collider segments leaving the middle
        /// pocket gap at x = 0 and the corner setbacks at both ends.</summary>
        private void BuildRail(string name, float railZ, float outward)
        {
            GameObject rail = new GameObject(name);
            rail.transform.SetParent(_tableRoot.transform, false);

            float h = GameConfig.TableDims.CushionHeight;
            float t = CushionThickness;
            float zc = railZ + outward * t * 0.5f;
            float innerX = GameConfig.TableDims.MaxX - GameConfig.TableDims.CornerPocketMouth;
            float halfMiddle = GameConfig.TableDims.MiddlePocketMouth * 0.5f;
            float len = innerX - halfMiddle;
            float cx = (innerX + halfMiddle) * 0.5f;

            AddCushionSegment(rail, new Vector3(-cx, h * 0.5f, zc), new Vector3(len, h, t));
            AddCushionSegment(rail, new Vector3(cx, h * 0.5f, zc), new Vector3(len, h, t));
            MakeBoxVisual(rail.transform, name + "_Visual_A", new Vector3(-cx, h * 0.5f, zc), new Vector3(len, h, t), _cushionVisualMaterial);
            MakeBoxVisual(rail.transform, name + "_Visual_B", new Vector3(cx, h * 0.5f, zc), new Vector3(len, h, t), _cushionVisualMaterial);
        }

        /// <summary>One end rail (E/W at X = ±(MaxX + half thickness)): a single collider segment with corner
        /// setbacks at both ends (no middle pocket on the ends).</summary>
        private void BuildEndRail(string name, float railX, float outward)
        {
            GameObject rail = new GameObject(name);
            rail.transform.SetParent(_tableRoot.transform, false);

            float h = GameConfig.TableDims.CushionHeight;
            float t = CushionThickness;
            float xc = railX + outward * t * 0.5f;
            float innerZ = GameConfig.TableDims.MaxZ - GameConfig.TableDims.CornerPocketMouth;

            AddCushionSegment(rail, new Vector3(xc, h * 0.5f, 0f), new Vector3(t, h, innerZ * 2f));
            MakeBoxVisual(rail.transform, name + "_Visual_A", new Vector3(xc, h * 0.5f, 0f), new Vector3(t, h, innerZ * 2f), _cushionVisualMaterial);
        }

        /// <summary>Adds one cushion collider segment to a cushion GameObject (exact "Cushion_*" name → Ball.cs
        /// cushion audio still matches).</summary>
        private void AddCushionSegment(GameObject rail, Vector3 centre, Vector3 size)
        {
            BoxCollider col = rail.AddComponent<BoxCollider>();
            col.center = centre;
            col.size = size;
            col.sharedMaterial = CushionPhysic();
        }

        /// <summary>Builds the four wooden frame boxes (#2B1D12, WoodFrame width) with colliders so a missed pot
        /// bounces back into the pocket mouth instead of leaving the world.</summary>
        private void BuildFrame()
        {
            float t = CushionThickness;
            float fw = GameConfig.TableDims.WoodFrame;
            float fh = FrameHeight;
            float lenX = GameConfig.TableDims.BedLength + 2f * t + 2f * fw;
            float lenZ = GameConfig.TableDims.BedWidth + 2f * t;
            float y = fh * 0.5f - 0.005f;

            MakeFrameBox("Frame_N", new Vector3(0f, y, GameConfig.TableDims.MaxZ + t + fw * 0.5f), new Vector3(lenX, fh, fw));
            MakeFrameBox("Frame_S", new Vector3(0f, y, -(GameConfig.TableDims.MaxZ + t + fw * 0.5f)), new Vector3(lenX, fh, fw));
            MakeFrameBox("Frame_E", new Vector3(GameConfig.TableDims.MaxX + t + fw * 0.5f, y, 0f), new Vector3(fw, fh, lenZ));
            MakeFrameBox("Frame_W", new Vector3(-(GameConfig.TableDims.MaxX + t + fw * 0.5f), y, 0f), new Vector3(fw, fh, lenZ));
        }

        /// <summary>One frame box: mesh + collider + wood material.</summary>
        private void MakeFrameBox(string name, Vector3 centre, Vector3 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(_tableRoot.transform, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;

            BoxCollider col = go.AddComponent<BoxCollider>();
            col.sharedMaterial = CushionPhysic();

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = CubeMesh();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = _woodMaterial;
        }

        /// <summary>Six dark pocket cylinder visuals (collider-free) at the PocketManager.Pockets positions.</summary>
        private void BuildPocketVisuals()
        {
            Vector3[] pockets = PocketManager.Pockets;
            if (pockets == null) return;

            for (int i = 0; i < pockets.Length; i++)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(go.GetComponent<Collider>()); // visual only — sensors come from PocketManager
                go.name = "PocketVisual_" + i;
                go.transform.SetParent(_tableRoot.transform, false);

                float radius = i < 4 ? PocketVisualCornerRadius : PocketVisualMiddleRadius;
                go.transform.localScale = new Vector3(radius * 2f, PocketVisualHeight, radius * 2f);
                go.transform.localPosition = new Vector3(pockets[i].x, PocketVisualHeight * 0.5f - 0.005f, pockets[i].z);

                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = _pocketMaterial;
            }
        }

        /// <summary>White markings as thin quads: the baulk line, the segmented D arc (bulging toward the baulk
        /// cushion) and the six spot dots (black, pink, blue, brown, green, yellow).</summary>
        private void BuildMarkings()
        {
            float baulkX = GameConfig.TableDims.BaulkLineX;
            float radius = GameConfig.TableDims.DRadius;
            float y = MarkingLift;

            MakeMarkingQuad("Mark_BaulkLine", new Vector3(baulkX, y, 0f), 0f, MarkingWidth, GameConfig.TableDims.BedWidth);

            Vector2 centre = new Vector2(baulkX, 0f);
            Vector2 prev = ArcPoint(centre, radius, -Mathf.PI * 0.5f);
            for (int i = 1; i <= DArcSegments; i++)
            {
                float theta = -Mathf.PI * 0.5f + (Mathf.PI * i) / DArcSegments;
                Vector2 next = ArcPoint(centre, radius, theta);
                MakeMarkingSegment("Mark_D_" + i, prev, next);
                prev = next;
            }

            MakeMarkingQuad("Mark_Spot_Black", new Vector3(GameConfig.TableDims.BlackX, y, 0f), 0f, SpotDotSize, SpotDotSize);
            MakeMarkingQuad("Mark_Spot_Pink", new Vector3(GameConfig.TableDims.PinkX, y, 0f), 0f, SpotDotSize, SpotDotSize);
            MakeMarkingQuad("Mark_Spot_Blue", new Vector3(0f, y, 0f), 0f, SpotDotSize, SpotDotSize);
            MakeMarkingQuad("Mark_Spot_Brown", new Vector3(baulkX, y, 0f), 0f, SpotDotSize, SpotDotSize);
            MakeMarkingQuad("Mark_Spot_Green", new Vector3(baulkX, y, -GameConfig.TableDims.BaulkLineZ), 0f, SpotDotSize, SpotDotSize);
            MakeMarkingQuad("Mark_Spot_Yellow", new Vector3(baulkX, y, GameConfig.TableDims.BaulkLineZ), 0f, SpotDotSize, SpotDotSize);
        }

        /// <summary>Point on the D arc: θ = 0 is the apex toward −X (baulk cushion side), θ = ±π/2 the baulk line ends.</summary>
        private static Vector2 ArcPoint(Vector2 centre, float radius, float theta)
        {
            return new Vector2(centre.x - radius * Mathf.Cos(theta), centre.y + radius * Mathf.Sin(theta));
        }

        /// <summary>One D arc segment: a thin quad stretched along the chord between two arc points.</summary>
        private void MakeMarkingSegment(string name, Vector2 a, Vector2 b)
        {
            float dx = b.x - a.x;
            float dz = b.y - a.y;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 1e-5f) return;

            float yaw = Mathf.Atan2(-dz, dx) * Mathf.Rad2Deg;
            MakeMarkingQuad(name, new Vector3((a.x + b.x) * 0.5f, MarkingLift, (a.y + b.y) * 0.5f), yaw,
                len + MarkingWidth, MarkingWidth);
        }

        /// <summary>One flat white marking quad (faces up, optionally yawed; collider-free, no shadow casting).</summary>
        private void MakeMarkingQuad(string name, Vector3 position, float yawDegrees, float sizeX, float sizeZ)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(_tableRoot.transform, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(-90f, yawDegrees, 0f); // normal faces +Y (visible from above)
            go.transform.localScale = new Vector3(sizeX, sizeZ, 1f);

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = _markingMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>Lighting rig under the Table root: two warm spotlights above the bed plus one slightly cool
        /// directional fill.</summary>
        private void BuildLights()
        {
            MakeSpotLight("TableLight_A", new Vector3(-GameConfig.TableDims.MaxX * 0.5f, LightHeight, 0f));
            MakeSpotLight("TableLight_B", new Vector3(GameConfig.TableDims.MaxX * 0.5f, LightHeight, 0f));

            GameObject fillGo = new GameObject("FillLight");
            fillGo.transform.SetParent(_tableRoot.transform, false);
            fillGo.transform.localRotation = Quaternion.Euler(38f, 35f, 0f);
            Light fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = FillLightColor;
            fill.intensity = 0.35f;
            fill.shadows = LightShadows.None;
        }

        /// <summary>One warm downward spotlight (#FFF2DC, intensity 1.15, range 8, angle 70, soft shadows).</summary>
        private void MakeSpotLight(string name, Vector3 localPosition)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(_tableRoot.transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // straight down

            Light light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = WarmLightColor;
            light.intensity = 1.15f;
            light.range = 8f;
            light.spotAngle = 70f;
            light.shadows = LightShadows.Soft;
        }

        // -------------------------------------------------------------------------------------------------
        // Shared resources
        // -------------------------------------------------------------------------------------------------

        /// <summary>Bed PhysicMaterial from BallManager via TryGet, else a defensive 0/0 fallback.</summary>
        private PhysicMaterial BedPhysic()
        {
            if (ServiceRegistry.TryGet<BallManager>(out BallManager bm) && bm != null && bm.BedMaterial != null)
            {
                return bm.BedMaterial;
            }
            return ZeroFallback();
        }

        /// <summary>Cushion PhysicMaterial from BallManager via TryGet, else a defensive 0/0 fallback.</summary>
        private PhysicMaterial CushionPhysic()
        {
            if (ServiceRegistry.TryGet<BallManager>(out BallManager bm) && bm != null && bm.CushionMaterial != null)
            {
                return bm.CushionMaterial;
            }
            return ZeroFallback();
        }

        /// <summary>Friction-less / bounce-less fallback material (created once, only when BallManager is absent).</summary>
        private PhysicMaterial ZeroFallback()
        {
            if (_fallbackMaterial == null)
            {
                _fallbackMaterial = new PhysicMaterial("TableFallbackZero");
                _fallbackMaterial.bounciness = 0f;
                _fallbackMaterial.dynamicFriction = 0f;
                _fallbackMaterial.staticFriction = 0f;
                _fallbackMaterial.frictionCombine = PhysicMaterialCombine.Minimum;
                _fallbackMaterial.bounceCombine = PhysicMaterialCombine.Minimum;
            }
            return _fallbackMaterial;
        }

        /// <summary>The built-in cube mesh (fetched once via a throwaway primitive — shared mesh survives).</summary>
        private static Mesh CubeMesh()
        {
            if (_cubeMesh == null)
            {
                GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                MeshFilter mf = tmp.GetComponent<MeshFilter>();
                if (mf != null) _cubeMesh = mf.sharedMesh;
                Destroy(tmp);
            }
            return _cubeMesh;
        }

        /// <summary>One unlit visual box (no collider): shared cube mesh scaled/positioned in table space.</summary>
        private static void MakeBoxVisual(Transform parent, string name, Vector3 centre, Vector3 size, Material material)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = CubeMesh();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = material;
        }
    }
}
