using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
foreach(var path in args) {
 using var stream=File.OpenRead(path);using var pe=new PEReader(stream);var reader=pe.GetMetadataReader();
 foreach(var required in new[]{("SaveGame","OnPrefabInit"),("Game","CanSave"),("ClusterManager","AddSaveBlocker"),("ClusterManager","RemoveSaveBlocker"),("ClusterManager","get_CanSave")}) {
  var type=reader.TypeDefinitions.Select(h=>reader.GetTypeDefinition(h)).Single(t=>reader.GetString(t.Name)==required.Item1);
  var methods=type.GetMethods().Select(h=>reader.GetMethodDefinition(h)).Where(m=>reader.GetString(m.Name)==required.Item2).ToArray();
  if(methods.Length!=1) {
   if(required.Item1=="ClusterManager") {Console.WriteLine(path+": optional API absent: "+required.Item2);continue;}
   throw new Exception("Missing/ambiguous declared hook: "+required);
  }
  if(methods[0].GetGenericParameters().Count!=0)throw new Exception("Generic hook: "+required);
 }
 Console.WriteLine(path+": required declared hooks present; optional native APIs inspected (metadata only).");
}
