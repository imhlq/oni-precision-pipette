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
 public class MonoBehaviour { public int GetInstanceID()=>1; }
 public class GameObject { public T AddComponent<T>() where T:new()=>new T(); }
 public static class Debug { public static void LogError(object o) {} }
 public struct Vector3 {public float x,y,z;}
 public struct Rect {public Rect(float x,float y,float w,float h){} public bool Contains(Vector3 p)=>false;}
 public class Camera {public static Camera main=new Camera(); public Vector3 ScreenToWorldPoint(Vector3 v)=>v;public Vector3 WorldToScreenPoint(Vector3 v)=>v;}
 public static class Screen {public static int height=100;}
 public enum EventType {KeyDown,MouseDown}
 public enum KeyCode {Escape}
 public class Event {public static Event current;public EventType type;public KeyCode keyCode;public int button;public Vector3 mousePosition;public void Use(){}}
 public static class GUI {public static bool enabled=true;public static bool Button(Rect r,string s)=>false;public static void Box(Rect r,string s){}public static void DragWindow(Rect r){}}
 public class GUILayoutOption {}
 public static class GUILayout {
  public static string Pressed;
  public static GUILayoutOption Width(float n)=>new GUILayoutOption();
  public static Rect Window(int id,Rect r,Action<int> f,string s,params object[] o)=>r;
  public static void BeginHorizontal(){} public static void EndHorizontal(){}
  public static bool Toggle(bool b,string s,string style=null)=>b;
  public static void Label(string s,params object[] o){}
  public static string TextField(string s,int n,params object[] o)=>s;
  public static bool Button(string s,params GUILayoutOption[] options)=>s==Pressed;
 }
}
public class KMonoBehaviour:UnityEngine.MonoBehaviour {protected virtual void OnSpawn(){} protected virtual void OnCleanUp(){}}
public class SaveGame {private void OnPrefabInit(){} public T GetComponent<T>()=>default;public UnityEngine.GameObject gameObject=new UnityEngine.GameObject();}
public class Game {public static Game Instance=new Game();public ConduitFlow gasConduitFlow=new ConduitFlow(), liquidConduitFlow=new ConduitFlow{MaxMass=10f};public bool IsLoading()=>false;public bool CanSave()=>true;}
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
 public static int[] WorldIdx={1,1,1,1}; public static bool[] HasDoor=new bool[4];
 public static Element[] Element={ElementLoader.FindElementByHash(SimHashes.Hydrogen),ElementLoader.FindElementByHash(SimHashes.Hydrogen),ElementLoader.FindElementByHash(0),ElementLoader.FindElementByHash(0)};
 public static float[] Mass={.01f,.5f,0,0},Temperature={300,300,0,0};
 public static bool IsValidCell(int c)=>c>=0&&c<4;public static bool IsVisible(int c)=>true;public static bool IsSolidCell(int c)=>false;
 public static int PosToCell(UnityEngine.Vector3 v)=>0;public static void CellToXY(int c,out int x,out int y){x=c;y=0;}public static UnityEngine.Vector3 CellToPosCCC(int c,SceneLayer l)=>default;
}
public class Db {public static bool Unlocked;public static Db Get()=>new Db();public Db Techs=>this;public Db TryGet(string s)=>this;public bool IsComplete()=>Unlocked;}
public class ConduitFlow {
 public float MaxMass=1f;
 public struct Contents {public float mass,temperature;public SimHashes element;public byte diseaseIdx;public int diseaseCount;}
 public Dictionary<int,Contents> Cells=new Dictionary<int,Contents>();
 public bool ThrowRemove,ThrowAdd,WrongReceipt,Refuse;public float Partial=1;
 public bool HasConduit(int c)=>Cells.ContainsKey(c);public Contents GetContents(int c)=>Cells[c];
 public Contents RemoveElement(int c,float amount){if(ThrowRemove)throw new Exception("unknown debit");var before=Cells[c];var removed=before;removed.mass=Math.Min(amount,before.mass);removed.diseaseCount=(int)(before.diseaseCount*(removed.mass/before.mass));before.mass-=removed.mass;before.diseaseCount-=removed.diseaseCount;if(before.diseaseCount==0)before.diseaseIdx=255;Cells[c]=before;if(removed.diseaseCount==0)removed.diseaseIdx=255;return removed;}
 public float AddElement(int c,SimHashes element,float amount,float temp,byte germ,int count){if(ThrowAdd)throw new Exception("unknown credit");if(Refuse)return 0;var before=Cells[c];float accepted=amount*Partial;before.mass+=accepted;before.element=element;before.temperature=temp;before.diseaseCount+=(int)(count*Partial);before.diseaseIdx=before.diseaseCount>0?germ:(byte)255;Cells[c]=before;return WrongReceipt?amount*2:accepted;}
}

namespace STRINGS { public static class UI {public static class UNITSUFFIXES {public static class MASS {public const string MICROGRAM="μg", MILLIGRAM="mg", GRAM="g", KILOGRAM="kg";}}}}
namespace Klei { public static class SimUtil {
 public static (byte idx,int count) CalculateFinalDiseaseInfo(byte a,int n,byte b,int m) => (n>0?a:b,n+m);
}}
