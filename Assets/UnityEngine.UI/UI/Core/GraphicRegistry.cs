using System.Collections.Generic;
using UnityEngine.UI.Collections;

namespace UnityEngine.UI
{
    /// <summary>
    /// Registry which maps a Graphic to the canvas it belongs to.单例类，用于保存Canvas和与其关联的Graphic。并可以通过其获取到某个Canvas关联的所有Graphic。在其内部每个Canvas对应的所有Graphic是通过一种名为IndexedSet的数据结构来保存的。
    /// </summary>
    public class GraphicRegistry
    {
        private static GraphicRegistry s_Instance;

        private readonly Dictionary<Canvas, IndexedSet<Graphic>> m_Graphics = new Dictionary<Canvas, IndexedSet<Graphic>>();

        protected GraphicRegistry()
        {
            // This is needed for AOT on IOS. Without it the compile doesn't get the definition of the Dictionarys
#pragma warning disable 168
            Dictionary<Graphic, int> emptyGraphicDic;
            Dictionary<ICanvasElement, int> emptyElementDic;
#pragma warning restore 168
        }

        public static GraphicRegistry instance
        {
            get
            {
                if (s_Instance == null)
                    s_Instance = new GraphicRegistry();
                return s_Instance;
            }
        }
        /// <summary>
        /// Store a link between the given canvas and graphic in the registry.
        /// </summary>
        /// <param name="c"></param>
        /// <param name="graphic"></param>
        public static void RegisterGraphicForCanvas(Canvas c, Graphic graphic)
        {
            if (c == null)
                return;

            IndexedSet<Graphic> graphics;
            instance.m_Graphics.TryGetValue(c, out graphics);

            if (graphics != null)
            {
                graphics.AddUnique(graphic);
                return;
            }

            // Dont need to AddUnique as we know its the only item in the list
            graphics = new IndexedSet<Graphic>();
            graphics.Add(graphic);
            instance.m_Graphics.Add(c, graphics);
        }
        /// <summary>
        /// Deregister the given Graphic from a Canvas.
        /// </summary>
        /// <param name="c"></param>
        /// <param name="graphic"></param>
        public static void UnregisterGraphicForCanvas(Canvas c, Graphic graphic)
        {
            if (c == null)
                return;

            IndexedSet<Graphic> graphics;
            if (instance.m_Graphics.TryGetValue(c, out graphics))
            {
                graphics.Remove(graphic);

                if (graphics.Count == 0)
                    instance.m_Graphics.Remove(c);
            }
        }

        private static readonly List<Graphic> s_EmptyList = new List<Graphic>();
        /// <summary>
        /// Return a list of Graphics that are registered on the Canvas.
        /// </summary>
        /// <param name="canvas"></param>
        /// <returns></returns>
        public static IList<Graphic> GetGraphicsForCanvas(Canvas canvas)
        {
            IndexedSet<Graphic> graphics;
            if (instance.m_Graphics.TryGetValue(canvas, out graphics))
                return graphics;

            return s_EmptyList;
        }
    }
}
