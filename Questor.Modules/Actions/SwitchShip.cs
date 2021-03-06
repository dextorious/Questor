﻿using Questor.Modules.BackgroundTasks;

namespace Questor.Modules.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DirectEve;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Combat;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;
    using global::Questor.Modules.Logging;

    public class SwitchShip
    {
        private DateTime _lastSwitchShipAction;

        public void ProcessState()
        {
            if (!Cache.Instance.InStation)
                return;

            if (Cache.Instance.InSpace)
                return;

            if (DateTime.UtcNow < Time.Instance.LastInSpace.AddSeconds(20)) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
                return;

            string defaultFitting = MissionSettings.DefaultFitting.FittingName;

            switch (_States.CurrentSwitchShipState)
            {
                case SwitchShipState.Idle:
                    break;

                case SwitchShipState.Done:
                    break;

                case SwitchShipState.Begin:
                    _States.CurrentSwitchShipState = SwitchShipState.OpenShipHangar;
                    break;

                case SwitchShipState.OpenShipHangar:

                    // Is the ship hangar open?
                    if (Cache.Instance.ShipHangar == null) break;

                    Logging.Log("SwitchShip", "Activating combat ship", Logging.White);

                    _States.CurrentSwitchShipState = SwitchShipState.ActivateCombatShip;

                    break;

                case SwitchShipState.ActivateCombatShip:
                    string shipName = Combat.CombatShipName.ToLower();

                    if (Cache.Instance.ActiveShip == null || (!string.IsNullOrEmpty(shipName) && Cache.Instance.ActiveShip.GivenName.ToLower() != shipName))
                    {
                        if (DateTime.UtcNow.Subtract(_lastSwitchShipAction).TotalSeconds > Time.Instance.SwitchShipsDelay_seconds)
                        {
                            if (Cache.Instance.ShipHangar == null) break;

                            if (Cache.Instance.ShipHangar != null)
                            {
                                List<DirectItem> ships = Cache.Instance.ShipHangar.Items;
                                if (ships != null && ships.Any())
                                {
                                    foreach (DirectItem ship in ships.Where(ship => ship.GivenName != null && ship.GivenName.ToLower() == shipName))
                                    {
                                        Logging.Log("SwitchShip", "Making [" + ship.GivenName + "] active", Logging.White);
                                        ship.ActivateShip();
                                        Logging.Log("SwitchShip", "Activated", Logging.White);
                                        _lastSwitchShipAction = DateTime.UtcNow;
                                        return;
                                    }
                                }
                            }
                        }
                    }

                    _States.CurrentSwitchShipState = Settings.Instance.UseFittingManager ? SwitchShipState.OpenFittingWindow : SwitchShipState.Cleanup;

                    break;

                case SwitchShipState.OpenFittingWindow:

                    //let's check first if we need to change fitting at all
                    Logging.Log("SwitchShip", "Fitting: " + defaultFitting + " - currentFit: " + MissionSettings.CurrentFit, Logging.White);
                    if (defaultFitting.Equals(MissionSettings.CurrentFit))
                    {
                        Logging.Log("SwitchShip", "Current fit is correct - no change necessary", Logging.White);
                        _States.CurrentSwitchShipState = SwitchShipState.Done;
                    }
                    else
                    {
                        Cache.Instance.DirectEve.OpenFitingManager();
                        _States.CurrentSwitchShipState = SwitchShipState.WaitForFittingWindow;
                    }
                    break;

                case SwitchShipState.WaitForFittingWindow:

                    DirectFittingManagerWindow fittingMgr = Cache.Instance.Windows.OfType<DirectFittingManagerWindow>().FirstOrDefault();

                    //open it again ?
                    if (fittingMgr == null)
                    {
                        Logging.Log("SwitchShip", "Opening fitting manager", Logging.White);
                        Cache.Instance.DirectEve.OpenFitingManager();
                    }

                    //check if it's ready
                    else if (fittingMgr.IsReady)
                    {
                        _States.CurrentSwitchShipState = SwitchShipState.ChoseFitting;
                    }
                    break;

                case SwitchShipState.ChoseFitting:
                    fittingMgr = Cache.Instance.Windows.OfType<DirectFittingManagerWindow>().FirstOrDefault();
                    if (fittingMgr != null)
                    {
                        Logging.Log("SwitchShip", "Looking for fitting " + defaultFitting, Logging.White);

                        foreach (DirectFitting fitting in fittingMgr.Fittings)
                        {
                            //ok found it
                            DirectActiveShip ship = Cache.Instance.ActiveShip;
                            if (defaultFitting.ToLower().Equals(fitting.Name.ToLower()) &&
                                fitting.ShipTypeId == ship.TypeId)
                            {
                                Logging.Log("SwitchShip", "Found fitting " + fitting.Name, Logging.White);

                                //switch to the requested fitting for the current mission
                                fitting.Fit();
                                _lastSwitchShipAction = DateTime.UtcNow;
                                MissionSettings.CurrentFit = fitting.Name;
                                _States.CurrentSwitchShipState = SwitchShipState.WaitForFitting;
                                break;
                            }
                        }
                    }
                    _States.CurrentSwitchShipState = SwitchShipState.Done;
                    if (fittingMgr != null) fittingMgr.Close();
                    break;

                case SwitchShipState.WaitForFitting:

                    //let's wait 10 seconds
                    if (DateTime.UtcNow.Subtract(_lastSwitchShipAction).TotalMilliseconds > Time.Instance.FittingWindowLoadFittingDelay_seconds &&
                        Cache.Instance.DirectEve.GetLockedItems().Count == 0)
                    {
                        //we should be done fitting, proceed to the next state
                        _States.CurrentSwitchShipState = SwitchShipState.Done;
                        fittingMgr = Cache.Instance.Windows.OfType<DirectFittingManagerWindow>().FirstOrDefault();
                        if (fittingMgr != null) fittingMgr.Close();
                        Logging.Log("SwitchShip", "Done fitting", Logging.White);
                    }
                    else Logging.Log("SwitchShip", "Waiting for fitting. time elapsed = " + DateTime.UtcNow.Subtract(_lastSwitchShipAction).TotalMilliseconds + " locked items = " + Cache.Instance.DirectEve.GetLockedItems().Count, Logging.White);
                    break;

                case SwitchShipState.NotEnoughAmmo:
                    Logging.Log("SwitchShip", "Out of Ammo, checking a solution ...", Logging.White);
                    break;

                case SwitchShipState.Cleanup:
                    //if (!Cleanup.CloseInventoryWindows()) break;
                    _States.CurrentSwitchShipState = SwitchShipState.Done;
                    break;

            }
        }
    }
}