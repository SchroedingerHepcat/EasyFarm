﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using System.Xml.Serialization;
using EasyFarm;
using EasyFarm.PathingTools;
using EasyFarm.UnitTools;
using FFACETools;

namespace EasyFarm.PlayerTools
{
    [Serializable]
    public class Player
    {
        public Pathing Pathing;
        public WeaponAbility Weaponskill = new WeaponAbility();
        public Units Units;
        public RecoveryPath RecoveryPath;

        // Moves
        public List<Ability> StartingResponses = new List<Ability>();
        public List<Ability> CombatResponses = new List<Ability>();
        public List<Ability> EndingResponses = new List<Ability>();
        public List<Ability> HealingResponses = new List<Ability>();
        public List<Ability> DebuffResponses = new List<Ability>();

        // Resting
        public int StandUPHPPValue { get; set; }
        public int SitdownHPPValue { get; set; }

        public int WeaponSkillHP { get; set; }

        public bool KillAggro = true;
        public bool KillPartyClaimed = true;
        public bool KillUnclaimed = true;

        [NonSerialized]
        public bool IsWorking = false;
        [NonSerialized]
        private BackgroundWorker WorkerThread = new BackgroundWorker();
        [NonSerialized]
        private DispatcherTimer Timer = new DispatcherTimer();
        [NonSerialized]
        private FFACE.Position LastPosition = new FFACE.Position();
        [NonSerialized]
        private FFACE Session;

        // Events
        public delegate void Handler();
        public static event Handler OnFinish;
        public static event Handler OnStart;

        public Player()
        {

        }

        /// <summary>
        /// Sets up pathing, fface and units objs and
        /// initializes timers and threads.
        /// </summary>
        /// <param name="session"></param>
        public Player(FFACE session)
        {
            Session = session;
            Pathing = new Pathing(Session);
            Units = new Units(Session);
            RecoveryPath = new RecoveryPath(Session);

            WorkerThread.DoWork += new DoWorkEventHandler(Worker_DoWork);
            Timer.Tick += new EventHandler(Timer_Tick);
            Timer.Interval = new TimeSpan(0, 0, 0, 1);
            WorkerThread.WorkerSupportsCancellation = true;
        }

        /// <summary>
        /// Sets the path for our bot and the sequence,
        /// that resting and battle should occur.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            var Waypoints = Pathing.Waypoints;

            if (Waypoints.Count > 0)
            {
                var NearestPoint = GetNearestPoint(Waypoints);

                if (NearestPoint == null)
                {
                    IsWorking = false;
                    return;
                }

                Pathing.GotoWaypoint(NearestPoint);

                var Route = Waypoints.SkipWhile(element => element != NearestPoint).ToList();

                foreach (var NextPosition in Route)
                {
                    if (Session.Player.HPPCurrent > 0 && IsWorking)
                    {
                        var Mob = GetTargetUnit();

                        // Check Health Before Battle
                        if (HPLow() && !HasAggro())
                            Rest();
                        // Battle if health high
                        else if (Mob.ID != 0)
                            Battle(Mob);
                        else
                            Pathing.GotoWaypoint(NextPosition);
                    }
                }
            }
            else if (Session.Player.HPPCurrent > 0 && IsWorking)
            {
                var Mob = GetTargetUnit();

                // Check Health Before Battle
                if (HPLow() && !HasAggro())
                    Rest();
                // Battle if health high
                else if (Mob.ID != 0)
                    Battle(Mob);
            }
        }

        private FFACE.Position GetNearestPoint(List<FFACE.Position> list)
        {
            // Grab part of route that is within 10 yalms.
            var Route = new List<FFACE.Position>();
            Route.AddRange(Pathing.Waypoints.Where(element => Session.Navigator.DistanceTo(element) < 50));

            // Grab the closest point that is in that path.
            var NearestPoint = (from element in Route
                                orderby Session.Navigator.DistanceTo(element)
                                select element).FirstOrDefault();

            return NearestPoint;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!WorkerThread.IsBusy && IsWorking)
                WorkerThread.RunWorkerAsync();

            if (Session.Player.HPPCurrent <= 0 || !IsWorking)
                Pause();
        }

        /// <summary>
        /// Makes the character rest
        /// </summary>
        private void RestingOff()
        {
            if (Session.Player.Status == Status.Healing)
            {
                Session.Windower.SendString("/heal off");
                System.Threading.Thread.Sleep(50);
            }
        }

        /// <summary>
        /// Makes the character stand up
        /// </summary>
        private void RestingOn()
        {
            if (Session.Player.Status != Status.Healing)
            {
                Session.Windower.SendString("/heal on");
                System.Threading.Thread.Sleep(50);
            }
        }

        /// <summary>
        /// Starts up the bot for combat
        /// </summary>
        /// <param name="unit"></param>
        public void Start(Unit unit)
        {
            Session.Navigator.DistanceTolerance = 3;
            Session.Navigator.HeadingTolerance = 5;
            RestingOff();
        }

        /// <summary>
        /// Closes the distance between the character and 
        /// the target unit. 
        /// </summary>
        /// <param name="unit"></param>
        public void CloseDistance(Unit unit)
        {
            if (Session.Target.ID == unit.ID && 
                Session.Player.Status == Status.Fighting && 
                Session.Navigator.DistanceTo(unit.Position) >= 5)
            {
                Session.Navigator.GotoTarget();
            }
        }


        /// <summary>
        /// Targets the target unit.
        /// </summary>
        /// <param name="unit"></param>
        public void Target(Unit unit)
        {
            if (Session.Target.ID != unit.ID)
            {
                MaintainHeading(unit);
                Session.Windower.SendKeyPress(FFACETools.KeyCode.TabKey);
                System.Threading.Thread.Sleep(50);
            }
        }

        /// <summary>
        /// Performs all starting actions
        /// </summary>
        /// <param name="unit"></param>
        public void Pull(Unit unit)
        {
            if (Session.Target.ID == unit.ID && 
                unit.Status != FFACETools.Status.Fighting && 
                StartingResponses.Count > 0)
            {
                ExecuteActions(StartingResponses, unit);
            }
        }

        /// <summary>
        /// Switches the player to attack mode
        /// </summary>
        /// <param name="unit"></param>
        public void Engage(Unit unit)
        {
            if (Session.Target.ID == unit.ID && 
                Session.Player.Status != Status.Fighting)
            {
                MaintainHeading(unit);
                Session.Windower.SendString("/attack on");
                System.Threading.Thread.Sleep(50);
            }

            // Wrong Target Attacked
            else if (Session.Target.ID != unit.ID && 
                Session.Player.Status == Status.Fighting)
            {
                Disengage();
            }
        }

        /// <summary>
        /// Peforms our rotation to kill our
        /// target unit.
        /// </summary>
        /// <param name="unit"></param>
        public void Kill(Unit unit)
        {
            if (Session.Target.ID == unit.ID && 
                Session.Player.Status == Status.Fighting)
            {
                if (CombatResponses.Count > 0)
                    ExecuteActions(CombatResponses, unit);

                if (Session.Player.TPCurrent >= Weaponskill.TPCost &&
                    unit.HPPCurrent <= WeaponSkillHP &&
                    Session.Player.Status == Status.Fighting &&
                    unit.Status == Status.Fighting &&
                    unit.Distance < Weaponskill.MaxDistance &&
                    Weaponskill.IsValidName)
                {
                    MaintainHeading(unit);
                    Session.Windower.SendString(Weaponskill.ToString());
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        /// <summary>
        /// Clean up for path traveling and 
        /// peform any end battle moves
        /// </summary>
        /// <param name="unit"></param>
        public void End(Unit unit)
        {
            Disengage();

            if (unit.HPPCurrent <= 0 && 
                EndingResponses.Count > 0)
                ExecuteActions(EndingResponses, unit);

            Session.Navigator.DistanceTolerance = 1;
            Session.Navigator.HeadingTolerance = 5;
        }

        /// <summary>
        /// Face character towards opponent.
        /// </summary>
        /// <param name="unit"></param>
        private void MaintainHeading(Unit unit)
        {
            Session.Navigator.FaceHeading(unit.Position);
            System.Threading.Thread.Sleep(50);
        }

        /// <summary>
        /// Stop the character from fight the target
        /// </summary>
        private void Disengage()
        {
            Session.Windower.SendString("/attack off");
            System.Threading.Thread.Sleep(50);
        }

        /// <summary>
        /// Rests the characters HP
        /// </summary>
        private void Rest()
        {
            while (Session.Player.HPPCurrent < StandUPHPPValue && 
                !HasAggro() && 
                IsWorking && 
                Session.Player.HPCurrent > 0 && 
                !IsDebuffPreventingResting(Session.Player.StatusEffects))
            {
                if (Session.Player.Status != Status.Healing)
                    Session.Windower.SendString("/heal on");

                System.Threading.Thread.Sleep(1000);
            }
        }

        public void Battle(Unit unit)
        {
            Start(unit);

            while (Units.IsValid(unit) &&
                Session.Player.HPCurrent > 0 && 
                IsWorking && 
                !WorkerThread.CancellationPending)
            {
                Target(unit);
                Pull(unit);
                Engage(unit);
                CloseDistance(unit);
                Kill(unit);

                System.Threading.Thread.Sleep(50);
            }

            End(unit);
        }

        /// <summary>
        /// Checks to  see if we can cast/use 
        /// a job ability or spell.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private bool AbilityRecastable(Ability value)
        {
            int AbilityRecast = Session.Timer.GetAbilityRecast((AbilityList)value.Index);
            int SpellRecast = Session.Timer.GetSpellRecast((SpellList)value.Index);

            if (value.Type == "JobAbility")
                return AbilityRecast <= 0;
            else
                return SpellRecast <= 0;
        }

        /// <summary>
        /// Performs a list of actions. 
        /// Could be spells or job abilities.
        /// </summary>
        /// <param name="actions"></param>
        /// <param name="unit"></param>
        private void ExecuteActions(List<Ability> actions, Unit unit)
        {
            foreach (var act in actions)
            {
                if (AbilityRecastable(act) &&
                    Session.Player.TPCurrent >= act.TPCost &&
                    Session.Player.MPCurrent >= act.MPCost)
                {
                    int SleepDuration = act.CastTime != 0 ? (int)Math.Ceiling(act.CastTime) * 1000 : 2000;
                    MaintainHeading(unit);
                    Session.Windower.SendString(act.ToString());
                    System.Threading.Thread.Sleep(SleepDuration);
                }
            }
        }

        /// <summary>
        /// Makes the decision to which mob in the area
        /// should get the target unit.
        /// </summary>
        /// <returns></returns>
        private Unit GetTargetUnit()
        {
            var MainTarget = Unit.CreateUnit(0);

            try
            {
                if (KillPartyClaimed && Units.Mobs.Where(mob => mob.PartyClaim).Count() > 0)
                {
                    MainTarget = Units.Mobs.First(mob => mob.PartyClaim);
                }
                else if (Units.Mobs.Where(mob => mob.MyClaim).Count() > 0)
                {
                    MainTarget = Units.Mobs.First(mob => mob.MyClaim);
                }
                else if (KillAggro && Units.Mobs.Where(mob => mob.HasAggroed).Count() > 0)
                {
                    MainTarget = Units.Mobs.First(mob => mob.HasAggroed);
                }
                else if (KillUnclaimed && Units.Mobs.Where(mob => !mob.IsClaimed).Count() > 0)
                {
                    MainTarget = Units.Mobs.Where(mob => !mob.IsClaimed).First();
                }
            }
            catch (InvalidOperationException)
            {
                // Do Nothing, let bot retry
            }

            return MainTarget;
        }

        /// <summary>
        /// Determines low hp status.
        /// </summary>
        /// <returns></returns>
        private bool HPLow()
        {
            return Session.Player.HPPCurrent < SitdownHPPValue;
        }
        
        /// <summary>
        /// Does our character have aggro
        /// </summary>
        /// <returns></returns>
        private bool HasAggro()
        {
            return Units.HasAggro;
        }

        /// <summary>
        /// Does our player have a status effect that prevents him
        /// </summary>
        /// <param name="playerStatusEffects"></param>
        /// <returns></returns>
        private bool IsDebuffPreventingResting(params StatusEffect[] playerStatusEffects)
        {
            var RestBlockingDebuffs = new List<StatusEffect>() 
            { 
                StatusEffect.Poison, StatusEffect.Bio, StatusEffect.Sleep, 
                StatusEffect.Sleep2, StatusEffect.Poison, StatusEffect.Petrification,
                StatusEffect.Stun, StatusEffect.Charm1, StatusEffect.Charm2, 
                StatusEffect.Terror, StatusEffect.Frost, StatusEffect.Burn, 
                StatusEffect.Choke, StatusEffect.Rasp, StatusEffect.Shock, 
                StatusEffect.Drown, StatusEffect.Dia, StatusEffect.Requiem, 
                StatusEffect.Lullaby
            };

            var RestingBlocked = false;

            foreach (var Effect in playerStatusEffects)
                if (RestBlockingDebuffs.Contains(Effect))
                    RestingBlocked = true;

            return RestingBlocked;
        }

        /// <summary>
        /// Runs our bot
        /// </summary>
        public void Run()
        {
            IsWorking = true;
            OnStart.Invoke();
            Timer.Start();
        }

        /// <summary>
        /// Stops the bot.
        /// </summary>
        public void Pause()
        {
            IsWorking = false;
            OnFinish.Invoke();
            Timer.Stop();
            WorkerThread.CancelAsync();
        }

        /// <summary>
        /// Transfers fields from one Player obj,
        /// to another player object.
        /// </summary>
        /// <param name="Temp"></param>
        internal void TransferDeserializedFields(Player Temp)
        {
            this.CombatResponses = Temp.CombatResponses;
            this.DebuffResponses = Temp.DebuffResponses;
            this.EndingResponses = Temp.EndingResponses;
            this.HealingResponses = Temp.HealingResponses;
            this.KillAggro = Temp.KillAggro;
            this.KillPartyClaimed = Temp.KillPartyClaimed;
            this.KillUnclaimed = Temp.KillUnclaimed;
            this.LastPosition = Temp.LastPosition;
            this.Pathing.Waypoints = Temp.Pathing.Waypoints;
            //this.RecoveryPath.Path = Temp.RecoveryPath.Path;
            this.SitdownHPPValue = Temp.SitdownHPPValue;
            this.StandUPHPPValue = Temp.StandUPHPPValue;
            this.StartingResponses = Temp.StartingResponses;
            this.Units.IgnoredMobs = Temp.Units.IgnoredMobs;
            this.Units.TargetNames = Temp.Units.TargetNames;
            this.Weaponskill = Temp.Weaponskill;
            this.WeaponSkillHP = Temp.WeaponSkillHP;
        }

        /// <summary>
        /// Sets the desired unit hp to which our 
        /// character should weaponskill at.
        /// </summary>
        /// <param name="p"></param>
        public void SetWeaponSkillHP(int p)
        {
            this.WeaponSkillHP = p;
        }

        /// <summary>
        /// Targets an enemy. Sames as target, 
        /// but exposed to other classes.
        /// </summary>
        /// <param name="unit"></param>
        public static void TargetNPC(Unit unit)
        {
            TargetNPC(unit);
        }
    }
}