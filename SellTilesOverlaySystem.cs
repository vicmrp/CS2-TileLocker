using Colossal.Mathematics;
using Game;
using Game.Areas;
using Game.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CS2_TileLocker
{
    public sealed partial class SellTilesOverlaySystem : GameSystemBase
    {
        private SellTilesToolSystem _tool;
        private OverlayRenderSystem _overlay;

        private static readonly Color HoverOutline =
            new Color(1.00f, 0.35f, 0.10f, 1.00f);

        private static readonly Color HoverFill =
            new Color(1.00f, 0.18f, 0.05f, 0.28f);

        private static readonly Color SelectedOutline =
            new Color(1.00f, 0.03f, 0.02f, 1.00f);

        private static readonly Color SelectedFill =
            new Color(1.00f, 0.02f, 0.01f, 0.55f);

        private static readonly Color KeepOutline =
            new Color(0.68f, 0.18f, 1.00f, 1.00f);

        private static readonly Color KeepFill =
            new Color(0.55f, 0.05f, 0.95f, 0.52f);

        protected override void OnCreate()
        {
            base.OnCreate();
            _tool = World.GetOrCreateSystemManaged<SellTilesToolSystem>();
            _overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
        }

        protected override void OnUpdate()
        {
            if (_tool == null || !_tool.IsSellToolActive)
                return;

            OverlayRenderSystem.Buffer buffer =
                _overlay.GetBuffer(out JobHandle deps);

            JobHandle writer =
                JobHandle.CombineDependencies(deps, Dependency);

            using NativeArray<Entity> selected =
                _tool.GetSelectedTiles(Allocator.Temp);

            Color selectedOutline =
                _tool.IsKeepMode ? KeepOutline : SelectedOutline;

            Color selectedFill =
                _tool.IsKeepMode ? KeepFill : SelectedFill;

            for (int i = 0; i < selected.Length; i++)
            {
                DrawTile(
                    buffer,
                    selected[i],
                    selectedOutline,
                    selectedFill,
                    true);
            }

            Entity hovered = _tool.HoveredTile;

            if (hovered != Entity.Null && !_tool.IsSelected(hovered))
            {
                Color hoverOutline =
                    _tool.IsKeepMode ? KeepOutline : HoverOutline;

                Color hoverFill =
                    _tool.IsKeepMode
                        ? new Color(0.55f, 0.05f, 0.95f, 0.28f)
                        : HoverFill;

                DrawTile(
                    buffer,
                    hovered,
                    hoverOutline,
                    hoverFill,
                    false);
            }

            _overlay.AddBufferWriter(writer);
            Dependency = writer;
        }

        private void DrawTile(
            OverlayRenderSystem.Buffer buffer,
            Entity tile,
            Color outline,
            Color fill,
            bool selected)
        {
            if (tile == Entity.Null ||
                !EntityManager.Exists(tile) ||
                !EntityManager.HasBuffer<Game.Areas.Node>(tile) ||
                !EntityManager.HasBuffer<Triangle>(tile))
            {
                return;
            }

            DynamicBuffer<Game.Areas.Node> nodes =
                EntityManager.GetBuffer<Game.Areas.Node>(tile, true);

            DynamicBuffer<Triangle> triangles =
                EntityManager.GetBuffer<Triangle>(tile, true);

            for (int i = 0; i < triangles.Length; i++)
            {
                Triangle3 tri =
                    AreaUtils.GetTriangle3(nodes, triangles[i]);

                DrawTriangleFill(buffer, tri, fill, selected);
            }

            if (nodes.Length >= 2)
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    float3 a = nodes[i].m_Position;
                    float3 b = nodes[(i + 1) % nodes.Length].m_Position;

                    buffer.DrawLine(
                        outline,
                        outline,
                        0f,
                        OverlayRenderSystem.StyleFlags.Projected,
                        new Line3.Segment(a, b),
                        selected ? 9f : 6f,
                        new float2(0f, 0f));
                }
            }
        }

        private static void DrawTriangleFill(
            OverlayRenderSystem.Buffer buffer,
            Triangle3 triangle,
            Color fill,
            bool selected)
        {
            int steps = selected ? 56 : 40;

            float3 a = triangle.a;
            float3 b = triangle.b;
            float3 c = triangle.c;

            float longest = math.max(
                math.distance(a.xz, c.xz),
                math.distance(b.xz, c.xz));

            float stripWidth = math.max(4f, longest / steps * 1.75f);

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float3 p1 = math.lerp(a, c, t);
                float3 p2 = math.lerp(b, c, t);

                if (math.distance(p1.xz, p2.xz) < 0.1f)
                    continue;

                buffer.DrawLine(
                    fill,
                    fill,
                    0f,
                    OverlayRenderSystem.StyleFlags.Projected,
                    new Line3.Segment(p1, p2),
                    stripWidth,
                    new float2(0f, 0f));
            }
        }
    }
}
