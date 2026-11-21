using System;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace OniU60MapProbe
{
    public sealed class Mod : KMod.UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            try
            {
                var hook = AccessTools.DeclaredMethod(typeof(Game), "OnSpawn", Type.EmptyTypes);
                if (hook == null) { Debug.LogError("[PPMAP] Missing Game.OnSpawn; diagnostic disabled."); return; }
                harmony.Patch(hook, postfix: new HarmonyMethod(typeof(Mod), nameof(Attach)));
            }
            catch (Exception e) { Debug.LogError("[PPMAP] Diagnostic disabled: " + e); }
        }
        private static void Attach(Game __instance)
        {
            try
            {
                if (__instance.GetComponent<MapProbe>() == null) __instance.gameObject.AddComponent<MapProbe>();
            }
            catch (Exception e) { Debug.LogError("[PPMAP] Cannot attach diagnostic: " + e); }
        }
    }

    // Runtime-only component on Game, not SaveGame. No Serialize fields, sim mutations,
    // callbacks registered with native transfer managers, or save interception.
    public sealed class MapProbe : KMonoBehaviour, ISim33ms
    {
        private bool expanded, recording;
        private int target = -1, source = -1, world = -1, picking, sequence, remaining;
        private string run, count = "120", label = "fresh-before", build = "enter-game-build";
        private string status = "只读诊断 / read only. Select target; capture is opt-in.";
        private int dose = 2, route;
        private Rect panel = new Rect(480, 110, 590, 0);
        private static readonly string[] Doses = { "10 ug", "15 ug", "50 ug" };
        private static readonly string[] Routes = { "observed-only", "pump-down", "vanilla-vent", "external-test" };

        protected override void OnCleanUp()
        {
            Stop("cleanup");
            base.OnCleanUp();
        }
        private bool Paused => SpeedControlScreen.Instance == null || SpeedControlScreen.Instance.IsPaused;
        private bool Valid(int cell) => Grid.IsValidCell(cell) && Grid.IsVisible(cell) &&
            ClusterManager.Instance != null && Grid.WorldIdx[cell] == ClusterManager.Instance.activeWorldId;
        private string CellLabel(int cell)
        {
            if (!Grid.IsValidCell(cell)) return "not selected";
            Grid.CellToXY(cell, out int x, out int y);
            return cell + " (" + x + "," + y + ")";
        }
        private void OnGUI()
        {
            if (Game.Instance == null || Game.Instance.IsLoading() || Camera.main == null) return;
            if (!expanded)
            {
                if (GUI.Button(new Rect(480, 110, 250, 28), "U60 map probe / 地图只读采样")) expanded = true;
                return;
            }
            panel = GUILayout.Window(GetInstanceID(), panel, DrawWindow, "U60 read-only map probe", GUILayout.Width(590));
            var ev = Event.current;
            if (picking == 0 || ev == null) return;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape) { picking = 0; ev.Use(); return; }
            if (ev.type != EventType.MouseDown || ev.button != 0 || panel.Contains(ev.mousePosition)) return;
            int cell = Grid.PosToCell(Camera.main.ScreenToWorldPoint(KInputManager.GetMousePos()));
            if (Valid(cell))
            {
                if (picking == 1) target = cell; else source = cell;
                picking = 0;
                status = "Selected. No material changed. / 已选格，未改变物质。";
            }
            else status = "Select a visible cell in the active world.";
            ev.Use();
        }
        private void DrawWindow(int id)
        {
            bool enabled = GUI.enabled;
            GUILayout.Label("Only reads map cells: no dosing, pipes, food edits or save hooks.");
            GUI.enabled = enabled && !recording;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Target / 目标: " + CellLabel(target))) picking = 1;
            if (GUILayout.Button("Source (optional): " + CellLabel(source))) picking = 2;
            if (GUILayout.Button("Clear source")) source = -1;
            GUILayout.EndHorizontal();
            GUILayout.Label("Case label only — DOES NOT SET MASS / 标签不会设置剂量");
            dose = Choices(dose, Doses);
            route = Choices(route, Routes);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Build"); build = GUILayout.TextField(build, 32);
            GUILayout.Label("Label"); label = GUILayout.TextField(label, 64);
            GUILayout.Label("Callbacks"); count = GUILayout.TextField(count, 4, GUILayout.Width(55));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Snapshot / 单次快照")) Begin(false);
            if (GUILayout.Button("Capture / 连续采样")) Begin(true);
            GUILayout.EndHorizontal();
            GUI.enabled = enabled;
            if (recording && GUILayout.Button("Stop / 停止")) Stop("user");
            GUILayout.Label(status);
            GUILayout.Label("Capture logs baseline now, then each unpaused ISim33ms callback.\nGrid may lag native sim; callback ordinal is NOT a native tick ID.");
            if (GUILayout.Button("Hide / 收起")) { expanded = false; picking = 0; }
            GUI.DragWindow(new Rect(0, 0, 590, 20));
        }
        // Moono's handwritten IMGUI supports Button; SelectionGrid may be an
        // unimplemented generated surface. Use only ordinary buttons here.
        private static int Choices(int selected, string[] choices)
        {
            selected = ProbeData.ChoiceIndex(selected, choices.Length);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < choices.Length; i++)
                if (GUILayout.Button((selected == i ? "[x] " : "[ ] ") + choices[i])) selected = i;
            GUILayout.EndHorizontal();
            return selected;
        }
        private void Begin(bool continuous)
        {
            if (recording || !Valid(target) || (source >= 0 && !Valid(source)))
            { status = "Select valid target (and optional source) in active world."; return; }
            picking = 0;
            dose = ProbeData.ChoiceIndex(dose, Doses.Length);
            route = ProbeData.ChoiceIndex(route, Routes.Length);
            world = Grid.WorldIdx[target];
            run = Guid.NewGuid().ToString("N");
            sequence = 0;
            remaining = continuous ? ProbeData.Limit(count) : 0;
            recording = true;
            Debug.Log("PPMAP|kind=begin|schema=1|run=" + run + "|utc=" + System.DateTime.UtcNow.ToString("O") +
                "|buildLabel=" + ProbeData.Label(build) + "|applicationVersion=" + ProbeData.Label(Application.version) +
                "|assemblyMvid=" + typeof(Game).Module.ModuleVersionId + "|label=" + ProbeData.Label(label) +
                "|case=" + Doses[dose] + "|route=" + Routes[route] + "|world=" + world +
                "|target=" + target + "|source=" + source + "|radius=2|requestedCallbacks=" + remaining);
            Capture("baseline", 0);
            if (!continuous && recording) Stop("snapshot");
            else if (recording) status = "Armed: unpause for " + remaining + " callbacks. Logs: PPMAP in Player.log.";
        }
        public void Sim33ms(float dt)
        {
            if (!recording) return;
            if (Game.Instance == null || Game.Instance.IsLoading()) { Stop("loading"); return; }
            if (Paused) return;
            if (ClusterManager.Instance == null || ClusterManager.Instance.activeWorldId != world ||
                !Valid(target) || (source >= 0 && !Valid(source))) { Stop("world-or-selection-changed"); return; }
            Capture("sim33ms", dt);
            if (recording && --remaining <= 0) Stop("complete");
        }
        private void Capture(string phase, float dt)
        {
            try
            {
                var text = new StringBuilder(18000);
                string common = "|run=" + run + "|seq=" + sequence;
                text.Append("PPMAP|kind=sample").Append(common).Append("|phase=").Append(phase)
                    .Append("|clock=").Append(GameClock.Instance == null ? "unavailable" : GameClock.Instance.GetTimeAsDouble().ToString("R", CultureInfo.InvariantCulture))
                    .Append("|renderFrame=").Append(Time.frameCount).Append("|dt=").Append(ProbeData.Number(dt))
                    .Append("|paused=").Append(Paused ? 1 : 0).AppendLine();
                Watch(text, common, "target", target);
                if (source >= 0) Watch(text, common, "source", source);
                text.Append("PPMAP|kind=sampleEnd").Append(common);
                Debug.Log(text.ToString());
                sequence++;
            }
            catch (Exception e) { Debug.LogError("PPMAP|kind=error|run=" + run + "|error=" + ProbeData.Label(e.ToString())); Stop("read-error"); }
        }
        private void Watch(StringBuilder text, string common, string watch, int center)
        {
            Grid.CellToXY(center, out int x, out int y);
            for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
            {
                int cell = ProbeData.Cell(x + dx, y + dy, Grid.WidthInCells, Grid.HeightInCells);
                text.Append("PPMAP|kind=cell").Append(common).Append("|watch=").Append(watch)
                    .Append("|dx=").Append(dx).Append("|dy=").Append(dy).Append("|cell=").Append(cell);
                if (!Grid.IsValidCell(cell) || Grid.WorldIdx[cell] != world || !Grid.IsVisible(cell))
                { text.AppendLine("|valid=0"); continue; }
                var element = Grid.Element[cell];
                float kg = Grid.Mass[cell], kelvin = Grid.Temperature[cell];
                text.Append("|valid=1|world=").Append(Grid.WorldIdx[cell])
                    .Append("|element=").Append(element == null ? "null" : element.id.ToString())
                    .Append("|elementHash=").Append(element == null ? "null" : ((int)element.id).ToString(CultureInfo.InvariantCulture))
                    .Append("|kg=").Append(ProbeData.Number(kg)).Append("|kgBits=").Append(ProbeData.Bits(kg))
                    .Append("|kelvin=").Append(ProbeData.Number(kelvin)).Append("|kelvinBits=").Append(ProbeData.Bits(kelvin))
                    .Append("|germIdx=").Append(Grid.DiseaseIdx[cell]).Append("|germCount=").Append(Grid.DiseaseCount[cell])
                    .Append("|solid=").Append(Grid.IsSolidCell(cell) ? 1 : 0).Append("|door=").Append(Grid.HasDoor[cell] ? 1 : 0).AppendLine();
            }
        }
        private void Stop(string reason)
        {
            if (!recording) return;
            recording = false;
            Debug.Log("PPMAP|kind=end|run=" + run + "|reason=" + reason + "|samples=" + sequence);
            status = "Stopped: " + reason + ". " + sequence + " samples in Player.log. / 已停止";
        }
    }
}
