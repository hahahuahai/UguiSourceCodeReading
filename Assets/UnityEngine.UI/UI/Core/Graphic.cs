using System;
#if UNITY_EDITOR
using System.Reflection;
#endif
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI.CoroutineTween;

namespace UnityEngine.UI
{
    /// <summary>
    /// Base class for all visual UI Component.When creating visual UI components you should inherit from this class. 
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    [RequireComponent(typeof(RectTransform))]
    [ExecuteInEditMode]
    public abstract class Graphic
        : UIBehaviour,//UIBehaviour是所有UI组件的抽象基类，提供了接收UnityEngine或UnityEditor的事件的接口
          ICanvasElement//只有实现了ICanvasElement接口的类才可以通过CanvasUpdateRegistry 注册更新事件
    {
        static protected Material s_DefaultUI = null;
        static protected Texture2D s_WhiteTexture = null;

        /// <summary>
        /// Default material used to draw everything if no explicit material was specified.
        /// </summary>
        static public Material defaultGraphicMaterial
        {
            get
            {
                if (s_DefaultUI == null)
                    s_DefaultUI = Canvas.GetDefaultCanvasMaterial();
                return s_DefaultUI;
            }
        }

        // Cached and saved values
        [FormerlySerializedAs("m_Mat")]
        [SerializeField] protected Material m_Material;

        [SerializeField] private Color m_Color = Color.white;
        /// <summary>
        /// 	Base color of the Graphic.
        /// </summary>
        public virtual Color color { get { return m_Color; } set { if (SetPropertyUtility.SetColor(ref m_Color, value)) SetVerticesDirty(); } }

        [SerializeField] private bool m_RaycastTarget = true;
        /// <summary>
        /// Should this graphic be considered a target for raycasting?
        /// </summary>
        public virtual bool raycastTarget { get { return m_RaycastTarget; } set { m_RaycastTarget = value; } }

        [NonSerialized] private RectTransform m_RectTransform;
        [NonSerialized] private CanvasRenderer m_CanvasRender;
        [NonSerialized] private Canvas m_Canvas;

        [NonSerialized] private bool m_VertsDirty;
        [NonSerialized] private bool m_MaterialDirty;

        [NonSerialized] protected UnityAction m_OnDirtyLayoutCallback;
        [NonSerialized] protected UnityAction m_OnDirtyVertsCallback;
        [NonSerialized] protected UnityAction m_OnDirtyMaterialCallback;

        [NonSerialized] protected static Mesh s_Mesh;
        [NonSerialized] private static readonly VertexHelper s_VertexHelper = new VertexHelper();

        // Tween controls for the Graphic
        [NonSerialized]
        private readonly TweenRunner<ColorTween> m_ColorTweenRunner;

        protected bool useLegacyMeshGeneration { get; set; }

        // Called by Unity prior to deserialization,
        // should not be called by users
        protected Graphic()
        {
            if (m_ColorTweenRunner == null)
                m_ColorTweenRunner = new TweenRunner<ColorTween>();
            m_ColorTweenRunner.Init(this);
            useLegacyMeshGeneration = true;
        }

        /// <summary>
        /// Mark the Graphic as dirty.
        /// </summary>
        public virtual void SetAllDirty()
        {
            SetLayoutDirty();
            SetVerticesDirty();
            SetMaterialDirty();
        }
        /// <summary>
        /// Mark the layout as dirty.
        /// </summary>
        public virtual void SetLayoutDirty()
        {
            if (!IsActive())
                return;

            LayoutRebuilder.MarkLayoutForRebuild(rectTransform);

            if (m_OnDirtyLayoutCallback != null)
                m_OnDirtyLayoutCallback();
        }
        /// <summary>
        /// Mark the vertices as dirty.
        /// </summary>
        public virtual void SetVerticesDirty()
        {
            if (!IsActive())
                return;

            m_VertsDirty = true;
            CanvasUpdateRegistry.RegisterCanvasElementForGraphicRebuild(this);

            if (m_OnDirtyVertsCallback != null)
                m_OnDirtyVertsCallback();
        }
        /// <summary>
        /// Mark the Material as dirty.
        /// </summary>
        public virtual void SetMaterialDirty()
        {
            if (!IsActive())
                return;

            m_MaterialDirty = true;
            CanvasUpdateRegistry.RegisterCanvasElementForGraphicRebuild(this);

            if (m_OnDirtyMaterialCallback != null)
                m_OnDirtyMaterialCallback();
        }
        /// <summary>
        /// This callback is called if an associated RectTransform has its dimensions changed. The call is also made to all child rect transforms, even if the child transform itself doesn't change - as it could have, depending on its anchoring.
        /// </summary>
        protected override void OnRectTransformDimensionsChange()
        {
            if (gameObject.activeInHierarchy)
            {
                // prevent double dirtying...
                if (CanvasUpdateRegistry.IsRebuildingLayout())
                    SetVerticesDirty();
                else
                {
                    SetVerticesDirty();
                    SetLayoutDirty();
                }
            }
        }
        /// <summary>
        /// See MonoBehaviour.OnBeforeTransformParentChanged.
        /// </summary>
        protected override void OnBeforeTransformParentChanged()
        {
            GraphicRegistry.UnregisterGraphicForCanvas(canvas, this);
            LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
        }
        /// <summary>
        /// See MonoBehaviour.OnRectTransformParentChanged.
        /// </summary>
        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();

            m_Canvas = null;

            if (!IsActive())
                return;

            CacheCanvas();
            GraphicRegistry.RegisterGraphicForCanvas(canvas, this);
            SetAllDirty();
        }

        /// <summary>
        /// Absolute depth of the graphic, used by rendering and events -- lowest to highest.
        /// </summary>
        public int depth { get { return canvasRenderer.absoluteDepth; } }

        /// <summary>
        /// Transform gets cached for speed.	The RectTransform component used by the Graphic.
        /// </summary>
        public RectTransform rectTransform
        {
            get { return m_RectTransform ?? (m_RectTransform = GetComponent<RectTransform>()); }
        }
        /// <summary>
        /// A reference to the Canvas this Graphic is rendering to.
        /// </summary>
        public Canvas canvas
        {
            get
            {
                if (m_Canvas == null)
                    CacheCanvas();
                return m_Canvas;
            }
        }

        private void CacheCanvas()
        {
            var list = ListPool<Canvas>.Get();
            gameObject.GetComponentsInParent(false, list);
            if (list.Count > 0)
            {
                // Find the first active and enabled canvas.
                for (int i = 0; i < list.Count; ++i)
                {
                    if (list[i].isActiveAndEnabled)
                    {
                        m_Canvas = list[i];
                        break;
                    }
                }
            }
            else
                m_Canvas = null;
            ListPool<Canvas>.Release(list);
        }

        /// <summary>
        /// UI Renderer component.The CanvasRenderer used by this Graphic.
        /// </summary>
        public CanvasRenderer canvasRenderer
        {
            get
            {
                if (m_CanvasRender == null)
                    m_CanvasRender = GetComponent<CanvasRenderer>();
                return m_CanvasRender;
            }
        }

        /// <summary>
        /// Returns the default material for the graphic.
        /// </summary>
        public virtual Material defaultMaterial
        {
            get { return defaultGraphicMaterial; }
        }

        /// <summary>
        /// Returns the material used by this Graphic.The Material set by the user.
        /// </summary>
        public virtual Material material
        {
            get
            {
                return (m_Material != null) ? m_Material : defaultMaterial;
            }
            set
            {
                if (m_Material == value)
                    return;

                m_Material = value;
                SetMaterialDirty();
            }
        }
        /// <summary>
        /// 	The material that will be sent for Rendering (Read only).
        /// </summary>
        public virtual Material materialForRendering
        {
            get
            {
                var components = ListPool<Component>.Get();
                GetComponents(typeof(IMaterialModifier), components);

                var currentMat = material;
                for (var i = 0; i < components.Count; i++)
                    currentMat = (components[i] as IMaterialModifier).GetModifiedMaterial(currentMat);
                ListPool<Component>.Release(components);
                return currentMat;
            }
        }

        /// <summary>
        /// Returns the texture used to draw this Graphic.The graphic's texture. (Read Only).
        /// </summary>
        public virtual Texture mainTexture
        {
            get
            {
                return s_WhiteTexture;
            }
        }

        /// <summary>
        /// Mark the Graphic and the canvas as having been changed.
        /// </summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            CacheCanvas();
            GraphicRegistry.RegisterGraphicForCanvas(canvas, this);

#if UNITY_EDITOR
            GraphicRebuildTracker.TrackGraphic(this);
#endif
            if (s_WhiteTexture == null)//如果没有设置纹理，则使用Texture2D.whiteTexture
                s_WhiteTexture = Texture2D.whiteTexture;

            SetAllDirty();
        }

        /// <summary>
        /// Clear references.
        /// </summary>
        protected override void OnDisable()
        {
#if UNITY_EDITOR
            GraphicRebuildTracker.UnTrackGraphic(this);
#endif
            GraphicRegistry.UnregisterGraphicForCanvas(canvas, this);
            CanvasUpdateRegistry.UnRegisterCanvasElementForRebuild(this);

            if (canvasRenderer != null)
                canvasRenderer.Clear();

            LayoutRebuilder.MarkLayoutForRebuild(rectTransform);

            base.OnDisable();
        }
        /// <summary>
        /// Called when the state of the parent Canvas is changed.When a parent canvas is either enabled, disabled or a nested canvas's OverrideSorting is changed this function is called. You can for example use this to modify objects below a canvas that may depend on a parent canvas - for example, if a canvas is disabled you may want to halt some processing of a UI element.
        /// </summary>
        protected override void OnCanvasHierarchyChanged()
        {
            // Use m_Cavas so we dont auto call CacheCanvas
            Canvas currentCanvas = m_Canvas;

            // Clear the cached canvas. Will be fetched below if active.
            m_Canvas = null;

            if (!IsActive())
                return;

            CacheCanvas();

            if (currentCanvas != m_Canvas)
            {
                GraphicRegistry.UnregisterGraphicForCanvas(currentCanvas, this);

                // Only register if we are active and enabled as OnCanvasHierarchyChanged can get called
                // during object destruction and we dont want to register ourself and then become null.
                if (IsActive())
                    GraphicRegistry.RegisterGraphicForCanvas(canvas, this);
            }
        }
        /// <summary>
        /// Rebuilds the graphic geometry and its material on the PreRender cycle.
        /// </summary>
        /// <param name="update"></param>
        public virtual void Rebuild(CanvasUpdate update)
        {
            if (canvasRenderer.cull)
                return;

            switch (update)
            {
                case CanvasUpdate.PreRender:
                    if (m_VertsDirty)
                    {
                        UpdateGeometry();
                        m_VertsDirty = false;
                    }
                    if (m_MaterialDirty)
                    {
                        UpdateMaterial();
                        m_MaterialDirty = false;
                    }
                    break;
            }
        }
        /// <summary>
        /// See ICanvasElement.LayoutComplete.
        /// </summary>
        public virtual void LayoutComplete()
        {}
        /// <summary>
        /// See ICanvasElement.GraphicUpdateComplete.
        /// </summary>
        public virtual void GraphicUpdateComplete()
        {}

        /// <summary>
        /// Update the renderer's material.
        /// </summary>
        protected virtual void UpdateMaterial()
        {
            if (!IsActive())
                return;

            canvasRenderer.materialCount = 1;
            canvasRenderer.SetMaterial(materialForRendering, 0);
            canvasRenderer.SetTexture(mainTexture);
        }

        /// <summary>
        /// Update the renderer's vertices.
        /// </summary>
        protected virtual void UpdateGeometry()
        {
            if (useLegacyMeshGeneration)
                DoLegacyMeshGeneration();
            else
                DoMeshGeneration();
        }

        private void DoMeshGeneration()
        {
            if (rectTransform != null && rectTransform.rect.width >= 0 && rectTransform.rect.height >= 0)
                OnPopulateMesh(s_VertexHelper);
            else
                s_VertexHelper.Clear(); // clear the vertex helper so invalid graphics dont draw.

            var components = ListPool<Component>.Get();
            GetComponents(typeof(IMeshModifier), components);

            for (var i = 0; i < components.Count; i++)
                ((IMeshModifier)components[i]).ModifyMesh(s_VertexHelper);

            ListPool<Component>.Release(components);

            s_VertexHelper.FillMesh(workerMesh);
            canvasRenderer.SetMesh(workerMesh);
        }

        private void DoLegacyMeshGeneration()
        {
            if (rectTransform != null && rectTransform.rect.width >= 0 && rectTransform.rect.height >= 0)
            {
#pragma warning disable 618
                OnPopulateMesh(workerMesh);
#pragma warning restore 618
            }
            else
            {
                workerMesh.Clear();
            }

            var components = ListPool<Component>.Get();
            GetComponents(typeof(IMeshModifier), components);

            for (var i = 0; i < components.Count; i++)
            {
#pragma warning disable 618
                ((IMeshModifier)components[i]).ModifyMesh(workerMesh);
#pragma warning restore 618
            }

            ListPool<Component>.Release(components);
            canvasRenderer.SetMesh(workerMesh);
        }

        protected static Mesh workerMesh
        {
            get
            {
                if (s_Mesh == null)
                {
                    s_Mesh = new Mesh();
                    s_Mesh.name = "Shared UI Mesh";
                    s_Mesh.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_Mesh;
            }
        }

        [Obsolete("Use OnPopulateMesh instead.", true)]
        protected virtual void OnFillVBO(System.Collections.Generic.List<UIVertex> vbo) {}

        [Obsolete("Use OnPopulateMesh(VertexHelper vh) instead.", false)]
        protected virtual void OnPopulateMesh(Mesh m)
        {
            OnPopulateMesh(s_VertexHelper);
            s_VertexHelper.FillMesh(m);
        }

        /// <summary>
        /// Fill the vertex buffer data.
        /// </summary>
        protected virtual void OnPopulateMesh(VertexHelper vh)
        {
            var r = GetPixelAdjustedRect();
            var v = new Vector4(r.x, r.y, r.x + r.width, r.y + r.height);

            Color32 color32 = color;
            vh.Clear();
            vh.AddVert(new Vector3(v.x, v.y), color32, new Vector2(0f, 0f));
            vh.AddVert(new Vector3(v.x, v.w), color32, new Vector2(0f, 1f));
            vh.AddVert(new Vector3(v.z, v.w), color32, new Vector2(1f, 1f));
            vh.AddVert(new Vector3(v.z, v.y), color32, new Vector2(1f, 0f));

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only callback that is issued by Unity if a rebuild of the Graphic is required.Currently sent when an asset is reimported.
        /// </summary>
        public virtual void OnRebuildRequested()
        {
            // when rebuild is requested we need to rebuild all the graphics /
            // and associated components... The correct way to do this is by
            // calling OnValidate... Because MB's don't have a common base class
            // we do this via reflection. It's nasty and ugly... Editor only.
            var mbs = gameObject.GetComponents<MonoBehaviour>();
            foreach (var mb in mbs)
            {
                if (mb == null)
                    continue;
                var methodInfo = mb.GetType().GetMethod("OnValidate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (methodInfo != null)
                    methodInfo.Invoke(mb, null);
            }
        }
        /// <summary>
        /// See MonoBehaviour.Reset.
        /// </summary>
        protected override void Reset()
        {
            SetAllDirty();
        }

#endif

        // Call from unity if animation properties have changed

        protected override void OnDidApplyAnimationProperties()
        {
            SetAllDirty();
        }

        /// <summary>
        /// Make the Graphic have the native size of its content.
        /// </summary>
        public virtual void SetNativeSize() {}
        /// <summary>
        /// When a GraphicRaycaster is raycasting into the scene it does two things. First it filters the elements using their RectTransform rect. Then it uses this Raycast function to determine the elements hit by the raycast.
        /// </summary>
        /// <param name="sp"></param>
        /// <param name="eventCamera"></param>
        /// <returns></returns>
        public virtual bool Raycast(Vector2 sp, Camera eventCamera)
        {
            if (!isActiveAndEnabled)
                return false;

            var t = transform;
            var components = ListPool<Component>.Get();

            bool ignoreParentGroups = false;
            bool continueTraversal = true;

            while (t != null)
            {
                t.GetComponents(components);
                for (var i = 0; i < components.Count; i++)
                {
                    var canvas = components[i] as Canvas;
                    if (canvas != null && canvas.overrideSorting)
                        continueTraversal = false;

                    var filter = components[i] as ICanvasRaycastFilter;

                    if (filter == null)
                        continue;

                    var raycastValid = true;

                    var group = components[i] as CanvasGroup;
                    if (group != null)
                    {
                        if (ignoreParentGroups == false && group.ignoreParentGroups)
                        {
                            ignoreParentGroups = true;
                            raycastValid = filter.IsRaycastLocationValid(sp, eventCamera);
                        }
                        else if (!ignoreParentGroups)
                            raycastValid = filter.IsRaycastLocationValid(sp, eventCamera);
                    }
                    else
                    {
                        raycastValid = filter.IsRaycastLocationValid(sp, eventCamera);
                    }

                    if (!raycastValid)
                    {
                        ListPool<Component>.Release(components);
                        return false;
                    }
                }
                t = continueTraversal ? t.parent : null;
            }
            ListPool<Component>.Release(components);
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// See MonoBehaviour.OnValidate.
        /// </summary>
        protected override void OnValidate()
        {
            base.OnValidate();
            SetAllDirty();
        }

#endif
        /// <summary>
        /// Adjusts the given pixel to be pixel perfect.Note: This is only accurate if the Graphic root Canvas is in Screen Space.
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public Vector2 PixelAdjustPoint(Vector2 point)
        {
            if (!canvas || canvas.renderMode == RenderMode.WorldSpace || canvas.scaleFactor == 0.0f || !canvas.pixelPerfect)
                return point;
            else
            {
                return RectTransformUtility.PixelAdjustPoint(point, transform, canvas);
            }
        }
        /// <summary>
        /// Returns a pixel perfect Rect closest to the Graphic RectTransform.Note: This is only accurate if the Graphic root Canvas is in Screen Space.
        /// </summary>
        /// <returns></returns>
        public Rect GetPixelAdjustedRect()
        {
            if (!canvas || canvas.renderMode == RenderMode.WorldSpace || canvas.scaleFactor == 0.0f || !canvas.pixelPerfect)
                return rectTransform.rect;
            else
                return RectTransformUtility.PixelAdjustRect(rectTransform, canvas);
        }
        /// <summary>
        /// Tweens the CanvasRenderer color associated with this Graphic.
        /// </summary>
        /// <param name="targetColor"></param>
        /// <param name="duration"></param>
        /// <param name="ignoreTimeScale"></param>
        /// <param name="useAlpha"></param>
        public virtual void CrossFadeColor(Color targetColor, float duration, bool ignoreTimeScale, bool useAlpha)
        {
            CrossFadeColor(targetColor, duration, ignoreTimeScale, useAlpha, true);
        }
        /// <summary>
        /// Tweens the CanvasRenderer color associated with this Graphic.
        /// </summary>
        /// <param name="targetColor"></param>
        /// <param name="duration"></param>
        /// <param name="ignoreTimeScale"></param>
        /// <param name="useAlpha"></param>
        /// <param name="useRGB"></param>
        public virtual void CrossFadeColor(Color targetColor, float duration, bool ignoreTimeScale, bool useAlpha, bool useRGB)
        {
            if (canvasRenderer == null || (!useRGB && !useAlpha))
                return;

            Color currentColor = canvasRenderer.GetColor();
            if (currentColor.Equals(targetColor))
            {
                m_ColorTweenRunner.StopTween();
                return;
            }

            ColorTween.ColorTweenMode mode = (useRGB && useAlpha ?
                                              ColorTween.ColorTweenMode.All :
                                              (useRGB ? ColorTween.ColorTweenMode.RGB : ColorTween.ColorTweenMode.Alpha));

            var colorTween = new ColorTween {duration = duration, startColor = canvasRenderer.GetColor(), targetColor = targetColor};
            colorTween.AddOnChangedCallback(canvasRenderer.SetColor);
            colorTween.ignoreTimeScale = ignoreTimeScale;
            colorTween.tweenMode = mode;
            m_ColorTweenRunner.StartTween(colorTween);
        }

        static private Color CreateColorFromAlpha(float alpha)
        {
            var alphaColor = Color.black;
            alphaColor.a = alpha;
            return alphaColor;
        }
        /// <summary>
        /// Tweens the alpha of the CanvasRenderer color associated with this Graphic.
        /// </summary>
        /// <param name="alpha"></param>
        /// <param name="duration"></param>
        /// <param name="ignoreTimeScale"></param>
        public virtual void CrossFadeAlpha(float alpha, float duration, bool ignoreTimeScale)
        {
            CrossFadeColor(CreateColorFromAlpha(alpha), duration, ignoreTimeScale, true, false);
        }
        /// <summary>
        /// Add a listener to receive notification when the graphics layout is dirtied.
        /// </summary>
        /// <param name="action"></param>
        public void RegisterDirtyLayoutCallback(UnityAction action)
        {
            m_OnDirtyLayoutCallback += action;
        }
        /// <summary>
        /// Remove a listener from receiving notifications when the graphics layout is dirtied.
        /// </summary>
        /// <param name="action"></param>
        public void UnregisterDirtyLayoutCallback(UnityAction action)
        {
            m_OnDirtyLayoutCallback -= action;
        }
        /// <summary>
        /// Add a listener to receive notification when the graphics vertices are dirtied.
        /// </summary>
        /// <param name="action"></param>
        public void RegisterDirtyVerticesCallback(UnityAction action)
        {
            m_OnDirtyVertsCallback += action;
        }
        /// <summary>
        /// Remove a listener from receiving notifications when the graphics vertices are dirtied.
        /// </summary>
        /// <param name="action"></param>
        public void UnregisterDirtyVerticesCallback(UnityAction action)
        {
            m_OnDirtyVertsCallback -= action;
        }
        /// <summary>
        /// Add a listener to receive notification when the graphics material is dirtied.
        /// </summary>
        /// <param name="action"></param>
        public void RegisterDirtyMaterialCallback(UnityAction action)
        {
            m_OnDirtyMaterialCallback += action;
        }
        /// <summary>
        /// Remove a listener from receiving notifications when the graphics material is dirtied.
        /// </summary>
        /// <param name="action"></param>
        public void UnregisterDirtyMaterialCallback(UnityAction action)
        {
            m_OnDirtyMaterialCallback -= action;
        }
    }
}
