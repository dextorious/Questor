﻿

namespace Questor
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Timers;
    using System.Windows.Forms;
    using System.Xml.Linq;
    using DirectEve;
    using LavishScriptAPI;
    using Mono.Options;
    using global::Questor.Modules.BackgroundTasks;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;

    public static class BeforeLogin
    {
        static BeforeLogin ()
        {
            Logging.UseInnerspace = true; //(defaults to true, will change to false IF passed -i Or -
        }

        private static bool loggedInAndreadyToStartQuestorUI;
        public static List<CharSchedule> CharSchedules { get; private set; }
        public static DateTime QuestorProgramLaunched = DateTime.UtcNow;
        private static bool _questorScheduleSaysWeShouldLoginNow;
        public static DateTime QuestorSchedulerReadyToLogin = DateTime.UtcNow;
        public static DateTime EVEAccountLoginStarted = DateTime.UtcNow;
        //public static DateTime EVECharacterSelected = DateTime.UtcNow;
        public static DateTime NextSlotActivate = DateTime.UtcNow;
        //private static string _scriptFile;
        //private static string _scriptAfterLoginFile;
        private static bool _loginOnly;
        private static bool _showHelp;

        private static bool __chantlingScheduler;
        private static bool _chantlingScheduler
        {
            get
            {
                return __chantlingScheduler;
            }
            set
            {
                __chantlingScheduler = value;
                if (__chantlingScheduler == false && string.IsNullOrEmpty(Logging.MyCharacterName))
                {
                    Logging.Log("Startup", "We were told to use the scheduler but we are Missing the CharacterName to login with...", Logging.Debug);
                }
            }
        }

        private static bool __loginNowIgnoreScheduler;
        private static bool _loginNowIgnoreScheduler
        {
            get
            {
                return __loginNowIgnoreScheduler;
            }
            set
            {
                __loginNowIgnoreScheduler = value;
                _chantlingScheduler = false;
            }
        }

        private static bool _standaloneInstance
        {
            get
            {
                return !Logging.UseInnerspace;
            }
            set
            {
                //Logging.Log("Startup", "! Logging.UseInnerspace = false; !", Logging.White);
                Logging.UseInnerspace = false;
            }

        }
        private static double _minutesToStart;
        private static bool? _readyToLoginEVEAccount;
        private static bool ReadyToLoginToEVEAccount
        {
            get
            {
                try
                {
                    return _readyToLoginEVEAccount ?? false;
                }
                catch (Exception ex)
                {
                    Logging.Log("ReadyToLoginToEVE", "Exception [" + ex + "]", Logging.Debug);
                    return false;
                }
            }

            set
            {
                _readyToLoginEVEAccount = value;
                if (value) //if true
                {
                    QuestorSchedulerReadyToLogin = DateTime.UtcNow;
                }
            }
        }
        private static bool _humanInterventionRequired;
        private static bool MissingEasyHookWarningGiven;
        private static readonly System.Timers.Timer Timer = new System.Timers.Timer();
        private const int RandStartDelay = 30; //Random startup delay in minutes
        private static readonly Random R = new Random();
        private static int ServerStatusCheck = 0;
        private static DateTime _nextPulse;
        private static DateTime _lastServerStatusCheckWasNotOK = DateTime.MinValue;
        public static DateTime StartTime = DateTime.MaxValue;
        public static DateTime StopTime = DateTime.MinValue;
        private static Questor _questor;
        public static List<string> _QuestorParamaters;
        public static bool standalone = true;
        public static string PreLoginSettingsINI;

        public static bool Stop { get; set; }

        

        private static void ParseArgs(IEnumerable<string> args)
        {
            if (!string.IsNullOrEmpty(Logging.EVELoginUserName) &&
                !string.IsNullOrEmpty(Logging.EVELoginPassword) &&
                !string.IsNullOrEmpty(Logging.MyCharacterName))
            {
                return;
            }

            OptionSet p = new OptionSet
            {
                "Usage: questor [OPTIONS]",
                "Run missions and make uber ISK.",
                "",
                "Options:",
                {"u|user=", "the {USER} we are logging in as.", v => Logging.EVELoginUserName = v},
                {"p|password=", "the user's {PASSWORD}.", v => Logging.EVELoginPassword = v},
                {"c|character=", "the {CHARACTER} to use.", v => Logging.MyCharacterName = v},
                {"l|loginOnly", "login only and exit.", v => _loginOnly = v != null},
                {"x|chantling|scheduler", "use scheduler (thank you chantling!)", v => _chantlingScheduler = v != null},
                {"n|loginNow", "Login using info in scheduler", v => _loginNowIgnoreScheduler = v != null},
                {"i|standalone", "Standalone instance, hook D3D w/o Innerspace!", v => _standaloneInstance = v != null},
                {"h|help", "show this message and exit", v => _showHelp = v != null}
            };

            try
            {
                _QuestorParamaters = p.Parse(args);
                //Logging.Log(string.Format("questor: extra = {0}", string.Join(" ", extra.ToArray())));
            }
            catch (OptionException ex)
            {
                Logging.Log("Startup", "questor: ", Logging.White);
                Logging.Log("Startup", ex.Message, Logging.White);
                Logging.Log("Startup", "Try `questor --help' for more information.", Logging.White);
                return;
            }

            if (_showHelp)
            {
                System.IO.StringWriter sw = new System.IO.StringWriter();
                p.WriteOptionDescriptions(sw);
                Logging.Log("Startup", sw.ToString(), Logging.White);
                return;
            }
        }

        private static void OptionallyLoadPreLoginSettingsFromINI(IList<string> args)
        {
            if (!string.IsNullOrEmpty(Logging.EVELoginUserName) &&
                !string.IsNullOrEmpty(Logging.EVELoginPassword) &&
                !string.IsNullOrEmpty(Logging.MyCharacterName))
            {
                return;
            }

            //
            // (optionally) Load EVELoginUserName, EVELoginPassword, MyCharacterName (and other settings) from an ini
            //
            if (args.Count() == 1 && args[0].ToLower().EndsWith(".ini"))
            {
                PreLoginSettingsINI = System.IO.Path.Combine(Logging.PathToCurrentDirectory, args[0]);
            }
            else if (args.Count() == 2 && args[1].ToLower().EndsWith(".ini"))
            {
                PreLoginSettingsINI = System.IO.Path.Combine(Logging.PathToCurrentDirectory, args[0] + " " + args[1]);
            }
            if (!string.IsNullOrEmpty(PreLoginSettingsINI) && File.Exists(PreLoginSettingsINI))
            {
                //Logging.Log("Startup", "Found [" + PreLoginSettingsINI + "] loading Questor PreLogin Settings", Logging.White);
                if (!PreLoginSettings(PreLoginSettingsINI)) Logging.Log("Startup.PreLoginSettings", "Failed to load PreLogin settings from [" + PreLoginSettingsINI + "]", Logging.Debug);
            }
        }

        public static void Main(string[] args)
        {
            //if (Logging.DebugPreLogin)
            //{
            //    int i = 0;
            //    foreach (string arg in args)
            //    {
            //        Logging.Log("Startup", " *** Questor Parameters we have parsed [" + i + "] - [" + arg + "]", Logging.Debug);
            //        i++;
            //    }
            //}

            ParseArgs(args);
            OptionallyLoadPreLoginSettingsFromINI(args);
            
            //
            // Wait to login based on schedule info from schedules.xml
            //
            if (_chantlingScheduler && !string.IsNullOrEmpty(Logging.MyCharacterName))
            {
                WaitToLoginUntilSchedulerSaysWeShould();
            }

            //
            // direct login, no schedules.xml
            //
            if (!string.IsNullOrEmpty(Logging.EVELoginUserName) && !string.IsNullOrEmpty(Logging.EVELoginPassword) && !string.IsNullOrEmpty(Logging.MyCharacterName))
            {
                ReadyToLoginToEVEAccount = true;
            }


            #region Load DirectEVE

            //
            // Load DirectEVE
            //

            try
            {
                bool EasyHookExists = File.Exists(System.IO.Path.Combine(Logging.PathToCurrentDirectory, "EasyHook.dll"));
                if (!EasyHookExists && !MissingEasyHookWarningGiven)
                {
                    Logging.Log("Startup", "EasyHook DLL's are missing. Please copy them into the same directory as your questor.exe", Logging.Orange);
                    Logging.Log("Startup", "halting!", Logging.Orange);
                    MissingEasyHookWarningGiven = true;
                    return;
                }

                int TryLoadingDirectVE = 0;
                while (Cache.Instance.DirectEve == null && TryLoadingDirectVE < 30)
                {
                    //
                    // DE now has cloaking enabled using EasyHook, If EasyHook DLLs are missing DE should complain. We check for and complain about missing EasyHook stuff before we get this far.
                    // 
                    //
                    //Logging.Log("Startup", "temporarily disabling the loading of DE for debugging purposes, halting", Logging.Debug);
                    //while (Cache.Instance.DirectEve == null)
                    //{
                    //    System.Threading.Thread.Sleep(50); //this pauses forever...
                    //}
                    if (!Logging.UseInnerspace)
                    {
                        try
                        {
                            Logging.Log("Startup", "Starting Instance of DirectEVE using StandaloneFramework", Logging.Debug);
                            Cache.Instance.DirectEve = new DirectEve(new StandaloneFramework());
                            TryLoadingDirectVE++;
                            Logging.Log("Startup", "DirectEVE should now be active: see above for any messages from DirectEVE", Logging.Debug);
                        }
                        catch (Exception exception)
                        {
                            Logging.Log("Startup", "exception [" + exception + "]", Logging.Orange);
                            return;
                        }
                    }
                    else
                    {
                        try
                        {
                            Logging.Log("Startup", "Starting Instance of DirectEVE using Innerspace", Logging.Debug);
                            Cache.Instance.DirectEve = new DirectEve();
                            TryLoadingDirectVE++;
                            Logging.Log("Startup", "DirectEVE should now be active: see above for any messages from DirectEVE", Logging.Debug);
                        }
                        catch (Exception exception)
                        {
                            Logging.Log("Startup", "exception [" + exception + "]", Logging.Orange);
                            return;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Logging.Log("Startup", "exception [" + exception + "]", Logging.Orange);
                return;
            }

            if (Cache.Instance.DirectEve == null)
            {
                try
                {
                    Logging.Log("Startup", "Error on Loading DirectEve, maybe server is down", Logging.Orange);
                    Cache.Instance.CloseQuestorCMDLogoff = false;
                    Cache.Instance.CloseQuestorCMDExitGame = true;
                    Cache.Instance.CloseQuestorEndProcess = true;
                    Cleanup.ReasonToStopQuestor = "Error on Loading DirectEve, maybe server is down";
                    Cleanup.SignalToQuitQuestorAndEVEAndRestartInAMoment = true;
                    Cleanup.CloseQuestor(Cleanup.ReasonToStopQuestor, true);
                    return;
                }
                catch (Exception exception)
                {
                    Logging.BasicLog("Startup", "Exception while logging exception, oh joy [" + exception + "]");
                    return;
                }
            }

            #endregion Load DirectEVE

            #region Verify DirectEVE Support Instances

            //
            // Verify DirectEVE Support Instances
            //

            try
            {
                if (Cache.Instance.DirectEve != null && Cache.Instance.DirectEve.HasSupportInstances())
                {
                    Logging.Log("Startup", "You have a valid directeve.lic file and have instances available", Logging.Orange);
                }
                else
                {
                    Logging.Log("Startup", "You have 0 Support Instances available [ Cache.Instance.DirectEve.HasSupportInstances() is false ]", Logging.Orange);
                }

            }
            catch (Exception exception)
            {
                Logging.Log("Startup", "Exception while checking: _directEve.HasSupportInstances() - exception was: [" + exception + "]", Logging.Orange);
            }

            #endregion Verify DirectEVE Support Instances

            if (!Logging.DebugDisableAutoLogin)
            {
                try
                {
                    Cache.Instance.DirectEve.OnFrame += LoginOnFrame;
                }
                catch (Exception ex)
                {
                    Logging.Log("Startup", string.Format("DirectEVE.OnFrame: Exception {0}...", ex), Logging.White);
                }

                // Sleep until we're loggedInAndreadyToStartQuestorUI
                while (!loggedInAndreadyToStartQuestorUI)
                {
                    System.Threading.Thread.Sleep(50); //this runs while we wait to login
                }

                if (loggedInAndreadyToStartQuestorUI)
                {
                    try
                    {
                        //
                        // do not dispose here as we want to use the same DirectEve instance later in the main program
                        //
                        //_directEve.Dispose();
                        Cache.Instance.DirectEve.OnFrame -= LoginOnFrame;
                    }
                    catch (Exception ex)
                    {
                        Logging.Log("Startup", "DirectEVE.Dispose: Exception [" + ex + "]", Logging.White);
                    }


                    //if (!string.IsNullOrEmpty(_scriptAfterLoginFile))
                    //{
                    //    Logging.Log("Startup", "Running Script After Login: [ timedcommand 150 runscript " + _scriptAfterLoginFile + " ]", Logging.Teal);
                    //    LavishScript.ExecuteCommand("timedcommand 150 runscript " + _scriptAfterLoginFile);
                    //    return;
                    //}

                    // If the last parameter is false, then we only auto-login
                    if (_loginOnly)
                    {
                        Logging.Log("Startup", "_loginOnly: done and exiting", Logging.Teal);
                        return;
                    }


                    StartTime = DateTime.Now;

                    //
                    // We should only get this far if run if we are already logged in...
                    // launch questor
                    //
                    try
                    {

                        Logging.Log("Startup", "We are logged in.", Logging.Teal);
                        Logging.Log("Startup", "Launching Questor", Logging.Teal);
                        _questor = new Questor();

                        int intdelayQuestorUI = 0;
                        while (intdelayQuestorUI < 50) //2.5sec = 50ms x 50
                        {
                            intdelayQuestorUI++;
                            System.Threading.Thread.Sleep(50);
                        }

                        Logging.Log("Startup", "Launching QuestorUI", Logging.Teal);
                        Application.Run(new QuestorUI());

                        while (!Cleanup.SignalToQuitQuestor)
                        {
                            System.Threading.Thread.Sleep(50); //this runs while questor is running.
                        }

                        Logging.Log("Startup", "Exiting Questor", Logging.Teal);

                    }
                    catch (Exception ex)
                    {
                        Logging.Log("Startup", "Exception [" + ex + "]", Logging.Teal);
                    }
                    finally
                    {
                        Cleanup.DirecteveDispose();
                        AppDomain.Unload(AppDomain.CurrentDomain);
                    }
                }    
            }
            else
            {
                Logging.Log("Startup", "DebugDisableAutoLogin is true (check characters prelogin settings ini), closing before doing anything useful!", Logging.Debug);
                Cache.Instance.DirectEve.Dispose();
            }
        }

        private static void WaitToLoginUntilSchedulerSaysWeShould()
        {
            string path = Logging.PathToCurrentDirectory;
            Logging.MyCharacterName = Logging.MyCharacterName.Replace("\"", ""); // strip quotation marks if any are present


            CharSchedules = new List<CharSchedule>();
            if (path != null)
            {
                //
                // we should add a check for a missing schedules.xml here and log to the user if it is missing
                //
                XDocument values = XDocument.Load(Path.Combine(path, "Schedules.xml"));
                if (values.Root != null)
                {
                    foreach (XElement value in values.Root.Elements("char"))
                    {
                        CharSchedules.Add(new CharSchedule(value));
                    }
                }
            }

            //
            // chantling scheduler
            //
            CharSchedule schedule = CharSchedules.FirstOrDefault(v => v.ScheduleCharacterName == Logging.MyCharacterName);
            if (schedule == null)
            {
                Logging.Log("Startup", "Error - character [" + Logging.MyCharacterName + "] not found in Schedules.xml!", Logging.Red);
                return;
            }

            if (schedule.LoginUserName == null || schedule.LoginPassWord == null)
            {
                Logging.Log("Startup", "Error - Login details not specified in Schedules.xml!", Logging.Red);
                return;
            }

            Logging.EVELoginUserName = schedule.LoginUserName;
            Logging.EVELoginPassword = schedule.LoginPassWord;
            Logging.Log("Startup", "User: " + schedule.LoginUserName + " Name: " + schedule.ScheduleCharacterName, Logging.White);

            if (schedule.StartTimeSpecified)
            {
                if (schedule.Start1 > schedule.Stop1) schedule.Stop1 = schedule.Stop1.AddDays(1);
                if (DateTime.Now.AddHours(2) > schedule.Start1 && DateTime.Now < schedule.Stop1)
                {
                    StartTime = schedule.Start1;
                    StopTime = schedule.Stop1;
                    Time.Instance.StopTimeSpecified = true;
                    Logging.Log("Startup", "Schedule1: Start1: " + schedule.Start1 + " Stop1: " + schedule.Stop1, Logging.White);
                }
            }

            if (schedule.StartTime2Specified)
            {
                if (schedule.Start2 > schedule.Stop2) schedule.Stop2 = schedule.Stop2.AddDays(1);
                if (DateTime.Now.AddHours(2) > schedule.Start2 && DateTime.Now < schedule.Stop2)
                {
                    StartTime = schedule.Start2;
                    StopTime = schedule.Stop2;
                    Time.Instance.StopTimeSpecified = true;
                    Logging.Log("Startup", "Schedule2: Start2: " + schedule.Start2 + " Stop2: " + schedule.Stop2, Logging.White);
                }
            }

            if (schedule.StartTime3Specified)
            {
                if (schedule.Start3 > schedule.Stop3) schedule.Stop3 = schedule.Stop3.AddDays(1);
                if (DateTime.Now.AddHours(2) > schedule.Start3 && DateTime.Now < schedule.Stop3)
                {
                    StartTime = schedule.Start3;
                    StopTime = schedule.Stop3;
                    Time.Instance.StopTimeSpecified = true;
                    Logging.Log("Startup", "Schedule3: Start3: " + schedule.Start3 + " Stop3: " + schedule.Stop3, Logging.White);
                }
            }

            //
            // if we have not found a workable schedule yet assume schedule 1 is correct. what we want.
            //
            if (schedule.StartTimeSpecified && StartTime == DateTime.MaxValue)
            {
                StartTime = schedule.Start1;
                StopTime = schedule.Stop1;
                Logging.Log("Startup", "Forcing Schedule 1 because none of the schedules started within 2 hours", Logging.White);
                Logging.Log("Startup", "Schedule 1: Start1: " + schedule.Start1 + " Stop1: " + schedule.Stop1, Logging.White);
            }

            if (schedule.StartTimeSpecified || schedule.StartTime2Specified || schedule.StartTime3Specified)
            {
                StartTime = StartTime.AddSeconds(R.Next(0, (RandStartDelay*60)));
            }

            if ((DateTime.Now > StartTime))
            {
                if ((DateTime.Now.Subtract(StartTime).TotalMinutes < 1200)) //if we're less than x hours past start time, start now
                {
                    StartTime = DateTime.Now;
                    _questorScheduleSaysWeShouldLoginNow = true;
                }
                else
                {
                    StartTime = StartTime.AddDays(1); //otherwise, start tomorrow at start time
                }
            }
            else if ((StartTime.Subtract(DateTime.Now).TotalMinutes > 1200)) //if we're more than x hours shy of start time, start now
            {
                StartTime = DateTime.Now;
                _questorScheduleSaysWeShouldLoginNow = true;
            }

            if (StopTime < StartTime)
            {
                StopTime = StopTime.AddDays(1);
            }

            //if (schedule.RunTime > 0) //if runtime is specified, overrides stop time
            //    StopTime = StartTime.AddMinutes(schedule.RunTime); //minutes of runtime

            //if (schedule.RunTime < 18 && schedule.RunTime > 0)     //if runtime is 10 or less, assume they meant hours
            //    StopTime = StartTime.AddHours(schedule.RunTime);   //hours of runtime

            if (_loginNowIgnoreScheduler)
            {
                _questorScheduleSaysWeShouldLoginNow = true;
            }
            else
            {
                Logging.Log("Startup", " Start Time: " + StartTime + " - Stop Time: " + StopTime, Logging.White);
            }

            if (!_questorScheduleSaysWeShouldLoginNow)
            {
                _minutesToStart = StartTime.Subtract(DateTime.Now).TotalMinutes;
                Logging.Log("Startup", "Starting at " + StartTime + ". " + String.Format("{0:0.##}", _minutesToStart) + " minutes to go.", Logging.Yellow);
                Timer.Elapsed += new ElapsedEventHandler(TimerEventProcessor);
                if (_minutesToStart > 0)
                {
                    Timer.Interval = (int) (_minutesToStart*60000);
                }
                else
                {
                    Timer.Interval = 1000;
                }

                Timer.Enabled = true;
                Timer.Start();
            }
            else
            {
                ReadyToLoginToEVEAccount = true;
                Logging.Log("Startup", "Already passed start time.  Starting in 15 seconds.", Logging.White);
                System.Threading.Thread.Sleep(15000);
            }

            //
            // chantling scheduler (above)
            //
        }

        private static void LoginOnFrame(object sender, EventArgs e)
        {
            // New frame, invalidate old cache
            Cache.Instance.InvalidateCache();

            Time.Instance.LastFrame = DateTime.UtcNow;
            Time.Instance.LastSessionIsReady = DateTime.UtcNow;
                //update this regardless before we login there is no session

            if (Cleanup.SignalToQuitQuestorAndEVEAndRestartInAMoment)
            {
                if (_States.CurrentQuestorState != QuestorState.CloseQuestor)
                {
                    _States.CurrentQuestorState = QuestorState.CloseQuestor;
                    Cleanup.BeginClosingQuestor();
                }
            }

            if (DateTime.UtcNow < _lastServerStatusCheckWasNotOK.AddSeconds(RandomNumber(10, 20)))
            {
                Logging.Log("Startup", "lastServerStatusCheckWasNotOK = [" + _lastServerStatusCheckWasNotOK.ToShortTimeString() + "] waiting 10 to 20 seconds.", Logging.White);
                return;
            }

            _lastServerStatusCheckWasNotOK = DateTime.UtcNow.AddDays(-1); //reset this so we never hit this twice in a row w/o another server status check not being OK.

            if (DateTime.UtcNow < _nextPulse)
            {
                //Logging.Log("if (DateTime.UtcNow.Subtract(_lastPulse).TotalSeconds < _pulsedelay) then return");
                return;
            }

            _nextPulse = DateTime.UtcNow.AddMilliseconds(Time.Instance.QuestorBeforeLoginPulseDelay_milliseconds);

            if (DateTime.UtcNow < QuestorProgramLaunched.AddSeconds(5))
            {
                //
                // do not login for the first 5 seconds, wait...
                //
                return;
            }

            if (!ReadyToLoginToEVEAccount && Cache.Instance.DirectEve.Login.AtLogin)
            {
                Logging.Log("Startup", "if (!ReadyToLoginToEVEAccount)", Logging.White);
                return;
            }

            if (_chantlingScheduler && !string.IsNullOrEmpty(Logging.MyCharacterName) &&
                !_questorScheduleSaysWeShouldLoginNow)
            {
                Logging.Log("Startup", "if (_chantlingScheduler && !string.IsNullOrEmpty(Logging._character) && !_questorScheduleSaysWeShouldLoginNow)", Logging.White);
                return;
            }

            if (_humanInterventionRequired)
            {
                Logging.Log("Startup", "OnFrame: _humanInterventionRequired is true (this will spam every second or so)", Logging.Orange);
                return;
            }

            // If the session is ready, then we are done :)
            if (Cache.Instance.DirectEve.Session.IsReady)
            {
                Logging.Log("Startup", "We have successfully logged in", Logging.White);
                Time.Instance.LastSessionIsReady = DateTime.UtcNow;
                loggedInAndreadyToStartQuestorUI = true;
                return;
            }

            // We should not get any windows
            if (Cache.Instance.DirectEve.Windows.Count != 0)
            {
                foreach (DirectWindow window in Cache.Instance.DirectEve.Windows)
                {
                    if (string.IsNullOrEmpty(window.Html))
                        continue;
                    Logging.Log("Startup", "WindowTitles:" + window.Name + "::" + window.Html, Logging.White);

                    //
                    // Close these windows and continue
                    //
                    if (window.Name == "telecom")
                    {
                        Logging.Log("Startup", "Closing telecom message...", Logging.Yellow);
                        Logging.Log("Startup", "Content of telecom window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.Yellow);
                        window.Close();
                        continue;
                    }

                    // Modal windows must be closed
                    // But lets only close known modal windows
                    if (window.Name == "modal")
                    {
                        bool close = false;
                        bool restart = false;
                        bool needHumanIntervention = false;
                        bool sayYes = false;
                        bool sayOk = false;
                        bool quit = false;

                        //bool update = false;

                        if (!string.IsNullOrEmpty(window.Html))
                        {
                            //errors that are repeatable and unavoidable even after a restart of eve/questor
                            needHumanIntervention = window.Html.Contains("reason: Account subscription expired");

                            //update |= window.Html.Contains("The update has been downloaded");

                            // Server going down
                            //Logging.Log("[Startup] (1) close is: " + close);
                            close |= window.Html.ToLower().Contains("please make sure your characters are out of harms way");
                            close |= window.Html.ToLower().Contains("accepting connections");
                            close |= window.Html.ToLower().Contains("could not connect");
                            close |= window.Html.ToLower().Contains("the connection to the server was closed");
                            close |= window.Html.ToLower().Contains("server was closed");
                            close |= window.Html.ToLower().Contains("make sure your characters are out of harm");
                            close |= window.Html.ToLower().Contains("connection to server lost");
                            close |= window.Html.ToLower().Contains("the socket was closed");
                            close |= window.Html.ToLower().Contains("the specified proxy or server node");
                            close |= window.Html.ToLower().Contains("starting up");
                            close |= window.Html.ToLower().Contains("unable to connect to the selected server");
                            close |= window.Html.ToLower().Contains("could not connect to the specified address");
                            close |= window.Html.ToLower().Contains("connection timeout");
                            close |= window.Html.ToLower().Contains("the cluster is not currently accepting connections");
                            close |= window.Html.ToLower().Contains("your character is located within");
                            close |= window.Html.ToLower().Contains("the transport has not yet been connected");
                            close |= window.Html.ToLower().Contains("the user's connection has been usurped");
                            close |= window.Html.ToLower().Contains("the EVE cluster has reached its maximum user limit");
                            close |= window.Html.ToLower().Contains("the connection to the server was closed");
                            close |= window.Html.ToLower().Contains("client is already connecting to the server");

                            //close |= window.Html.Contains("A client update is available and will now be installed");
                            //
                            // eventually it would be nice to hit ok on this one and let it update
                            //
                            close |= window.Html.ToLower().Contains("client update is available and will now be installed");
                            close |= window.Html.ToLower().Contains("change your trial account to a paying account");

                            //
                            // these windows require a restart of eve all together
                            //
                            restart |= window.Html.ToLower().Contains("the connection was closed");
                            restart |= window.Html.ToLower().Contains("connection to server lost."); //INFORMATION
                            restart |= window.Html.ToLower().Contains("local cache is corrupt");
                            sayOk |= window.Html.ToLower().Contains("local session information is corrupt");
                            restart |= window.Html.ToLower().Contains("The client's local session"); // information is corrupt");
                            restart |= window.Html.ToLower().Contains("restart the client prior to logging in");

                            //
                            // these windows require a quit of eve all together
                            //
                            quit |= window.Html.ToLower().Contains("the socket was closed");

                            //
                            // Modal Dialogs the need "yes" pressed
                            //
                            //sayYes |= window.Html.Contains("There is a new build available. Would you like to download it now");
                            //sayOk |= window.Html.Contains("The update has been downloaded. The client will now close and the update process begin");
                            sayOk |= window.Html.Contains("The transport has not yet been connected, or authentication was not successful");

                            //Logging.Log("[Startup] (2) close is: " + close);
                            //Logging.Log("[Startup] (1) window.Html is: " + window.Html);
                        }

                        //if (update)
                        //{
                        //    int secRestart = (400 * 3) + Cache.Instance.RandomNumber(3, 18) * 100 + Cache.Instance.RandomNumber(1, 9) * 10;
                        //    LavishScript.ExecuteCommand("uplink exec Echo [${Time}] timedcommand " + secRestart + " OSExecute taskkill /IM launcher.exe");
                        //}

                        if (sayYes)
                        {
                            Logging.Log("Startup", "Found a window that needs 'yes' chosen...", Logging.White);
                            Logging.Log("Startup", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White);
                            window.AnswerModal("Yes");
                            continue;
                        }

                        if (sayOk)
                        {
                            Logging.Log("Startup", "Found a window that needs 'ok' chosen...", Logging.White);
                            Logging.Log("Startup", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White);
                            window.AnswerModal("OK");
                            if (window.Html.Contains("The update has been downloaded. The client will now close and the update process begin"))
                            {
                                //
                                // schedule the closing of launcher.exe via a timedcommand (10 min?) in the uplink...
                                //
                            }
                            continue;
                        }

                        if (quit)
                        {
                            Logging.Log("Startup", "Restarting eve...", Logging.Red);
                            Logging.Log("Startup", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.Red);
                            window.AnswerModal("quit");

                            //_directEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                        }

                        if (restart)
                        {
                            Logging.Log("Startup", "Restarting eve...", Logging.Red);
                            Logging.Log("Startup", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.Red);
                            window.AnswerModal("restart");
                            continue;
                        }

                        if (close)
                        {
                            Logging.Log("Startup", "Closing modal window...", Logging.Yellow);
                            Logging.Log("Startup", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.Yellow);
                            window.Close();
                            continue;
                        }

                        if (needHumanIntervention)
                        {
                            Logging.Log("Startup", "ERROR! - Human Intervention is required in this case: halting all login attempts - ERROR!", Logging.Red);
                            Logging.Log("Startup", "window.Name is: " + window.Name, Logging.Red);
                            Logging.Log("Startup", "window.Html is: " + window.Html, Logging.Red);
                            Logging.Log("Startup", "window.Caption is: " + window.Caption, Logging.Red);
                            Logging.Log("Startup", "window.Type is: " + window.Type, Logging.Red);
                            Logging.Log("Startup", "window.ID is: " + window.Id, Logging.Red);
                            Logging.Log("Startup", "window.IsDialog is: " + window.IsDialog, Logging.Red);
                            Logging.Log("Startup", "window.IsKillable is: " + window.IsKillable, Logging.Red);
                            Logging.Log("Startup", "window.Viewmode is: " + window.ViewMode, Logging.Red);
                            Logging.Log("Startup", "ERROR! - Human Intervention is required in this case: halting all login attempts - ERROR!", Logging.Red);
                            _humanInterventionRequired = true;
                            return;
                        }
                    }

                    if (string.IsNullOrEmpty(window.Html))
                        continue;

                    if (window.Name == "telecom")
                        continue;
                    Logging.Log("Startup", "We have an unexpected window, auto login halted.", Logging.Red);
                    Logging.Log("Startup", "window.Name is: " + window.Name, Logging.Red);
                    Logging.Log("Startup", "window.Html is: " + window.Html, Logging.Red);
                    Logging.Log("Startup", "window.Caption is: " + window.Caption, Logging.Red);
                    Logging.Log("Startup", "window.Type is: " + window.Type, Logging.Red);
                    Logging.Log("Startup", "window.ID is: " + window.Id, Logging.Red);
                    Logging.Log("Startup", "window.IsDialog is: " + window.IsDialog, Logging.Red);
                    Logging.Log("Startup", "window.IsKillable is: " + window.IsKillable, Logging.Red);
                    Logging.Log("Startup", "window.Viewmode is: " + window.ViewMode, Logging.Red);
                    Logging.Log("Startup", "We have got an unexpected window, auto login halted.", Logging.Red);
                    return;
                }
                return;
            }

            if (Cache.Instance.DirectEve.Login.AtLogin && Cache.Instance.DirectEve.Login.ServerStatus != "Status: OK")
            {
                if (ServerStatusCheck <= 20) // at 10 sec a piece this would be 200+ seconds
                {
                    Logging.Log("Startup", "Server status[" + Cache.Instance.DirectEve.Login.ServerStatus + "] != [OK] try later", Logging.Orange);
                    ServerStatusCheck++;
                    //retry the server status check twice (with 1 sec delay between each) before kicking in a larger delay
                    if (ServerStatusCheck > 2)
                    {
                        _lastServerStatusCheckWasNotOK = DateTime.UtcNow;
                    }

                    return;
                }

                ServerStatusCheck = 0;
                Cleanup.ReasonToStopQuestor = "Server Status Check shows server still not ready after more than 3 min. Restarting Questor. ServerStatusCheck is [" + ServerStatusCheck + "]";
                Logging.Log("Startup", Cleanup.ReasonToStopQuestor, Logging.Red);
                Time.EnteredCloseQuestor_DateTime = DateTime.UtcNow;
                Cleanup.CloseQuestor(Cleanup.ReasonToStopQuestor, true);
                return;
            }

            if (Cache.Instance.DirectEve.Login.AtLogin && !Cache.Instance.DirectEve.Login.IsLoading && !Cache.Instance.DirectEve.Login.IsConnecting)
            {
                if (!Cache.Instance.DirectEve.HasSupportInstances())
                {
                    Logging.Log("Startup", "DirectEVE Requires Active Support Instances to use the convenient like Auto-Login, Market Functions (ValueDump and Market involving storylines) among other features.", Logging.White);
                    Logging.Log("Startup", "Make sure you have support instances and that you have downloaded your directeve.lic file and placed it in the .net programs folder with your directeve.dll", Logging.White);
                    _humanInterventionRequired = true;
                    return;
                }

                //
                // we must have support instances available, after a delay, login
                //
                if (DateTime.UtcNow.Subtract(QuestorSchedulerReadyToLogin).TotalMilliseconds > RandomNumber(Time.Instance.EVEAccountLoginDelayMinimum_seconds*1000, Time.Instance.EVEAccountLoginDelayMaximum_seconds*1000))
                {
                    Logging.Log("Startup", "Login account [" + Logging.EVELoginUserName + "]", Logging.White);
                    Cache.Instance.DirectEve.Login.Login(Logging.EVELoginUserName, Logging.EVELoginPassword);
                    EVEAccountLoginStarted = DateTime.UtcNow;
                    Logging.Log("Startup", "Waiting for Character Selection Screen", Logging.White);
                    return;
                }
            }

            if (Cache.Instance.DirectEve.Login.AtCharacterSelection && Cache.Instance.DirectEve.Login.IsCharacterSelectionReady && !Cache.Instance.DirectEve.Login.IsConnecting && !Cache.Instance.DirectEve.Login.IsLoading)
            {
                if (DateTime.UtcNow.Subtract(EVEAccountLoginStarted).TotalMilliseconds > RandomNumber(Time.Instance.CharacterSelectionDelayMinimum_seconds*1000, Time.Instance.CharacterSelectionDelayMaximum_seconds*1000) && DateTime.UtcNow > NextSlotActivate)
                {
                    foreach (DirectLoginSlot slot in Cache.Instance.DirectEve.Login.CharacterSlots)
                    {
                        if (slot.CharId.ToString(CultureInfo.InvariantCulture) != Logging.MyCharacterName && System.String.Compare(slot.CharName, Logging.MyCharacterName, System.StringComparison.OrdinalIgnoreCase) != 0)
                        {
                            continue;
                        }

                        Logging.Log("Startup", "Activating character [" + slot.CharName + "]", Logging.White);
                        NextSlotActivate = DateTime.UtcNow.AddSeconds(5);
                        slot.Activate();
                        //EVECharacterSelected = DateTime.UtcNow;
                        return;
                    }
                    Logging.Log("Startup",
                        "Character id/name [" + Logging.MyCharacterName + "] not found, retrying in 10 seconds",
                        Logging.White);
                }
            }
        }

        private static void TimerEventProcessor(Object myObject, EventArgs myEventArgs)
        {
            Timer.Stop();
            Logging.Log("Startup", "Timer elapsed.  Starting now.", Logging.White);
            ReadyToLoginToEVEAccount = true;
            _questorScheduleSaysWeShouldLoginNow = true;
        }

        public static int RandomNumber(int min, int max)
        {
            Random random = new Random();
            return random.Next(min, max);
        }

        public static IEnumerable<string> SplitArguments(string commandLine)
        {
            var parmChars = commandLine.ToCharArray();
            var inSingleQuote = false;
            var inDoubleQuote = false;
            for (var index = 0; index < parmChars.Length; index++)
            {
                if (parmChars[index] == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                    parmChars[index] = '\n';
                }
                if (parmChars[index] == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                    parmChars[index] = '\n';
                }
                if (!inSingleQuote && !inDoubleQuote && parmChars[index] == ' ')
                    parmChars[index] = '\n';
            }
            return (new string(parmChars)).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static bool PreLoginSettings(string iniFile)
        {
            try
            {
                if (!File.Exists(iniFile))
                {
                    Logging.Log("PreLoginSettings", "Could not find a file named [" + iniFile + "]", Logging.Debug);
                }

                int index = 0;
                foreach (string line in File.ReadAllLines(iniFile))
                {
                    index++;
                    if (line.StartsWith(";"))
                        continue;

                    if (line.StartsWith("["))
                        continue;

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (string.IsNullOrEmpty(line))
                        continue;
                    
                    string[] sLine = line.Split(new string[] { "=" }, StringSplitOptions.RemoveEmptyEntries);
                    //if (sLine.Count() != 2 && !sLine[0].Equals(ProxyUsername) && !sLine[0].Equals(ProxyPassword) )
                    if (sLine.Count() != 2)
                    {
                        Logging.Log("PreLoginSettings", "IniFile not right format at line: [" + index + "]", Logging.Debug);
                    }

                    switch (sLine[0].ToLower())
                    {
                        case "eveloginusername":
                            Logging.EVELoginUserName = sLine[1];
                            break;

                        case "eveloginpassword":
                            Logging.EVELoginPassword = sLine[1];
                            break;

                        case "characternametologin":
                            Logging.MyCharacterName = sLine[1];
                            break;

                        case "questorloginonly":
                            _loginOnly = bool.Parse(sLine[1]);
                            break;

                        case "questorusescheduler":
                            _chantlingScheduler = bool.Parse(sLine[1]);
                            break;

                        case "standaloneinstance":
                            _standaloneInstance = bool.Parse(sLine[1]);
                            break;

                        case "enablevisualstyles":
                            Logging.EnableVisualStyles = bool.Parse(sLine[1]);
                            break;

                        case "debugdisableautologin":
                            Logging.DebugDisableAutoLogin = bool.Parse(sLine[1]);
                            break;
                    }
                }

                if (Logging.EVELoginUserName == null)
                {
                    Logging.Log("PreLoginSettings", "Missing: EVELoginUserName in [" + iniFile + "]: questor cant possibly AutoLogin without the EVE Login UserName", Logging.Debug);
                }

                if (Logging.EVELoginPassword == null)
                {
                    Logging.Log("PreLoginSettings", "Missing: EVELoginPassword in [" + iniFile + "]: questor cant possibly AutoLogin without the EVE Login Password!", Logging.Debug);
                }

                if (Logging.MyCharacterName == null)
                {
                    Logging.Log("PreLoginSettings", "Missing: CharacterNameToLogin in [" + iniFile + "]: questor cant possibly AutoLogin without the EVE CharacterName to choose", Logging.Debug);
                }

                return true;
            }
            catch (Exception exception)
            {
                Logging.Log("Startup.PreLoginSettings", "Exception [" + exception + "]", Logging.Debug);
                return false;
            }
        }
    }
}