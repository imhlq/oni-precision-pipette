using System;
using System.IO;
using System.Linq;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
foreach (var path in args) {
 using var f=File.OpenRead(path);using var pe=new PEReader(f);var r=pe.GetMetadataReader();var provider=new Types();
 Console.WriteLine("ASSEMBLY "+path);
 foreach(var h in r.TypeDefinitions) {
  var t=r.GetTypeDefinition(h);var name=r.GetString(t.Name);
  if (!(name=="Game"||name=="SimMessages"||name=="SimDLL"||name=="ReplaceType"||name=="Grid"||name=="GameClock"||name=="ISim33ms"||name=="Sim"||name.Contains("MassConsumed")||name.Contains("MassEmitted")||name.Contains("ModifyCell")||name.Contains("EmitMass")||name.Contains("ConsumeMass")))continue;
  Console.WriteLine("TYPE "+name);
  foreach(var fh in t.GetFields()) {
   var field=r.GetFieldDefinition(fh);string n=r.GetString(field.Name);
   if(name=="Grid" && !new[]{"Mass","Temperature","Element","DiseaseIdx","DiseaseCount","WorldIdx","WidthInCells","HeightInCells"}.Contains(n))continue;
   if(name=="Sim"&&!n.Contains("tick",StringComparison.OrdinalIgnoreCase)&&!n.Contains("frame",StringComparison.OrdinalIgnoreCase))continue;
   string val="";if(!field.GetDefaultValue().IsNil){var c=r.GetConstant(field.GetDefaultValue());val=" const="+Convert.ToHexString(r.GetBlobBytes(c.Value));}
   Console.WriteLine(" FIELD "+field.DecodeSignature(provider,null)+" "+n+val);
  }
  foreach(var mh in t.GetMethods()) {
   var m=r.GetMethodDefinition(mh);string n=r.GetString(m.Name);
   if(name=="Game"&&n!="OnSpawn")continue;
   if(name=="Grid"&&!new[]{"XYToCell","CellToXY","IsValidCell"}.Contains(n))continue;
   if(name=="GameClock"&&!n.Contains("Time")&&!n.Contains("Cycle"))continue;
   if(name=="Sim"&&!n.Contains("tick",StringComparison.OrdinalIgnoreCase)&&!n.Contains("frame",StringComparison.OrdinalIgnoreCase))continue;
   var sig=m.DecodeSignature(provider,null);var pars=m.GetParameters().Select(x=>r.GetParameter(x)).Where(x=>x.SequenceNumber>0).OrderBy(x=>x.SequenceNumber).ToArray();
   string ps=string.Join(", ",sig.ParameterTypes.Select((type,i)=>type+" "+(i<pars.Length?r.GetString(pars[i].Name):"?")+ (i<pars.Length&&!pars[i].GetDefaultValue().IsNil?" defaultBytes="+Convert.ToHexString(r.GetBlobBytes(r.GetConstant(pars[i].GetDefaultValue()).Value)):"")));
   string body="";if(m.RelativeVirtualAddress!=0){var bytes=pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes();body=" IL="+Convert.ToHexString(bytes.Take(12).ToArray())+" length="+bytes.Length;}
   Console.WriteLine(" METHOD "+sig.ReturnType+" "+n+"("+ps+") RVA="+m.RelativeVirtualAddress+body);
  }
 }
}
class Types:ISignatureTypeProvider<string,object> {
 public string GetArrayType(string t,ArrayShape s)=>t+"["+s.Rank+"]";public string GetByReferenceType(string t)=>t+"&";
 public string GetFunctionPointerType(MethodSignature<string> s)=>"fnptr";public string GetGenericInstantiation(string t,ImmutableArray<string>a)=>t+"<"+string.Join(",",a)+">";
 public string GetGenericMethodParameter(object c,int i)=>"!!"+i;public string GetGenericTypeParameter(object c,int i)=>"!"+i;
 public string GetModifiedType(string m,string u,bool req)=>u;public string GetPinnedType(string t)=>t;public string GetPointerType(string t)=>t+"*";
 public string GetPrimitiveType(PrimitiveTypeCode t)=>t.ToString();public string GetSZArrayType(string t)=>t+"[]";
 public string GetTypeFromDefinition(MetadataReader r,TypeDefinitionHandle h,byte k)=>r.GetString(r.GetTypeDefinition(h).Name);
 public string GetTypeFromReference(MetadataReader r,TypeReferenceHandle h,byte k)=>r.GetString(r.GetTypeReference(h).Name);
 public string GetTypeFromSpecification(MetadataReader r,object c,TypeSpecificationHandle h,byte k)=>r.GetTypeSpecification(h).DecodeSignature(this,c);
}
