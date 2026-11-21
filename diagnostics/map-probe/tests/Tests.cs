using System;
using System.Linq;
using System.Reflection;
using System.Globalization;
using OniU60MapProbe;
using UnityEngine;
class Tests {
 static int checks;
 static void Assert(bool b,string s){checks++;if(!b)throw new Exception(s);}
 static void Set(object p,string n,object v)=>p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,v);
 static T Get<T>(object p,string n)=>(T)p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p);
 static void Call(object p,string n,params object[] a)=>p.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(p,a);
 static MapProbe New(){Debug.Logs.Clear();Game.Instance=new Game();ClusterManager.Instance.activeWorldId=1;SpeedControlScreen.Instance.IsPaused=false;GameClock.Instance.Clock=0;for(int i=0;i<25;i++){Grid.WorldIdx[i]=1;Grid.Mass[i]=0;Grid.Temperature[i]=300;Grid.Element[i]=ElementLoader.FindElementByHash(0);}Grid.Element[12]=ElementLoader.FindElementByHash(SimHashes.Hydrogen);Grid.Mass[12]=50e-9f;var p=new MapProbe();Set(p,"target",12);return p;}
 static void Main(){
  CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
  foreach(float v in new[]{10e-9f,15e-9f,50e-9f,248.15f,0f,-0f,float.Epsilon,float.MaxValue}) {
   float decoded=(float)double.Parse(ProbeData.Number(v),CultureInfo.InvariantCulture);
   Assert(ProbeData.Bits(v)==ProbeData.Bits(decoded),"round-trip bits "+v);
  }
  Assert(ProbeData.Cell(-1,1,5,5)==-1&&ProbeData.Cell(5,0,5,5)==-1,"no edge wrapping");
  Assert(ProbeData.Cell(4,4,5,5)==24,"valid edge");
  Assert(ProbeData.Limit("0")==1&&ProbeData.Limit("9999")==600&&ProbeData.Limit("bad")==120,"bounded captures");
  Assert(ProbeData.Label("a|b\nc")=="a_b_c","log field sanitation");
  var p=New();p.Sim33ms(.033f);Assert(Debug.Logs.Count==0,"opt in only");
  Set(p,"count","2");SpeedControlScreen.Instance.IsPaused=true;Call(p,"Begin",true);
  Assert(Get<int>(p,"sequence")==1,"baseline records while paused");p.Sim33ms(.033f);Assert(Get<int>(p,"sequence")==1,"paused excludes scheduled sample");
  SpeedControlScreen.Instance.IsPaused=false;p.Sim33ms(.033f);p.Sim33ms(.033f);
  Assert(!Get<bool>(p,"recording")&&Get<int>(p,"sequence")==3,"two callbacks plus baseline stop");
  int n=Debug.Logs.Count;p.Sim33ms(.033f);Assert(Debug.Logs.Count==n,"no logging after stop");
  Assert(Debug.Logs.Count(x=>x.Contains("kind=sample|"))==3,"samples logged");
  Assert(Debug.Logs.Where(x=>x.Contains("kind=sample|")).All(x=>x.Split('\n').Count(l=>l.Contains("kind=cell|"))==25),"5x5 records per target");
  Assert(Grid.Mass[12]==50e-9f&&Grid.Mass.Sum()==50e-9f,"observations do not mutate mass");
  p=New();Set(p,"source",0);Call(p,"Begin",false);Assert(!Get<bool>(p,"recording")&&Debug.Logs.Any(x=>x.Contains("watch=source")&&x.Contains("|valid=0")),"optional source and bounded edge snapshot");
  Assert(Debug.Logs.Single(x=>x.Contains("kind=sample|")).Split('\n').Count(l=>l.Contains("kind=cell|"))==50,"two neighborhoods labeled separately");
  p=New();Call(p,"Begin",true);ClusterManager.Instance.activeWorldId=2;p.Sim33ms(.033f);Assert(Debug.Logs.Last().Contains("world-or-selection-changed"),"world switch stops");
  p=New();Call(p,"Begin",true);Game.Instance.Loading=true;p.Sim33ms(.033f);Assert(Debug.Logs.Last().Contains("reason=loading"),"load stops");
  p=New();Call(p,"Begin",true);Call(p,"OnCleanUp");Assert(Debug.Logs.Last().Contains("cleanup"),"cleanup stops");
  p=New();Set(p,"target",-1);Call(p,"Begin",true);Assert(Debug.Logs.Count==0,"invalid target never records");
  p=New();Set(p,"dose",-1);Set(p,"route",999);Call(p,"Begin",false);Assert(Debug.Logs[0].Contains("|case=10 ug|route=observed-only|"),"invalid choice indexes bounded before logging");
  p=New();Set(p,"dose",-1);Set(p,"route",999);Call(p,"DrawWindow",1);Assert(Get<int>(p,"dose")==0&&Get<int>(p,"route")==0,"button UI bounds invalid selection");
  p=New();Set(p,"count","2");Call(p,"Begin",true);p.Sim33ms(.033f);p.Sim33ms(.033f);
  if(Environment.GetEnvironmentVariable("PROBE_FIXTURE") is string file)System.IO.File.WriteAllLines(file,Debug.Logs);
  Console.WriteLine(checks+" probe checks passed with offline doubles; no native runtime tested.");
 }
}
