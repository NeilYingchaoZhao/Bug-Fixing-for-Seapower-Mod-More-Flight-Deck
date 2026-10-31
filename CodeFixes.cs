using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SeaPower;
using UnityEngine;
using Graph = FlightDeckOpsMod.DeckOpsTaxiGraph.Graph;
using Edge = FlightDeckOpsMod.DeckOpsTaxiGraph.Edge;
using Node = FlightDeckOpsMod.DeckOpsTaxiGraph.Node;

namespace FlightDeckOpsMod {
public static class CodexFixes {
 public const string PatchVersion="2026-10-31.1";
 static readonly ConditionalWeakTable<List<Vector3>,List<bool>> Directions=new ConditionalWeakTable<List<Vector3>,List<bool>>();
 static readonly ConditionalWeakTable<Graph,Dictionary<string,bool>> GraphFlags=new ConditionalWeakTable<Graph,Dictionary<string,bool>>();
 static readonly ConditionalWeakTable<object,HashSet<string>> AutoFlags=new ConditionalWeakTable<object,HashSet<string>>();
 static readonly Dictionary<int,float> NextTry=new Dictionary<int,float>();

 // Metadata follows list operations, including route copies and pushout substitutions.
 public static void AddStep(List<Vector3> route,Vector3 p,bool reverse){List<bool> flags=Directions.GetOrCreateValue(route);while(flags.Count<route.Count)flags.Add(false);route.Add(p);flags.Add(reverse);}
 public static bool DirectionAt(List<Vector3> route,int i){List<bool> flags;return route!=null&&Directions.TryGetValue(route,out flags)&&i>=0&&i<flags.Count&&flags[i];}
 public static void VectorAdd(List<Vector3> route,Vector3 p){List<bool> flags;bool tracked=Directions.TryGetValue(route,out flags);route.Add(p);if(tracked)flags.Add(false);}
 public static void VectorAddRange(List<Vector3> route,IEnumerable<Vector3> source){List<Vector3> list=source as List<Vector3>;List<bool> flags;List<bool> copied=null;if(list!=null&&Directions.TryGetValue(list,out flags))copied=new List<bool>(flags);int old=route.Count;route.AddRange(source);List<bool> target;if(copied!=null){target=Directions.GetOrCreateValue(route);while(target.Count<old)target.Add(false);for(int i=0;i<route.Count-old;i++)target.Add(i<copied.Count&&copied[i]);}else if(Directions.TryGetValue(route,out target)){while(target.Count<route.Count)target.Add(false);}}
 public static void VectorClear(List<Vector3> route){route.Clear();Directions.Remove(route);}
 public static void VectorRemoveAt(List<Vector3> route,int index){route.RemoveAt(index);List<bool> flags;if(Directions.TryGetValue(route,out flags)&&index<flags.Count)flags.RemoveAt(index);}
 public static void VectorInsert(List<Vector3> route,int index,Vector3 p){route.Insert(index,p);List<bool> flags;if(Directions.TryGetValue(route,out flags))flags.Insert(index,false);}
 public static void VectorSet(List<Vector3> route,int index,Vector3 p){route[index]=p;}

 static Dictionary<string,bool> FlagsFor(FlightDeck deck,Graph graph){Dictionary<string,bool> result;if(GraphFlags.TryGetValue(graph,out result))return result;result=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);string used;Dictionary<string,Dictionary<string,string>> raw=DeckOpsTaxiGraph.ReadRawFile(deck,out used);if(raw!=null)foreach(KeyValuePair<string,Dictionary<string,string>> pair in raw){string value;if(pair.Value.TryGetValue("IsBackwards",out value))result[pair.Key]=value.Equals("true",StringComparison.OrdinalIgnoreCase)||value=="1";}GraphFlags.Add(graph,result);return result;}
 static bool ReverseOf(Dictionary<string,bool> flags,Edge edge){bool value;return flags.TryGetValue(edge.Name,out value)&&value;}

 public static List<Vector3> FindRoute(FlightDeck deck,FlightDeckPoint from,FlightDeckPoint to){
  if(deck==null||from==null||to==null||from==to||string.IsNullOrEmpty(from._sectionName)||string.IsNullOrEmpty(to._sectionName))return null;
  Graph graph=DeckOpsTaxiGraph.Get(deck);if(graph==null||graph.Edges.Count==0)return null;
  Node start,goal;
  if(!graph.Nodes.TryGetValue(from._sectionName,out start)||!start.HasPos)start=DeckOpsTaxiGraph.NearestNode(graph,from._position);
  if(!graph.Nodes.TryGetValue(to._sectionName,out goal)||!goal.HasPos)goal=DeckOpsTaxiGraph.NearestNode(graph,to._position);
  if(start==null||goal==null)return null;
  Dictionary<string,float> costs=new Dictionary<string,float>(StringComparer.OrdinalIgnoreCase);
  Dictionary<string,Edge> previous=new Dictionary<string,Edge>(StringComparer.OrdinalIgnoreCase);
  HashSet<string> seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);List<string> todo=new List<string>();costs[start.Name]=0;todo.Add(start.Name);
  while(todo.Count>0){int best=0;for(int i=1;i<todo.Count;i++)if(costs[todo[i]]<costs[todo[best]])best=i;string at=todo[best];todo.RemoveAt(best);if(!seen.Add(at))continue;if(at.Equals(goal.Name,StringComparison.OrdinalIgnoreCase))break;List<Edge> next;if(!graph.Out.TryGetValue(at,out next))continue;
   foreach(Edge edge in next){if(!edge.To.Equals(goal.Name,StringComparison.OrdinalIgnoreCase)&&DeckOpsZones.IsBlockedNow(deck,edge.To))continue;float score=costs[at]+edge.Len,old;if(costs.TryGetValue(edge.To,out old)&&score>=old)continue;costs[edge.To]=score;previous[edge.To]=edge;if(!seen.Contains(edge.To))todo.Add(edge.To);}
  }
  if(!previous.ContainsKey(goal.Name))return null;
  List<Edge> path=new List<Edge>();string current=goal.Name;while(!current.Equals(start.Name,StringComparison.OrdinalIgnoreCase)){Edge edge;if(!previous.TryGetValue(current,out edge))return null;path.Add(edge);current=edge.From;}path.Reverse();
  Dictionary<string,bool> flags=FlagsFor(deck,graph);List<Vector3> result=new List<Vector3>();bool backwards=false;
  foreach(Edge edge in path){backwards=ReverseOf(flags,edge);foreach(Vector3 p in edge.Wps)AddStep(result,p,backwards);Node n;if(edge.To.StartsWith("TaxiPoint",StringComparison.OrdinalIgnoreCase)&&graph.Nodes.TryGetValue(edge.To,out n)&&n.HasPos)AddStep(result,n.Pos,backwards);}
  if(result.Count==0||(result[result.Count-1]-goal.Pos).sqrMagnitude>0.000001f)AddStep(result,goal.Pos,backwards);
  return result;
 }
 public static List<Vector3> BuildRoute(FlightDeck deck,FlightDeckPoint from,FlightDeckPoint to){
  List<Vector3> empty=new List<Vector3>();if(deck==null||from==null||to==null)return empty;
  try{FlightDeckOpsConfig config=FlightDeckOpsConfig.Loaded;if(config==null||config.UseOwnTaxiGraph){List<Vector3> own=FindRoute(deck,from,to);if(own!=null&&own.Count>0)return own;}
   List<TaxiPath> paths=DeckOpsMove.ShortestTaxiRoute(deck,from,to);if(paths==null||paths.Count==0)return empty;bool backwards=false;
   foreach(TaxiPath p in paths){backwards=p._isBackwards;if(p._waypoints!=null)foreach(Vector3 v in p._waypoints)AddStep(empty,v,backwards);if(p._to is TaxiPoint)AddStep(empty,p._to._position,backwards);}
   if(empty.Count==0||(empty[empty.Count-1]-to._position).sqrMagnitude>0.000001f)AddStep(empty,to._position,backwards);
  }catch(Exception ex){FlightDeckOpsDebug.Warn("FlightDeckOps reverse-route patch: "+ex.Message);}return empty;
 }
 // Do not jump over an explicit reverse departure by selecting its forward pushout as the route origin.
 public static FlightDeckPoint DispatchAtPushOut(FlightDeck deck,Vector3 pushout){
  try{Graph graph=DeckOpsTaxiGraph.Get(deck);if(graph!=null){string used;var raw=DeckOpsTaxiGraph.ReadRawFile(deck,out used);var flags=FlagsFor(deck,graph);foreach(var sec in raw){Vector3 p;if(!DeckOpsTaxiGraph.ReadVec(sec.Value,"DeckOpsPushOutPosition",out p)||(p-pushout).sqrMagnitude>0.00000001f)continue;Node origin;if(!graph.Nodes.TryGetValue(sec.Key,out origin))continue;var todo=new Queue<string>();var seen=new HashSet<string>();todo.Enqueue(origin.Name);while(todo.Count>0){string name=todo.Dequeue();if(!seen.Add(name))continue;List<Edge> edges;if(!graph.Out.TryGetValue(name,out edges))continue;foreach(Edge edge in edges){if(edge.Len<0.00001f)todo.Enqueue(edge.To);else if(ReverseOf(flags,edge))return null;}}}}}
  catch(Exception ex){FlightDeckOpsDebug.Diag("Reverse dispatch selection: "+ex.Message);}return DeckOpsMove.NodeAtPushOut(deck,pushout);
 }
 public static float CornerTolerance(DeckOpsAircraft e){if(e!=null&&e.Route.Count>1&&DirectionAt(e.Route,0)!=DirectionAt(e.Route,1))return 0.007440469f;return 0.07440469f;}
 public static void ApplyRouteDrive(Taxi taxi,DeckOpsAircraft e){
  if(taxi==null||e==null||e.Docking||e.Turning||e.TaxiReversing||e.TaxiPaused||e.Route.Count==0)return;
  List<bool> tracked;if(!Directions.TryGetValue(e.Route,out tracked))return;
  MotionController mc=taxi._motionController;if((UnityEngine.Object)mc==null)return;
  bool reverse=DirectionAt(e.Route,0);bool changed=taxi._isBackwards!=reverse;taxi._isBackwards=reverse;DeckOpsDock.SetBackTaxi(mc,reverse);
  if(changed||Mathf.Abs(mc.CommandVelocity)<0.05f)DeckOpsDock.SetDriveSpeed(mc,e.Deck,reverse);
 }
 public static void ResetRouteDrive(Taxi taxi,DeckOpsAircraft e){if(taxi==null||e==null||e.TaxiReversing)return;taxi._isBackwards=false;if((UnityEngine.Object)taxi._motionController!=null)DeckOpsDock.SetBackTaxi(taxi._motionController,false);}

 public static bool Eligible(bool patrolAssigned,bool explicitAutoOrder,bool manuallyCancelled){return !manuallyCancelled&&(patrolAssigned||explicitAutoOrder);}
 static bool Flag(DeckOpsAircraft e,string flag){HashSet<string> flags;return e!=null&&!object.ReferenceEquals(e.Vehicle,null)&&AutoFlags.TryGetValue(e.Vehicle,out flags)&&flags.Contains(flag);}
 public static void MarkSpawned(ObjectBase vehicle,LaunchTaskParameters ltp){if(object.ReferenceEquals(vehicle,null)||ltp==null)return;if((int)ltp._missionType==7&&!string.IsNullOrEmpty(ltp._patrolZoneID))AutoFlags.GetOrCreateValue(vehicle).Add("auto");}
 public static void SetManualCancellation(object vehicle,bool result){if(result&&vehicle!=null)AutoFlags.GetOrCreateValue(vehicle).Add("cancelled");}
 public static bool IsManualCancelled(object vehicle){HashSet<string> flags;return vehicle!=null&&AutoFlags.TryGetValue(vehicle,out flags)&&flags.Contains("cancelled");}
 public static void ResumeVehicle(object vehicle){if(vehicle==null)return;HashSet<string> flags;if(AutoFlags.TryGetValue(vehicle,out flags))flags.Remove("cancelled");}
 public static bool AfterCancel(bool result,DeckOpsAircraft e){if(e!=null)SetManualCancellation(e.Vehicle,result);return result;}
 public static bool PermitLaunch(DeckOpsAircraft e){return !Flag(e,"cancelled");}
 static void Resume(DeckOpsAircraft e){if(e!=null)ResumeVehicle(e.Vehicle);}
 public static bool ManualAssignLaunch(DeckOpsAircraft e){Resume(e);return DeckOpsZones.AssignLaunch(e);}
 public static bool ManualAssignLaunchTo(DeckOpsAircraft e,string section){Resume(e);return DeckOpsZones.AssignLaunchTo(e,section);}
 public static bool ManualLaunchFromLaunchPoint(DeckOpsAircraft e){Resume(e);return DeckOpsZones.LaunchFromLaunchPoint(e);}
 public static void AutoTick(float dt){
  try{FlightDeckOpsConfig config=FlightDeckOpsConfig.Loaded;if(config==null||!config.AirOpsAutoSortie){NextTry.Clear();return;}
   float now=Time.unscaledTime,interval=Mathf.Max(.5f,config.AirOpsSortieCheckSeconds),fuelTarget=Mathf.Clamp01(config.AirOpsCAPFuelFraction<=0?.95f:config.AirOpsCAPFuelFraction);
   var all=FlightDeckOpsRuntime.Snapshot();for(int i=0;i<all.Count;i++){DeckOpsAircraft e=all[i];if(e==null||(UnityEngine.Object)e.Vehicle==null||e.Vehicle.IsDestroyed)continue;
    bool patrol=e.Vehicle._patrolZone!=null&&(int)e.Vehicle._patrolZone._zoneType==1;
    if(!patrol){try{var formation=e.Vehicle.Formation;ObjectBase leader=formation!=null&&formation.LeaderStation!=null?formation.LeaderStation.UnitObject:null;if((UnityEngine.Object)leader!=null&&leader!=e.Vehicle&&leader._patrolZone!=null&&(int)leader._patrolZone._zoneType==1)patrol=true;}catch{}}
    if(!Eligible(patrol,Flag(e,"auto"),Flag(e,"cancelled"))||e.Deck==null||!DeckOpsRegistry.IsEnabled(e.Deck))continue;
    if(e.Phase!=DeckOpsPhase.OnElevator&&e.Phase!=DeckOpsPhase.AtParking&&e.Phase!=DeckOpsPhase.AtFueling&&e.Phase!=DeckOpsPhase.AtRearming&&e.Phase!=DeckOpsPhase.AtLaunchReady)continue;
    if(e.Phase==DeckOpsPhase.AtRearming&&e.Job==DeckOpsSpotJob.Service&&e.ServiceDuration<=0&&e.ServiceElapsed<=0)e.Job=DeckOpsSpotJob.None;
    float next;if(NextTry.TryGetValue(e.Vehicle.UniqueID,out next)&&now<next)continue;NextTry[e.Vehicle.UniqueID]=now+interval;
    if(e.Phase==DeckOpsPhase.AtLaunchReady){if(config.AutoLaunchOnArrival)DeckOpsZones.LaunchFromLaunchPoint(e);continue;}
    float fuel=DeckOpsZones.FuelFraction(e);if(fuel<fuelTarget){if((e.Phase!=DeckOpsPhase.AtFueling||e.Job==DeckOpsSpotJob.None)&&(e.Phase!=DeckOpsPhase.AtRearming||e.Job!=DeckOpsSpotJob.Service)&&!DeckOpsZones.AssignZone(e,DeckOpsRole.Fueling)&&fuel>=DeckOpsTuning.LaunchFuelThreshold(e.Deck))DeckOpsZones.AssignLaunch(e);}
    else if(e.Phase!=DeckOpsPhase.AtRearming||e.Job!=DeckOpsSpotJob.Service)DeckOpsZones.AssignLaunch(e);
   }
  }catch(Exception ex){FlightDeckOpsDebug.Warn("FlightDeckOps automatic sortie patch: "+ex.Message);}
 }
}
}