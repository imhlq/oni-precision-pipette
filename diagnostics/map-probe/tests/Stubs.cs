// Offline doubles only: these do not implement or validate the native game runtime.
using System;
using System.Collections.Generic;
using System.Reflection;
namespace HarmonyLib {
 public class Harmony { public void Patch(MethodInfo m, HarmonyMethod prefix=null, HarmonyMethod postfix=null) {} }
 public class HarmonyMethod { public HarmonyMethod(Type t,string n) {} }
 public static class AccessTools {
  public static string Missing;
  public static MethodInfo DeclaredMethod(Type t,string n,Type[] a=null) => n==Missing?null:t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly);
  public static MethodInfo Method(Type t,string n,Type[] a=null) => DeclaredMethod(t,n,a);
 }
}
namespace KMod { public class UserMod2 { public virtual void OnLoad(HarmonyLib.Harmony h) {} } }
namespace KSerialization {
 public enum MemberSerialization { OptIn }
 public class SerializationConfigAttribute:Attribute { public SerializationConfigAttribute(MemberSerialization m) {} }
 public class SerializeAttribute:Attribute {}
}
namespace UnityEngine {
 public static class Application {public static string version="offline-test";}
 public static class Time {public static int frameCount;}
 public class MonoBehaviour { public int GetInstanceID()=>1; }
 public class GameObject { public T AddComponent<T>() where T:new()=>new T(); }
 public static class Debug { public static readonly List<string> Logs=new List<string>(); public static void LogError(object o) {Logs.Add(o.ToString());} public static void Log(object o) {Logs.Add(o.ToString());} }
 public struct Vector3 {public float x,y,z;}
 public struct Rect {public Rect(float x,float y,float w,float h){} public bool Contains(Vector3 p)=>false;}
 public class Camera {public static Camera main=new Camera(); public Vector3 ScreenToWorldPoint(Vector3 v)=>v;public Vector3 WorldToScreenPoint(Vector3 v)=>v;}
 public static class Screen {public static int height=100;}
 public enum EventType {KeyDown,MouseDown}
 public enum KeyCode {Escape}
 public class Event {public static Event current;public EventType type;public KeyCode keyCode;public int button;public Vector3 mousePosition;public void Use(){}}
 public static class GUI {public static bool enabled=true;public static bool Button(Rect r,string s)=>false;public static void Box(Rect r,string s){}public static void DragWindow(Rect r){}}
 public static class GUILayout {
  public static object Width(float n)=>null;
  public static Rect Window(int id,Rect r,Action<int> f,string s,params object[] o)=>r;
  public static void BeginHorizontal(){} public static void EndHorizontal(){}
  public static bool Toggle(bool b,string s,string style=null)=>b;
  public static void Label(string s,params object[] o){}
  public static string TextField(string s,int n,params object[] o)=>s;
  public static int SelectionGrid(int i,string[] s,int n,params object[] o)=>i;
  public static bool Button(string s)=>false;
 }
}
public class KMonoBehaviour:UnityEngine.MonoBehaviour {protected virtual void OnSpawn(){} protected virtual void OnCleanUp(){}}
public class SaveGame {private void OnPrefabInit(){} public T GetComponent<T>()=>default;public UnityEngine.GameObject gameObject=new UnityEngine.GameObject();}
public class Game {public static Game Instance=new Game();public bool Loading;public bool IsLoading()=>Loading;private void OnSpawn(){}public T GetComponent<T>()=>default;public UnityEngine.GameObject gameObject=new UnityEngine.GameObject();}
public interface ISim33ms {void Sim33ms(float dt);}
public class GameClock {public static GameClock Instance=new GameClock();public double Clock;public double GetTimeAsDouble()=>Clock;}
public class ClusterManager {public static ClusterManager Instance=new ClusterManager();public int activeWorldId=1,blockers;public int AddSaveBlocker(string s){blockers++;return blockers;}public void RemoveSaveBlocker(int id){blockers--;}}
public class SpeedControlScreen {public static SpeedControlScreen Instance=new SpeedControlScreen();public bool IsPaused;}
public static class KInputManager {public static UnityEngine.Vector3 GetMousePos()=>default;}
public enum SimHashes {Vacuum=0,Hydrogen=1,Water=2,Oxygen=3}
public class Element {public bool IsGas,IsLiquid,IsVacuum;public SimHashes id;public string name="element";public Element lowTempTransition,highTempTransition;public float lowTemp,highTemp;}
public static class ElementLoader {
 public static Element FindElementByHash(SimHashes h)=>h==SimHashes.Hydrogen||h==SimHashes.Oxygen?new Element {id=h,IsGas=true}:h==SimHashes.Water?new Element{id=h,IsLiquid=true}:h==0?new Element{IsVacuum=true}:null;
}
public static class Grid {
 public enum SceneLayer {Move}
 public static int WidthInCells=5,HeightInCells=5;
 public static int[] WorldIdx=new int[25];public static bool[] HasDoor=new bool[25];
 public static Element[] Element=new Element[25];
 public static float[] Mass=new float[25],Temperature=new float[25];
 public static byte[] DiseaseIdx=new byte[25];public static int[] DiseaseCount=new int[25];
 public static bool IsValidCell(int c)=>c>=0&&c<25;public static bool IsVisible(int c)=>true;public static bool IsSolidCell(int c)=>false;
 public static int PosToCell(UnityEngine.Vector3 v)=>12;public static void CellToXY(int c,out int x,out int y){x=c%WidthInCells;y=c/WidthInCells;}
}
