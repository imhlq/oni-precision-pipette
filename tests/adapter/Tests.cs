using System;
using System.Reflection;
using OniPrecision;
class Tests {
 static int cases;
 static void Set(object p,string n,object v)=>p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,v);
 static T Get<T>(object p,string n)=>(T)p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p);
 static void Call(object p,string n,params object[] v)=>p.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(p,v);
 static void Check(bool b,string n){cases++;if(!b)throw new Exception(n);}
 static FluidPipette New(float mass=.5f){Game.Instance=new Game();ClusterManager.Instance=new ClusterManager();Db.Unlocked=false;Grid.WorldIdx[1]=1;var f=Game.Instance.gasConduitFlow;f.Cells[1]=new ConduitFlow.Contents{mass=mass,temperature=300,element=SimHashes.Hydrogen,diseaseIdx=255};f.Cells[2]=default;var p=new FluidPipette();Call(p,"OnSpawn");Set(p,"source",1);Set(p,"target",2);Set(p,"massText","250");Set(p,"unitIndex",2);return p;}
 static void Emit(FluidPipette p,int c=2,bool pipe=true,bool returning=false,bool original=false)=>Call(p,"TryEmit",c,pipe,returning,original);
 static void Main(){
  HarmonyLib.AccessTools.Missing="CanSave";new PrecisionMod().OnLoad(new HarmonyLib.Harmony());Check(!PrecisionMod.Ready,"missing hook fails closed");
  HarmonyLib.AccessTools.Missing=null;new PrecisionMod().OnLoad(new HarmonyLib.Harmony());Check(PrecisionMod.Ready,"hooks available");
  var p=New();Call(p,"StartTransfer");Check(!p.InFlight&&Get<float>(p,"heldKg")==0&&Game.Instance.gasConduitFlow.Cells[2].mass==.25f,"ordinary germ-free transfer");
  p=New();
  bool CanPick(int cell,FluidPhase phase) => p.PlanPickup(cell,true,phase,.25f,out _,out _,out _);
  Check(CanPick(1,FluidPhase.Gas) && !CanPick(2,FluidPhase.Gas) && !CanPick(1,FluidPhase.Liquid),"pickup eligibility matches occupied pipe and phase");
  Call(p,"Buffer",.125f,300f,300f,(int)SimHashes.Oxygen,(byte)255,0,2,false);
  Check(!CanPick(1,FluidPhase.Gas),"incompatible held sample blocks pickup before hold");
  Set(p,"heldHash",(int)SimHashes.Hydrogen); Set(p,"heldKg",.25f);
  Check(!CanPick(1,FluidPhase.Gas),"full reservoir blocks pickup before hold");
  Check(p.DropRefusal(2,true,FluidPhase.Gas,out _)==null,"empty compatible pipe permits placement");
  Check(p.DropRefusal(-1,false,FluidPhase.Gas,out _)!=null,"invalid map target refuses placement");
  Grid.Element[2] = ElementLoader.FindElementByHash(SimHashes.Oxygen);
  Grid.Mass[2] = 1f;
  Check(p.DropRefusal(2,false,FluidPhase.Gas,out _) != null &&
      Get<float>(p,"heldKg") == .25f, "different gas refuses without consuming the sample");
  Grid.Element[2] = ElementLoader.FindElementByHash(SimHashes.Hydrogen);
  Set(p,"heldKg",1e-9f);
  Check(p.DropRefusal(2,false,FluidPhase.Gas,out _) != null &&
      Get<float>(p,"heldKg") == 1e-9f, "sub-resolution map dose is retained");
  Set(p,"heldKg",.25f);
  Check(p.DropRefusal(2,false,FluidPhase.Gas,out _) == null, "same gas accepts a representable dose");
  Grid.Element[2] = ElementLoader.FindElementByHash(SimHashes.Vacuum);
  Grid.Mass[2] = 0f;
  Game.Instance.gasConduitFlow.MaxMass=.1f;
  Check(p.DropRefusal(2,true,FluidPhase.Gas,out _)!=null && Get<float>(p,"heldKg")==.25f,"capacity preview rejects without mutating buffer");
  p=New(); Game.Instance.gasConduitFlow.MaxMass=.1f; Call(p,"StartTransfer"); Check(Game.Instance.gasConduitFlow.Cells[1].mass==.5f && Get<float>(p,"heldKg")==0,"runtime pipe capacity checked before debit");
  p=New(); Call(p,"Buffer",.125f,280f,280f,(int)SimHashes.Water,(byte)255,0,1,false); Call(p,"Buffer",.125f,320f,320f,(int)SimHashes.Water,(byte)255,0,2,false); Check(Get<float>(p,"heldKg")==.25f && Get<float>(p,"originalK")==300f,"top-up conserves sample mass and heat"); Call(p,"SelectReservoir",FluidPhase.Gas); Check(Get<float>(p,"heldKg")==0,"gas tool opens with liquid held"); Call(p,"SelectReservoir",FluidPhase.Liquid); Check(Get<float>(p,"heldKg")==.25f,"switching back restores liquid");
  p=New(.01f);Set(p,"massText","50");Set(p,"unitIndex",0);Call(p,"StartTransfer");Check(!p.InFlight&&Game.Instance.gasConduitFlow.Cells[2].mass>0,"50 microgram gas dose");
  p=New();Set(p,"targetPipe",false);Call(p,"StartTransfer");Check(Game.Instance.gasConduitFlow.Cells[1].mass==.5f&&!p.InFlight,"pipe to map refuses before debit");
  p=New();Set(p,"sourcePipe",false);Call(p,"StartTransfer");Check(Game.Instance.gasConduitFlow.Cells[2].mass==0&&!p.InFlight,"map source refuses before debit");
  p=New();var f=Game.Instance.gasConduitFlow;f.Refuse=true;Call(p,"StartTransfer");Check(Get<float>(p,"heldKg")==.25f&&!p.InFlight,"confirmed zero acceptance retains buffer");
  f.Refuse=false;Emit(p,1,true,true);Check(f.Cells[1].mass==.5f&&Get<float>(p,"heldKg")==0,"verified origin return");
  p=New();f=Game.Instance.gasConduitFlow;var src=f.Cells[1];src.diseaseIdx=2;src.diseaseCount=22;f.Cells[1]=src;f.Partial=.5f;Call(p,"StartTransfer");Check(Get<float>(p,"heldKg")==.125f&&Get<int>(p,"germCount")==6&&!p.InFlight,"partial receipt uses actual five germs delivered");
  f.Partial=1;Emit(p);Check(Get<float>(p,"heldKg")==0&&f.Cells[2].diseaseCount==11,"partial retry no duplication");
  p=New();Set(p,"heldKg",0f);Set(p,"germCount",123);Set(p,"outputK",float.NaN);Call(p,"OnSpawn");Check(!Get<bool>(p,"invalidBuffer")&&Get<int>(p,"germCount")==0,"v0.2 empty stale metadata");
  p=New();Set(p,"heldKg",.125f);Set(p,"originalK",300f);Set(p,"outputK",248f);Set(p,"heldHash",1);Set(p,"originalCell",1);Set(p,"originalPipe",true);Call(p,"OnSpawn");Emit(p,1,true,true);Check(Get<float>(p,"heldKg")==.125f,"legacy return refuses");Emit(p);Check(Get<float>(p,"heldKg")==.125f,"legacy tempered dose research gated");Emit(p,2,true,false,true);Check(Get<float>(p,"heldKg")==0,"legacy original-temp delivery");
  p=New();f=Game.Instance.gasConduitFlow;f.Refuse=true;Call(p,"StartTransfer");Grid.WorldIdx[1]=2;f.Refuse=false;Emit(p,1,true,true);Check(Get<float>(p,"heldKg")==.25f,"changed origin world refuses");
  p=New();Game.Instance.gasConduitFlow.ThrowRemove=true;Call(p,"StartTransfer");Check(p.InFlight&&ClusterManager.Instance.blockers==1,"debit exception quarantines");
  p=New();Game.Instance.gasConduitFlow.ThrowAdd=true;Call(p,"StartTransfer");Check(p.InFlight&&Get<float>(p,"heldKg")==.25f,"credit exception preserves unknown buffer");Game.Instance.gasConduitFlow.ThrowAdd=false;Emit(p);Check(Game.Instance.gasConduitFlow.Cells[2].mass==0,"uncertain credit never retries");Call(p,"OnCleanUp");Check(ClusterManager.Instance.blockers==0,"cleanup removes owned blocker");
  p=New();Game.Instance.gasConduitFlow.WrongReceipt=true;Call(p,"StartTransfer");Check(p.InFlight&&Get<float>(p,"heldKg")==.25f,"invalid native receipt quarantines");
  p=New();f=Game.Instance.gasConduitFlow;src=f.Cells[1];src.diseaseCount=22;src.diseaseIdx=2;f.Cells[1]=src;var dst=f.Cells[2];dst.mass=.125f;dst.temperature=300;dst.element=SimHashes.Hydrogen;dst.diseaseCount=3;dst.diseaseIdx=3;f.Cells[2]=dst;Call(p,"StartTransfer");Check(!p.InFlight&&Get<float>(p,"heldKg")==.25f&&f.Cells[2].mass==.125f,"different germ IDs refuse without credit");
  p=New();f=Game.Instance.gasConduitFlow;f.Refuse=true;Call(p,"StartTransfer");object[] saveResult={true};typeof(PrecisionMod).GetMethod("CheckSave",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,saveResult);Check((bool)saveResult[0],"confirmed held buffer permits save");
  f.Refuse=false;f.ThrowAdd=true;Emit(p);saveResult[0]=true;typeof(PrecisionMod).GetMethod("CheckSave",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,saveResult);Check(!(bool)saveResult[0],"uncertainty blocks CanSave");
  p=New(.01f);Set(p,"massText","50");Set(p,"unitIndex",0);Set(p,"tempered",true);Call(p,"StartTransfer");Check(Game.Instance.gasConduitFlow.Cells[1].mass==.01f,"new temperature request locked before debit");Db.Unlocked=true;Call(p,"StartTransfer");Check(!p.InFlight&&Math.Abs(Game.Instance.gasConduitFlow.Cells[2].temperature-248.15f)<.001f,"researched microgram temperature transfer");
  p=New();Game.Instance.liquidConduitFlow.Cells[1]=new ConduitFlow.Contents{mass=2,temperature=300,element=SimHashes.Water,diseaseIdx=255};Game.Instance.liquidConduitFlow.Cells[2]=default;Set(p,"mode",FluidPhase.Liquid);Set(p,"massText","30");Set(p,"tempered",true);Db.Unlocked=true;Call(p,"StartTransfer");Check(Game.Instance.liquidConduitFlow.Cells[2].temperature==300,"default 30 g retains source temperature");Game.Instance.liquidConduitFlow.Cells[2]=default;FluidPipette.UnlimitedTemperatureMass=true;Call(p,"StartTransfer");Check(!p.InFlight&&Math.Abs(Game.Instance.liquidConduitFlow.Cells[2].temperature-248.15f)<.001f&&Game.Instance.liquidConduitFlow.Cells[2].mass>.029f&&Get<float>(p,"heldKg")==0,"30 gram liquid dose");
  p=New(); Set(p,"unitIndex",-1); Call(p,"StartTransfer");
  Check(Game.Instance.gasConduitFlow.Cells[1].mass==.5f,"invalid dose unit fails closed instead of throwing");
  Console.WriteLine($"{cases} adapter scenarios passed against offline doubles (not the native runtime).");
 }
}
