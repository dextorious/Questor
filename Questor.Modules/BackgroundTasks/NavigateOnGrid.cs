

namespace Questor.Modules.BackgroundTasks
{
    using System;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Combat;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using System.Linq;
    using System.Collections.Generic;

    using DirectEve;

    public static class NavigateOnGrid
    {
        public static DateTime AvoidBumpingThingsTimeStamp = Time.Instance.StartTime;
        public static int SafeDistanceFromStructureMultiplier = 1;
        public static bool AvoidBumpingThingsWarningSent = false;
        public static DateTime NextNavigateIntoRange = DateTime.UtcNow;
        public static bool AvoidBumpingThingsBool { get; set; }
        public static bool SpeedTank { get; set; }
        private static int? _orbitDistance;
        public static int OrbitDistance
        {
            get
            {
                if (MissionSettings.MissionOrbitDistance != null)
                {
                    return (int)MissionSettings.MissionOrbitDistance;
                }

                return _orbitDistance ?? 2000;
            }
            set
            {
                _orbitDistance = value;
            }
        }
        public static bool OrbitStructure { get; set; }
        private static int? _optimalRange { get; set; }
        public static int OptimalRange
        {
            get
            {
                if (MissionSettings.MissionOptimalRange != null)
                {
                    return (int)MissionSettings.MissionOptimalRange;
                }

                return _optimalRange ?? 10000;
            }
            set
            {
                _optimalRange = value;
            }
        }
        
        public static bool AvoidBumpingThings(EntityCache thisBigObject, string module)
        {
            if (AvoidBumpingThingsBool)
            {
                //if It has not been at least 60 seconds since we last session changed do not do anything
                if (Cache.Instance.InStation || !Cache.Instance.InSpace || Cache.Instance.ActiveShip.Entity.IsCloaked || (Cache.Instance.InSpace && Time.Instance.LastSessionChange.AddSeconds(60) < DateTime.UtcNow))
                    return false;

                //we cant move in bastion mode, do not try
                List<ModuleCache> bastionModules = null;
                bastionModules = Cache.Instance.Modules.Where(m => m.GroupId == (int)Group.Bastion && m.IsOnline).ToList();
                if (bastionModules.Any(i => i.IsActive)) return false;

                
                if (Cache.Instance.ClosestStargate != null && Cache.Instance.ClosestStargate.Distance < 9000)
                {
                    //
                    // if we are 'close' to a stargate or a station do not attempt to do any collision avoidance, as its unnecessary that close to a station or gate!
                    //
                    return false;
                }
                    
                if (Cache.Instance.ClosestStation != null && Cache.Instance.ClosestStation.Distance < 11000)
                {
                    //
                    // if we are 'close' to a stargate or a station do not attempt to do any collision avoidance, as its unnecessary that close to a station or gate!
                    //
                    return false;
                }
                
                //EntityCache thisBigObject = Cache.Instance.BigObjects.FirstOrDefault();
                if (thisBigObject != null)
                {
                    //
                    // if we are "too close" to the bigObject move away... (is orbit the best thing to do here?)
                    //
                    if (thisBigObject.Distance >= (int)Distances.TooCloseToStructure)
                    {
                        //we are no longer "too close" and can proceed.
                        AvoidBumpingThingsTimeStamp = DateTime.UtcNow;
                        SafeDistanceFromStructureMultiplier = 1;
                        AvoidBumpingThingsWarningSent = false;
                    }
                    else
                    {
                        if (DateTime.UtcNow > Time.Instance.NextOrbit)
                        {
                            if (DateTime.UtcNow > AvoidBumpingThingsTimeStamp.AddSeconds(30))
                            {
                                if (SafeDistanceFromStructureMultiplier <= 4)
                                {
                                    //
                                    // for simplicities sake we reset this timestamp every 30 sec until the multiplier hits 5 then it should stay static until we are not "too close" anymore
                                    //
                                    AvoidBumpingThingsTimeStamp = DateTime.UtcNow;
                                    SafeDistanceFromStructureMultiplier++;
                                }

                                if (DateTime.UtcNow > AvoidBumpingThingsTimeStamp.AddMinutes(5) && !AvoidBumpingThingsWarningSent)
                                {
                                    Logging.Log("NavigateOnGrid", "We are stuck on a object and have been trying to orbit away from it for over 5 min", Logging.Orange);
                                    AvoidBumpingThingsWarningSent = true;
                                }

                                if (DateTime.UtcNow > AvoidBumpingThingsTimeStamp.AddMinutes(15))
                                {
                                    Cache.Instance.CloseQuestorCMDLogoff = false;
                                    Cache.Instance.CloseQuestorCMDExitGame = true;
                                    Cache.Instance.ReasonToStopQuestor = "navigateOnGrid: We have been stuck on an object for over 15 min";
                                    Logging.Log("ReasonToStopQuestor", Cache.Instance.ReasonToStopQuestor, Logging.Yellow);
                                    Cache.Instance.SessionState = "Quitting";
                                }
                            }

                            if (thisBigObject.Orbit((int) Distances.SafeDistancefromStructure * SafeDistanceFromStructureMultiplier))
                            {
                                Logging.Log(module, ": initiating Orbit of [" + thisBigObject.Name + "] orbiting at [" + ((int)Distances.SafeDistancefromStructure * SafeDistanceFromStructureMultiplier) + "]", Logging.White);
                                return true;
                            }

                            return false;
                        }

                        return false;
                        //we are still too close, do not continue through the rest until we are not "too close" anymore
                    }

                    return false;
                }

                return false;
            }

            return false;
        }

        public static void OrbitGateorTarget(EntityCache target, string module)
        {
            if (DateTime.UtcNow > Time.Instance.NextOrbit)
            {
                //we cant move in bastion mode, do not try
                List<ModuleCache> bastionModules = null;
                bastionModules = Cache.Instance.Modules.Where(m => m.GroupId == (int)Group.Bastion && m.IsOnline).ToList();
                if (bastionModules.Any(i => i.IsActive)) return;

                if (Logging.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "OrbitGateorTarget Started", Logging.White);
                if (NavigateOnGrid.OrbitDistance == 0)
                {
                    NavigateOnGrid.OrbitDistance = 2000;
                }

                if (target.Distance + NavigateOnGrid.OrbitDistance < Combat.MaxRange - 5000)
                {
                    if (Logging.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "if (target.Distance + Cache.Instance.OrbitDistance < Combat.MaxRange - 5000)", Logging.White);

                    //Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction] ,"StartOrbiting: Target in range");
                    if (!Cache.Instance.IsApproachingOrOrbiting(target.Id))
                    {
                        if (Logging.DebugNavigateOnGrid) Logging.Log("CombatMissionCtrl.NavigateIntoRange", "We are not approaching nor orbiting", Logging.Teal);

                        //
                        // Prefer to orbit the last structure defined in 
                        // Cache.Instance.OrbitEntityNamed
                        //
                        EntityCache structure = null;
                        if (!string.IsNullOrEmpty(Cache.Instance.OrbitEntityNamed))
                        {
                            structure = Cache.Instance.EntitiesOnGrid.Where(i => i.Name.Contains(Cache.Instance.OrbitEntityNamed)).OrderBy(t => t.Distance).FirstOrDefault();
                        }

                        if (structure == null)
                        {
                            structure = Cache.Instance.EntitiesOnGrid.Where(i => i.Name.Contains("Gate")).OrderBy(t => t.Distance).FirstOrDefault();
                        }

                        if (NavigateOnGrid.OrbitStructure && structure != null)
                        {
                            if (structure.Orbit(NavigateOnGrid.OrbitDistance))
                            {
                                Logging.Log(module, "Initiating Orbit [" + structure.Name + "][at " + Math.Round((double)NavigateOnGrid.OrbitDistance / 1000, 0) + "k][" + structure.MaskedId + "]", Logging.Teal);
                                return;    
                            }

                            return;
                        }

                        //
                        // OrbitStructure is false
                        //
                        if (NavigateOnGrid.SpeedTank)
                        {
                            if (target.Orbit(NavigateOnGrid.OrbitDistance))
                            {
                                Logging.Log(module, "Initiating Orbit [" + target.Name + "][at " + Math.Round((double)NavigateOnGrid.OrbitDistance / 1000, 0) + "k][ID: " + target.MaskedId + "]", Logging.Teal);
                                return;    
                            }
                            
                            return;
                        }

                        //
                        // OrbitStructure is false
                        // SpeedTank is false
                        //
                        if (Cache.Instance.MyShipEntity.Velocity < 300) //this will spam a bit until we know what "mode" our active ship is when aligning
                        {
                            if (Combat.DoWeCurrentlyHaveTurretsMounted())
                            {
                                if (Cache.Instance.Star.AlignTo())
                                {
                                    Logging.Log(module, "Aligning to the Star so we might possibly hit [" + target.Name + "][ID: " + target.MaskedId + "][ActiveShip.Entity.Mode:[" + Cache.Instance.ActiveShip.Entity.Mode + "]", Logging.Teal);
                                    return;                                    
                                }

                                return;
                            }
                        }
                    }
                }
                else
                {
                    if (target.Orbit(NavigateOnGrid.OrbitDistance))
                    {
                        Logging.Log(module, "Out of range. ignoring orbit around structure.", Logging.Teal);
                        return;
                    }

                    return;
                }

                return;
            }
        }

        public static void NavigateIntoRange(EntityCache target, string module, bool moveMyShip)
        {
            if (!Cache.Instance.InSpace || (Cache.Instance.InSpace && Cache.Instance.InWarp) || !moveMyShip)
                return;

            if (DateTime.UtcNow < NextNavigateIntoRange || Logging.DebugDisableNavigateIntoRange)
                return;

            NextNavigateIntoRange = DateTime.UtcNow.AddSeconds(5);

            //we cant move in bastion mode, do not try
            List<ModuleCache> bastionModules = null;
            bastionModules = Cache.Instance.Modules.Where(m => m.GroupId == (int)Group.Bastion && m.IsOnline).ToList();
            if (bastionModules.Any(i => i.IsActive)) return;

            if (Logging.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange Started", Logging.White);

            //if (Cache.Instance.OrbitDistance != 0)
            //    Logging.Log("CombatMissionCtrl", "Orbit Distance is set to: " + (Cache.Instance.OrbitDistance / 1000).ToString(CultureInfo.InvariantCulture) + "k", Logging.teal);

            NavigateOnGrid.AvoidBumpingThings(Cache.Instance.BigObjectsandGates.FirstOrDefault(), "NavigateOnGrid: NavigateIntoRange");

            if (NavigateOnGrid.SpeedTank)
            {
                if (target.Distance > Combat.MaxRange && !Cache.Instance.IsApproaching(target.Id))
                {
                    if (target.KeepAtRange((int) (Combat.MaxRange*0.8d)))
                    {
                        if (Logging.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: SpeedTank: Moving into weapons range before initiating orbit", Logging.Teal);    
                    }

                    return;
                }
                if (target.Distance < Combat.MaxRange && !Cache.Instance.IsOrbiting(target.Id))
                {
                    if (Logging.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: SpeedTank: orbitdistance is [" + NavigateOnGrid.OrbitDistance + "]", Logging.White);
                    OrbitGateorTarget(target, module);
                    return;
                }
                return;
            }
            else //if we are not speed tanking then check optimalrange setting, if that is not set use the less of targeting range and weapons range to dictate engagement range
            {
                if (DateTime.UtcNow > Time.Instance.NextApproachAction)
                {
                    //if optimalrange is set - use it to determine engagement range
                    if (NavigateOnGrid.OptimalRange != 0)
                    {
                        if (Logging.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: OptimalRange [ " + NavigateOnGrid.OptimalRange + "] Current Distance to [" + target.Name + "] is [" + Math.Round(target.Distance / 1000, 0) + "]", Logging.White);

                        if (target.Distance > NavigateOnGrid.OptimalRange + (int)Distances.OptimalRangeCushion)
                        {
                            if ((Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id) || Cache.Instance.MyShipEntity.Velocity < 50)
                            {
                                if (target.IsNPCFrigate && Combat.DoWeCurrentlyHaveTurretsMounted())
                                {
                                    if (Logging.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: target is NPC Frigate [" + target.Name + "][" + Math.Round(target.Distance / 1000, 0) + "]", Logging.White);
                                    OrbitGateorTarget(target, module);
                                    return;
                                }

                                if (target.KeepAtRange(NavigateOnGrid.OptimalRange))
                                {
                                    Logging.Log(module, "Using Optimal Range: Approaching target [" + target.Name + "][ID: " + target.MaskedId + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);   
                                }

                                return;    
                            }
                        }

                        if (target.Distance <= NavigateOnGrid.OptimalRange)
                        {
                            if (target.IsNPCFrigate && Combat.DoWeCurrentlyHaveTurretsMounted())
                            {
                                if ((Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id) || Cache.Instance.MyShipEntity.Velocity < 50)
                                {
                                    if (target.KeepAtRange(NavigateOnGrid.OptimalRange))
                                    {
                                        Logging.Log(module, "Target is NPC Frigate and we got Turrets. Keeping target at Range to hit it.", Logging.Teal);
                                        Logging.Log(module, "Initiating KeepAtRange [" + target.Name + "][at " + Math.Round((double)NavigateOnGrid.OptimalRange / 1000, 0) + "k][ID: " + target.MaskedId + "]", Logging.Teal);    
                                    }
                                    return;    
                                }
                            }
                            else if (Cache.Instance.Approaching != null && Cache.Instance.MyShipEntity.Velocity != 0)
                            {
                                if (target.IsNPCFrigate && Combat.DoWeCurrentlyHaveTurretsMounted()) return;

                                StopMyShip();
                                Logging.Log(module, "Using Optimal Range: Stop ship, target at [" + Math.Round(target.Distance / 1000, 0) + "k away] is inside optimal", Logging.Teal);
                                return;
                            }
                        }
                    }
                    else //if optimalrange is not set use MaxRange (shorter of weapons range and targeting range)
                    {
                        if (Logging.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: using MaxRange [" + Combat.MaxRange + "] target is [" + target.Name + "][" + target.Distance + "]", Logging.White);

                        if (target.Distance > Combat.MaxRange)
                        {
                            if (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id || Cache.Instance.MyShipEntity.Velocity < 50)
                            {
                                if (target.IsNPCFrigate && Combat.DoWeCurrentlyHaveTurretsMounted())
                                {
                                    if (Logging.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: target is NPC Frigate [" + target.Name + "][" + target.Distance + "]", Logging.White);
                                    OrbitGateorTarget(target, module);
                                    return;
                                }

                                if (target.KeepAtRange((int) (Combat.MaxRange*0.8d)))
                                {
                                    Logging.Log(module, "Using Weapons Range * 0.8d [" + Math.Round(Combat.MaxRange * 0.8d / 1000, 0) + " k]: Approaching target [" + target.Name + "][ID: " + target.MaskedId + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);                                    
                                }
                                
                                return;    
                            }
                        }

                        //I think when approach distance will be reached ship will be stopped so this is not needed
                        if (target.Distance <= Combat.MaxRange - 5000 && Cache.Instance.Approaching != null)
                        {
                            if (target.IsNPCFrigate && Combat.DoWeCurrentlyHaveTurretsMounted())
                            {
                                if (Logging.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: target is NPC Frigate [" + target.Name + "][" + target.Distance + "]", Logging.White);
                                OrbitGateorTarget(target, module);
                                return;
                            }
                            if (Cache.Instance.MyShipEntity.Velocity != 0) StopMyShip();
                            Logging.Log(module, "Using Weapons Range: Stop ship, target is more than 5k inside weapons range", Logging.Teal);
                            return;
                        }

                        if (target.Distance <= Combat.MaxRange && Cache.Instance.Approaching == null)
                        {
                            if (target.IsNPCFrigate && Combat.DoWeCurrentlyHaveTurretsMounted())
                            {
                                if (Logging.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: target is NPC Frigate [" + target.Name + "][" + target.Distance + "]", Logging.White);
                                OrbitGateorTarget(target, module);
                                return;
                            }
                        }
                    }
                    return;
                }
            }
        }

        public static void StopMyShip()
        {
            if (DateTime.UtcNow > Time.Instance.NextApproachAction)
            {
                Time.Instance.NextApproachAction = DateTime.UtcNow.AddSeconds(Time.Instance.ApproachDelay_seconds);
                Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                Cache.Instance.Approaching = null;
            }
        }

        public static void NavigateToObject(EntityCache target, string module)  //this needs to accept a distance parameter....
        {
            if (NavigateOnGrid.SpeedTank)
            {   //this should be only executed when no specific actions
                if (DateTime.UtcNow > Time.Instance.NextOrbit)
                {
                    if (target.Distance + NavigateOnGrid.OrbitDistance < Combat.MaxRange)
                    {
                        //we cant move in bastion mode, do not try
                        List<ModuleCache> bastionModules = null;
                        bastionModules = Cache.Instance.Modules.Where(m => m.GroupId == (int)Group.Bastion && m.IsOnline).ToList();
                        if (bastionModules.Any(i => i.IsActive)) return;

                        Logging.Log(module, "StartOrbiting: Target in range", Logging.Teal);
                        if (!Cache.Instance.IsApproachingOrOrbiting(target.Id))
                        {
                            Logging.Log("CombatMissionCtrl.NavigateToObject", "We are not approaching nor orbiting", Logging.Teal);
                            EntityCache structure = Cache.Instance.EntitiesOnGrid.Where(i => i.IsLargeCollidable || i.Name.Contains("Gate") || i.Name.Contains("Beacon")).OrderBy(t => t.Distance).ThenBy(t => t.Distance).FirstOrDefault();

                            if (NavigateOnGrid.OrbitStructure && structure != null)
                            {
                                if (structure.Orbit(NavigateOnGrid.OrbitDistance))
                                {
                                    Logging.Log(module, "Initiating Orbit [" + structure.Name + "][ID: " + structure.MaskedId + "]", Logging.Teal);
                                    return;
                                }

                                return;
                            }

                            if (target.Orbit(NavigateOnGrid.OrbitDistance))
                            {
                                Logging.Log(module, "Initiating Orbit [" + target.Name + "][ID: " + target.MaskedId + "]", Logging.Teal);
                                return;
                            }
                            
                            return;
                        }
                    }
                    else
                    {
                        Logging.Log(module, "Possibly out of range. ignoring orbit around structure", Logging.Teal);
                        if (target.Orbit(NavigateOnGrid.OrbitDistance))
                        {
                            Logging.Log(module, "Initiating Orbit [" + target.Name + "][ID: " + target.MaskedId + "]", Logging.Teal);
                            return;
                        }

                        return;
                    }
                }
            }
            else //if we are not speed tanking then check optimalrange setting, if that is not set use the less of targeting range and weapons range to dictate engagement range
            {
                if (DateTime.UtcNow > Time.Instance.NextApproachAction)
                {
                    //if optimalrange is set - use it to determine engagement range
                    //
                    // this assumes that both optimal range and missile boats both want to be within 5k of the object they asked us to navigate to
                    //
                    if (target.Distance > Combat.MaxRange)
                    {
                        if (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id || Cache.Instance.MyShipEntity.Velocity < 50)
                        {
                            if (target.KeepAtRange((int) (Distances.SafeDistancefromStructure)))
                            {
                                Logging.Log(module, "Using SafeDistanceFromStructure: Approaching target [" + target.Name + "][ID: " + target.MaskedId + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);        
                            }
                        }
                    }

                    return;
                }
            }
        }
    }
}