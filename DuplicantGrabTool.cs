using System.Collections.Generic;
using UnityEngine;
using UIEvents = UnityEngine.EventSystems.EventSystem;

namespace OniPrecision
{
    public sealed class DuplicantGrabTool : InterfaceTool
    {
        internal InterfaceTool Previous;
        internal KCollider2D Target;
        private readonly List<KSelectable> hoverHits = new List<KSelectable>();
        private float nextHoverCheck;
        private Brain brain;
        private Navigator navigator;
        private KBatchedAnimController animationController;
        private static FallMonitor.Instance heldMonitor;
        private bool dragging, pressing;
        private float pressedAt;
        private Vector2 pressPosition;
        private Vector3 gripOffset;
        private int world;
        private SelectToolHoverTextCard hoverStyle;

        internal static bool AllowFallUpdate(FallMonitor.Instance __instance) =>
            !ReferenceEquals(__instance, heldMonitor);

        internal static KCollider2D FindTarget(InterfaceTool tool, List<KSelectable> hits, Vector2 mouse)
        {
            hits.Clear();
            tool.GetSelectablesUnderCursor(hits);
            foreach (var hit in hits)
            {
                var identity = hit.GetComponentInParent<MinionIdentity>();
                if (identity == null || identity.HasTag(GameTags.Dead) || identity.HasTag(GameTags.Stored)) continue;
                var body = identity.GetComponent<KCollider2D>();
                if (body != null && body.isActiveAndEnabled && body.Intersects(mouse)) return body;
            }
            return null;
        }

        private static bool OverUI => UIEvents.current != null && UIEvents.current.IsPointerOverGameObject();

        public override void OnLeftClickDown(Vector3 pos)
        {
            if (pressing || dragging || OverUI) return;
            Target = FindTarget(this, hoverHits, pos);
            if (Target == null) return;
            pressing = true;
            pressPosition = pos;
            pressedAt = Time.unscaledTime;
        }

        public override void OnLeftClickUp(Vector3 pos) => Release();

        public override void LateUpdate()
        {
            base.LateUpdate();
            if (PlayerController.Instance == null || PlayerController.Instance.ActiveTool != this) return;
            if (!Application.isFocused || !hasFocus || Input.GetKeyDown(KeyCode.Escape) ||
                (!Input.GetKey(KeyCode.LeftAlt) && !pressing && !dragging))
            {
                PlayerController.Instance.ActivateTool(Previous != null ? Previous : SelectTool.Instance);
                return;
            }
            Vector3 mouse = Camera.main.ScreenToWorldPoint(KInputManager.GetMousePos());
            if (Input.GetMouseButtonDown(0)) OnLeftClickDown(mouse);
            if (!pressing && !dragging)
            {
                if (OverUI) Target = null;
                else if (Time.unscaledTime >= nextHoverCheck)
                {
                    Target = FindTarget(this, hoverHits, mouse);
                    nextHoverCheck = Time.unscaledTime + .05f;
                }
            }
            var pointed = !OverUI && Target != null && Target.Intersects(mouse)
                ? Target.GetComponent<KSelectable>() : null;
            if (hover != pointed)
            {
                ClearHover();
                hover = pointed;
                if (hover != null) hover.Hover(false);
            }
            if (Target == null)
            {
                Release();
                if (!OverUI && HoverTextScreen.Instance != null) HoverTextScreen.Instance.BeginDrawing().EndDrawing();
                return;
            }
            if (!OverUI && ShowHoverUI() && HoverTextScreen.Instance != null)
            {
                if (hoverStyle == null && SelectTool.Instance != null)
                    hoverStyle = SelectTool.Instance.GetComponent<SelectToolHoverTextCard>();
                if (hoverStyle != null)
                {
                    string hint = dragging ? PipetteText.Get("Release mouse to let go", "松开左键释放") :
                        pressing ? PipetteText.Get("Grabbing…", "正在抓取…") :
                        PipetteText.Get("Hold left to grab", "长按左键抓取");
                    var drawer = HoverTextScreen.Instance.BeginDrawing();
                    drawer.BeginShadowBar();
                    drawer.DrawText(Target.GetProperName() + " · " + hint, hoverStyle.Styles_BodyText.Standard);
                    drawer.EndShadowBar();
                    drawer.EndDrawing();
                }
            }
            if (!pressing && !dragging) return;

            if (!Input.GetMouseButton(0) || OverUI || Target.HasTag(GameTags.Dead) || Target.HasTag(GameTags.Stored))
            {
                Release();
                return;
            }
            if (!dragging)
            {
                // Keep the clicked duplicant while it walks, but cancel if the player moves off the click.
                if (((Vector2)mouse - pressPosition).sqrMagnitude > .25f) { pressing = false; return; }
                if (Time.unscaledTime - pressedAt < .45f) return;
                brain = Target.GetComponent<Brain>();
                navigator = Target.GetComponent<Navigator>();
                animationController = Target.GetComponent<KBatchedAnimController>();
                var monitor = Target.GetSMI<FallMonitor.Instance>();
                if (brain == null || navigator == null || navigator.CurrentNavType == NavType.Tube ||
                    animationController == null || monitor == null)
                { pressing = false; return; }
                bool falling = monitor.IsInsideState(monitor.sm.falling);
                if (!falling && !monitor.IsInsideState(monitor.sm.standing)) { pressing = false; return; }
                if (falling)
                {
                    // Leaving falling unregisters native gravity before the tool moves the body.
                    monitor.sm.isFalling.Set(false, monitor);
                    monitor.GoTo(monitor.sm.standing);
                }
                if (!brain.IsRunning()) { pressing = false; monitor.UpdateFalling(); return; }
                heldMonitor = monitor;
                dragging = true;
                pressing = false;
                world = ClusterManager.Instance.activeWorldId;
                Target.GetComponent<ChoreDriver>().StopChore();
                brain.Suspend("YAPP grab");
                navigator.Stop(false, false);
                animationController.Play("fall_loop", KAnim.PlayMode.Loop);
                gripOffset = Target.transform.position - mouse;
            }
            if (world != ClusterManager.Instance.activeWorldId) { Release(); return; }
            Vector3 pos = Target.transform.position;
            Vector3 goal = mouse + gripOffset;
            goal.z = pos.z;
            // Check each swept axis separately so the body can slide along a wall.
            Vector3 step = Vector3.ClampMagnitude(goal - pos, 3.25f * Mathf.Min(Time.unscaledDeltaTime, .05f));
            Vector3 dx = new Vector3(step.x, 0, 0);
            Vector3 dy = new Vector3(0, step.y, 0);
            Bounds bounds = Target.bounds;
            bounds.center += (Vector3)(Target.restOffset - Target.offset);
            bounds.Encapsulate(pos + Vector3.up * 1.8f);
            bounds.Encapsulate(pos);
            if (CanSweep(bounds, dx)) { pos += dx; bounds.center += dx; }
            if (CanSweep(bounds, dy)) pos += dy;
            if (pos != Target.transform.position)
            {
                Target.transform.SetPosition(pos);
                Target.MarkDirty();
            }
            // The regular world-animation list skips frames when game time is stopped.
            if (Time.deltaTime == 0f && (animationController.visibilityType != KAnimControllerBase.VisibilityType.Always ||
                animationController.forceUseGameTime))
                animationController.UpdateAnim(Time.unscaledDeltaTime);
        }

        private bool CanSweep(Bounds body, Vector3 delta)
        {
            if (delta == Vector3.zero) return false;
            Vector3 min = Vector3.Min(body.min, body.min + delta);
            Vector3 max = Vector3.Max(body.max, body.max + delta);
            for (int y = Mathf.FloorToInt(min.y + .001f); y <= Mathf.FloorToInt(max.y - .001f); y++)
            for (int x = Mathf.FloorToInt(min.x + .001f); x <= Mathf.FloorToInt(max.x - .001f); x++)
            {
                if (x < 0 || y < 0 || x >= Grid.WidthInCells || y >= Grid.HeightInCells) return false;
                int cell = Grid.XYToCell(x,y);
                if (Grid.WorldIdx[cell] != world) return false;
                if (!Grid.IsSolidCell(cell) && !Grid.DupeImpassable[cell]) continue;
                // A body already touching terrain may slide parallel or retreat from it.
                if (body.max.x <= x + .001f || body.min.x >= x + .999f ||
                    body.max.y <= y + .001f || body.min.y >= y + .999f) return false;
                float push = delta.x != 0f ? delta.x * (body.center.x - x - .5f) :
                    delta.y * (body.center.y - y - .5f);
                if (push < 0f) return false;
            }
            return true;
        }

        private void Release()
        {
            pressing = false;
            if (!dragging) return;
            dragging = false;
            var monitor = heldMonitor;
            heldMonitor = null;
            if (brain != null) brain.Resume("YAPP grab");
            if (Target == null) return;
            if (navigator != null && animationController != null)
                animationController.Play(navigator.NavGrid.GetIdleAnim(navigator.CurrentNavType), KAnim.PlayMode.Loop);
            monitor?.UpdateFalling();
            if (Time.deltaTime == 0f && animationController != null) animationController.UpdateAnim(0f);
        }

        protected override void OnDeactivateTool(InterfaceTool next)
        {
            Release();
            ClearHover();
            Target = null;
            base.OnDeactivateTool(next);
        }

        protected override void OnCleanUp()
        {
            Release();
            ClearHover();
            base.OnCleanUp();
        }
    }
}
