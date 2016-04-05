﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PkMn.Model;
using PkMn.Model.Enums;
using PkMn.Model.Moves;

namespace PkMn.Instance
{
    public class Battle
    {
        public delegate Monster ChooseMonEventHandler(Trainer trainer);
        public delegate void SendMessageEventHandler(string message);
        public delegate BattleAction ChoseActionEventHandler(Monster current, Trainer trainer);

        protected Trainer Player;
        protected Trainer Foe;

        public ActiveMonster PlayerCurrent { get; protected set; }
        public ActiveMonster FoeCurrent { get; protected set; }
        protected bool IsWildBattle;

        protected int runCount;

        public ChooseMonEventHandler ChooseNextMon;
        public SendMessageEventHandler SendMessage;
        public ChoseActionEventHandler ChooseAction;

        public Battle(Trainer player, Trainer foe, bool isWildBattle)
        {
            Player = player;
            Foe = foe;
            IsWildBattle = isWildBattle;
            runCount = 0;

            foreach (Monster mon in Player.Party)
            {
                if (mon != null && mon.Status != StatusCondition.Faint)
                {
                    PlayerCurrent = new ActiveMonster(Player, mon);
                    break;
                }
            }

            foreach (Monster mon in Foe.Party)
            {
                if (mon != null && mon.Status != StatusCondition.Faint)
                {
                    FoeCurrent = new ActiveMonster(Foe, mon);
                    break;
                }
            }

            if (PlayerCurrent == null || FoeCurrent == null)
                throw new Exception();
        }

        protected void OnSendMessage(string message, params Object[] args)
        {
            if (SendMessage != null)
                SendMessage(string.Format(message, args));
        }

        public bool Step()
        {
            if (PlayerCurrent.Monster.CurrentHP == 0)
            {
                HandleFainting(PlayerCurrent, true, FoeCurrent);
                return PlayerCurrent.Monster != null;
            }

            if (FoeCurrent.Monster.CurrentHP == 0)
            {
                HandleFainting(FoeCurrent, false, PlayerCurrent);
                return FoeCurrent.Monster != null;
            }

            BattleAction playerAction = null;

            if (PlayerCurrent.QueuedMove == null)
            {
                playerAction = ChooseAction(PlayerCurrent.Monster, Player);

                while (playerAction.Type == BattleActionType.UseMove && PlayerCurrent.DisabledCount > 0 && playerAction.WhichMove == PlayerCurrent.DisabledMoveIndex)
                {
                    //OnSendMessage("It's disabled");
                    playerAction = ChooseAction(PlayerCurrent.Monster, Player);
                }

                if (playerAction.Type == BattleActionType.Run)
                {
                    runCount++;

                    if (IsWildBattle)
                    {

                        int B = FoeCurrent.EffectiveStats.Speed / 4 % 256;
                        if (B == 0)
                        {
                            OnSendMessage("{0} successfully ran!", Player.Name);
                            return false;
                        }

                        int F = (int)Math.Floor(PlayerCurrent.EffectiveStats.Speed * 32m / B + 30m * runCount);
                        if (F > 255)
                        {
                            OnSendMessage("{0} successfully ran!", Player.Name);
                            return false;
                        }
                        else
                        {
                            if (Rng.Next(0, 256) < F)
                            {
                                OnSendMessage("{0} successfully ran!", Player.Name);
                                return false;
                            }
                            else
                            {
                                OnSendMessage("Unable to run away!");
                            }
                        }
                    }
                    else
                        OnSendMessage("You can't run from a trainer battle!");
                }
                else
                    runCount = 0;

                switch (playerAction.Type)
                {
                    case BattleActionType.UseItem:
                        throw new Exception();
                    case BattleActionType.ChangeMon:
                        OnSendMessage("Come back {0}!", PlayerCurrent.Monster.Name);
                        PlayerCurrent.Monster = playerAction.SwitchTo;
                        PlayerCurrent.Reset();
                        OnSendMessage("Go {0}!", PlayerCurrent.Monster.Name);
                        break;
                    case BattleActionType.UseMove:
                        PlayerCurrent.MoveIndex = playerAction.WhichMove;
                        break;
                }
            }

            if(FoeCurrent.QueuedMove == null)
                FoeCurrent.MoveIndex = Rng.Next(0, FoeCurrent.Monster.Moves.Count(m => m != null));

            ActiveMonster first = WhoGoesFirst(PlayerCurrent, FoeCurrent);
            ActiveMonster second = first == PlayerCurrent ? FoeCurrent : PlayerCurrent;

            first.Flinched = false;
            second.Flinched = false;
            first.MoveCancelled = false;
            second.MoveCancelled = false;

            foreach (ActiveMonster current in new ActiveMonster[] { first, second })
            {
                if (current.Flinched)
                {
                    OnSendMessage("{0}{1} flinched!", current.Trainer.MonNamePrefix, current.Monster.Name);
                    current.Flinched = false;
                    continue;
                }

                ActiveMonster opponent = current == first ? second : first;
                if (current.SelectedMove != null)
                {
                    bool battleContinues = ExecuteMove(current, opponent);
                    if (!battleContinues)
                    {
                        OnSendMessage("Battle ended due to roar or something");
                        return false;
                    }

                    if (current.Monster.CurrentHP == 0)
                    {
                        HandleFainting(current, current == PlayerCurrent, opponent);

                        if (current.Monster == null)
                            return false;
                    }
                    
                    if (opponent.Monster.CurrentHP == 0)
                    {
                        HandleFainting(opponent, opponent == PlayerCurrent, current);

                        if (opponent.Monster == null)
                            return false;

                        continue;
                    }

                    HandleDamageOverTime(current, opponent);

                    if (current.Monster.CurrentHP == 0)
                    {
                        HandleFainting(current, current == PlayerCurrent, opponent);
                        
                        if (current.Monster == null)
                            return false;
                    }
                }
            }

            return true;
        }

        protected void HandleFainting(ActiveMonster current, bool isPlayer, ActiveMonster opponent)
        {
            if (current.Monster.CurrentHP == 0)
            {
                OnSendMessage("{0}{1} fainted!", current.Trainer.MonNamePrefix, current.Monster.Name);
                current.Monster.Status = StatusCondition.Faint;
                if (isPlayer)
                    current.Monster = ChooseNextMon(current.Trainer);
                else
                    current.Monster = current.Trainer.Party.Where(m => m != null && m.CurrentHP > 0 && m.Status != StatusCondition.Faint).FirstOrDefault();
                
                current.Reset();
                
                if (current.Monster == null)
                {
                    if (isPlayer)
                        OnSendMessage("{0} is out of usable Pokémon! {0} blacked out!", current.Trainer.Name);
                    else
                        OnSendMessage("{0} defeated {1}!", this.Player.Name, current.Trainer.Name);
                }
                else
                {
                    if (isPlayer)
                        OnSendMessage("Go {0}!", current.Monster.Name);
                    else
                        OnSendMessage("{0} sent out {1}!", current.Trainer.Name, current.Monster.Name);
                    current.Recalc();
                }

                //cancel trapping move that isn't rage
                if (opponent.QueuedMove != null && opponent.QueuedMove.Effects.Any(e => e.Type == MoveEffectType.LockInMove && ((MultiEffect)e).ConstantDamage))
                {
                    opponent.QueuedMove = null;
                    opponent.QueuedMoveDamage = -1;
                    opponent.QueuedMoveLimit = -1;
                }
            }
        }

        protected ActiveMonster WhoGoesFirst(ActiveMonster one, ActiveMonster two)
        {
            if (one.MoveIndex >= 0 && one.SelectedMove == Move.Moves["Quick Attack"] && (two.MoveIndex < 0 || two.SelectedMove != Move.Moves["Quick Attack"]))
                return one;

            if (two.MoveIndex >= 0 && two.SelectedMove == Move.Moves["Quick Attack"] && (one.MoveIndex < 0 || one.SelectedMove != Move.Moves["Quick Attack"]))
                return two;

            if (one.MoveIndex >= 0 && one.SelectedMove == Move.Moves["Counter"] && (two.MoveIndex < 0 || two.SelectedMove != Move.Moves["Counter"]))
                return two;

            if (two.MoveIndex >= 0 && two.SelectedMove == Move.Moves["Counter"] && (one.MoveIndex < 0 || one.SelectedMove != Move.Moves["Counter"]))
                return one;

            return one.EffectiveStats.Speed > two.EffectiveStats.Speed ? one : one.EffectiveStats.Speed < two.EffectiveStats.Speed ? two : Rng.Next(0, 2) == 0 ? one : two;
        }

        protected void HandleDamageOverTime(ActiveMonster current, ActiveMonster opponent)
        {
            if (current.Monster.Status == StatusCondition.Poison || current.Monster.Status == StatusCondition.BadlyPoisoned)
            {
                OnSendMessage("{0}{1}'s hurt by poison!", current.Trainer.MonNamePrefix, current.Monster.Name);
                int damage = (int)(((decimal)current.Monster.Stats.HP) / 16m);
                if (current.Monster.Status == StatusCondition.BadlyPoisoned)
                    damage = damage * current.BadlyPoisonedCount++;
                current.Monster.CurrentHP = Math.Max(0, current.Monster.CurrentHP - damage);
                OnSendMessage("Did {0} damage to {1}{2}", damage, current.Trainer.MonNamePrefix, current.Monster.Name);
            }
            else if (current.Monster.Status == StatusCondition.Burn)
            {
                OnSendMessage("{0}{1}'s hurt by the burn!", current.Trainer.MonNamePrefix, current.Monster.Name);
                int damage = (int)(((decimal)current.Monster.Stats.HP) / 16m);
                current.Monster.CurrentHP = Math.Max(0, current.Monster.CurrentHP - damage);
                OnSendMessage("Did {0} damage to {1}{2}", damage, current.Trainer.MonNamePrefix, current.Monster.Name);
            }
        }

        protected bool HandleStatEffect(ActiveMonster current, ActiveMonster opponent, StatEffect eff, bool hitOpponent, bool showFailMessage = true)
        {
            bool ret = false;

            if (Rng.Next(0, 256) < eff.Chance)
            {
                if (current != null && (eff.Who == Who.Self || eff.Who == Who.Both))
                {
                    if (!string.IsNullOrWhiteSpace(eff.Message))
                        OnSendMessage(eff.Message, current.Trainer.MonNamePrefix, current.Monster.Name);

                    if (eff.Temporary)
                    {
                        current.EffectiveStats[eff.Stat] = (int)(((decimal)current.EffectiveStats[eff.Stat]) * eff.Multiplier);
                    }
                    else
                    {
                        if ((eff.Change > 0 && current.StatStages[eff.Stat] >= 6) || (eff.Change < 0 && current.StatStages[eff.Stat] <= 6))
                        {
                            if (showFailMessage)
                                OnSendMessage("Nothing happened!");
                        }
                        else
                        {
                            current.StatStages[eff.Stat] += eff.Change;
                            if (current.StatStages[eff.Stat] > 6)
                                current.StatStages[eff.Stat] = 6;
                            else if (current.StatStages[eff.Stat] < -6)
                                current.StatStages[eff.Stat] = -6;
                            OnSendMessage("{0}{1}'s {2} {3}{4}!", current.Trainer.MonNamePrefix, current.Monster.Name, eff.Stat.ToString().ToUpper(), eff.Change > 1 ? "greatly " : eff.Change < -1 ? "sharply " : "", eff.Change > 0 ? "rose" : "fell");
                            current.Recalc(eff.Stat);
                            ret = true;
                        }
                    }
                }

                if (opponent != null && (eff.Who == Who.Foe || eff.Who == Who.Both) && hitOpponent)
                {
                    if (eff.Temporary)
                    {
                        opponent.EffectiveStats[eff.Stat] = (int)(((decimal)opponent.EffectiveStats[eff.Stat]) * eff.Multiplier);
                    }
                    else
                    {
                        if ((eff.Change > 0 && opponent.StatStages[eff.Stat] >= 6) || (eff.Change < 0 && opponent.StatStages[eff.Stat] <= -6))
                        {
                            if (showFailMessage)
                                OnSendMessage("Nothing happened!");
                        }
                        else
                        {
                            opponent.StatStages[eff.Stat] += eff.Change;
                            if (opponent.StatStages[eff.Stat] > 6)
                                opponent.StatStages[eff.Stat] = 6;
                            else if (opponent.StatStages[eff.Stat] < -6)
                                opponent.StatStages[eff.Stat] = -6;
                            OnSendMessage("{0}{1}'s {2} {3}{4}!", opponent.Trainer.MonNamePrefix, opponent.Monster.Name, eff.Stat.ToString().ToUpper(), eff.Change > 1 ? "greatly " : eff.Change < -1 ? "sharply " : "", eff.Change > 0 ? "rose" : "fell");
                            opponent.Recalc(eff.Stat);
                            ret = true;
                        }
                    }
                }
            }

            return ret;
        }

        protected bool HandleStatusEffect(ActiveMonster current, Move move, ActiveMonster opponent, StatusEffect eff, bool hitOpponent)
        {
            bool ret = false;

            if (Rng.Next(0, 256) < eff.Chance)
            {
                if (current != null && (eff.Who == Who.Self || eff.Who == Who.Both))
                {
                    ret = true;
                    if (eff.Status == StatusCondition.Faint)
                        current.Monster.CurrentHP = 0;
                    else if (eff.Status == StatusCondition.Confusion)
                    {
                        if (current.IsConfused)
                            OnSendMessage("{0}{1} is already confused!", current.Trainer.MonNamePrefix, current.Monster.Name);
                        else
                        {
                            OnSendMessage(eff.Message ?? "{0}{1} became confused!", current.Trainer.MonNamePrefix, current.Monster.Name);
                            current.ConfusedCount = Rng.Next(2, 6);
                        }
                    }
                    else if (eff.Status == StatusCondition.Flinch)
                    {
                        //flinching itself is not implemented
                    }
                    else if ((eff.Force || current.Monster.Status == StatusCondition.None) && !current.Monster.Species.IsImmuneToStatus(eff.Status))
                    {
                        current.Monster.Status = eff.Status;
                        switch (eff.Status)
                        {
                            case StatusCondition.Paralysis:
                                OnSendMessage(eff.Message ?? "{0}{1} was paralyzed!", current.Trainer.MonNamePrefix, current.Monster.Name);
                                current.EffectiveStats.Speed = (int)(((decimal)current.EffectiveStats.Speed) * 0.25m);
                                break;
                            case StatusCondition.Sleep:
                                OnSendMessage(eff.Message ?? "{0}{1} fell asleep!", current.Trainer.MonNamePrefix, current.Monster.Name);
                                current.Monster.SleepCounter = eff.TurnLimit > 0 ? eff.TurnLimit : Rng.Next(1, 8);
                                break;
                            case StatusCondition.Burn:
                                OnSendMessage(eff.Message ?? "{0}{1} was burned!", current.Trainer.MonNamePrefix, current.Monster.Name);
                                current.EffectiveStats.Attack = (int)(((decimal)current.EffectiveStats.Attack) * 0.5m);
                                break;
                            case StatusCondition.BadlyPoisoned:
                                OnSendMessage(eff.Message ?? "{0}{1} was badly poisoned!", current.Trainer.MonNamePrefix, current.Monster.Name);
                                break;
                            case StatusCondition.Freeze:
                                OnSendMessage(eff.Message ?? "{0}{1} was frozen!", current.Trainer.MonNamePrefix, current.Monster.Name);
                                break;
                            default:
                                OnSendMessage(eff.Message ?? "{0}{1} was {2}ed!", current.Trainer.MonNamePrefix, current.Monster.Name, eff.Status.ToString().ToLower());
                                break;
                        }

                    }
                    else if (move.Category == ElementCategory.Status)
                        OnSendMessage("It didn't affect {0}{1}.", current.Trainer.MonNamePrefix, current.Monster.Name);
                }

                if (opponent != null && (eff.Who == Who.Foe || eff.Who == Who.Both) && hitOpponent)
                {
                    ret = true;
                    if (eff.Status == StatusCondition.Faint)
                        opponent.Monster.CurrentHP = 0;
                    else if (eff.Status == StatusCondition.Confusion)
                    {
                        if (opponent.IsConfused)
                            OnSendMessage("{0}{1} is already confused!", opponent.Trainer.MonNamePrefix, opponent.Monster.Name);
                        else
                        {
                            OnSendMessage(eff.Message ?? "{0}{1} became confused!", opponent.Trainer.MonNamePrefix, opponent.Monster.Name);
                            opponent.ConfusedCount = Rng.Next(2, 6);
                        }
                    }
                    else if (eff.Status == StatusCondition.Flinch)
                    {
                        opponent.Flinched = true;
                    }
                    else if ((eff.Force || opponent.Monster.Status == StatusCondition.None) && !opponent.Monster.Species.IsImmuneToStatus(eff.Status))
                    {
                        opponent.Monster.Status = eff.Status;
                        switch (eff.Status)
                        {
                            case StatusCondition.Paralysis:
                                OnSendMessage(eff.Message ?? "{0}{1} was paralyzed!", opponent.Trainer.MonNamePrefix, opponent.Monster.Name);
                                opponent.EffectiveStats.Speed = (int)(((decimal)opponent.EffectiveStats.Speed) * 0.25m);
                                break;
                            case StatusCondition.Sleep:
                                OnSendMessage(eff.Message ?? "{0}{1} fell asleep!", opponent.Trainer.MonNamePrefix, opponent.Monster.Name);
                                opponent.Monster.SleepCounter = eff.TurnLimit > 0 ? eff.TurnLimit : Rng.Next(1, 8);
                                break;
                            case StatusCondition.Burn:
                                OnSendMessage(eff.Message ?? "{0}{1} was burned!", opponent.Trainer.MonNamePrefix, opponent.Monster.Name);
                                opponent.EffectiveStats.Attack = (int)(((decimal)opponent.EffectiveStats.Attack) * 0.5m);
                                break;
                            case StatusCondition.BadlyPoisoned:
                                OnSendMessage(eff.Message ?? "{0}{1} was badly poisoned!", opponent.Trainer.MonNamePrefix, opponent.Monster.Name);
                                break;
                            case StatusCondition.Freeze:
                                OnSendMessage(eff.Message ?? "{0}{1} was frozen!", opponent.Trainer.MonNamePrefix, opponent.Monster.Name);
                                break;
                            default:
                                OnSendMessage(eff.Message ?? "{0}{1} was {2}ed!", opponent.Trainer.MonNamePrefix, opponent.Monster.Name, eff.Status.ToString().ToLower());
                                break;
                        }

                    }
                    else if (move.Category == ElementCategory.Status)
                        OnSendMessage("It didn't affect {0}{1}.", opponent.Trainer.MonNamePrefix, opponent.Monster.Name);
                }
            }

            return ret;
        }

        protected bool PreMoveChecks(ActiveMonster current, ActiveMonster opponent)
        {
            //handle current pkmn disabled
            if (current.DisabledCount > 0)
            {
                current.DisabledCount--;
                if (current.DisabledCount <= 0)
                {
                    current.DisabledMoveIndex = -1;
                    OnSendMessage("{0}{1}'s disabled no more!", current.Trainer.MonNamePrefix, current.Monster.Name);
                }
                else if (current.SelectedMove == current.Monster.Moves[current.DisabledMoveIndex])
                {
                    OnSendMessage("{0}{1}'s {2} is disabled!", current.Trainer.MonNamePrefix, current.Monster.Name, current.SelectedMove.Name.ToUpper());
                    return false;
                }
            }

            //handle current pkmn trapped
            if (opponent.QueuedMove != null && opponent.QueuedMove.Effects.Any(e => e.Type == MoveEffectType.CancelEnemyMove))
            {
                return false;
            }

            if (current.MoveCancelled)
                return false;

            //handle current pkmn asleep
            if (current.Monster.Status == StatusCondition.Sleep)
            {
                if (current.QueuedMove != null && !current.QueuedMove.Effects.Any(e => e.Type == MoveEffectType.LockInMove))
                    current.QueuedMove = null;
                current.IsSemiInvulnerable = false;

                current.Monster.SleepCounter--;
                if (current.Monster.SleepCounter <= 0)
                {
                    current.Monster.Status = StatusCondition.None;
                    OnSendMessage("{0}{1} woke up!", current.Trainer.MonNamePrefix, current.Monster.Name);
                    return false;
                }
                else
                {
                    OnSendMessage("{0}{1} is fast asleep!", current.Trainer.MonNamePrefix, current.Monster.Name);
                    return false;
                }
            }
            //handle current pkmn frozen
            else if (current.Monster.Status == StatusCondition.Freeze)
            {
                if (current.QueuedMove != null && !current.QueuedMove.Effects.Any(e => e.Type == MoveEffectType.LockInMove))
                    current.QueuedMove = null;
                current.IsSemiInvulnerable = false;

                OnSendMessage("{0}{1} is frozen solid!", current.Trainer.MonNamePrefix, current.Monster.Name);
                return false;
            }
            //handle current pkmn paralyzed
            else if (current.Monster.Status == StatusCondition.Paralysis && Rng.Next(0, 256) < 63)
            {
                OnSendMessage("{0}{1} is fully paralyzed!", current.Trainer.MonNamePrefix, current.Monster.Name);
                return false;
            }

            //handle current pkmn confused
            if (current.IsConfused)
            {
                current.ConfusedCount--;
                if (current.ConfusedCount <= 0)
                {
                    OnSendMessage("{0}{1}'s confused no more!", current.Trainer.MonNamePrefix, current.Monster.Name);
                }
                else
                {
                    OnSendMessage("{0}{1} is confused!", current.Trainer.MonNamePrefix, current.Monster.Name);

                    if (Rng.Next(0, 256) < 128)
                    {
                        OnSendMessage("It hurt itself in its confusion!");
                        if (current.QueuedMove != null && !current.QueuedMove.Effects.Any(e => e.Type == MoveEffectType.LockInMove))
                            current.QueuedMove = null;
                        current.IsSemiInvulnerable = false;

                        int confusionDamage = (int)((2m * current.Monster.Level / 5m + 2m) / 50m * current.EffectiveStats.Attack / current.EffectiveStats.Defense * 40m + 2m);
                        OnSendMessage("Did {0} damage to {1}{2}", confusionDamage, current.Trainer.MonNamePrefix, current.Monster.Name);
                        current.Monster.CurrentHP = Math.Max(0, current.Monster.CurrentHP - confusionDamage);
                        return false;
                    }
                }
            }

            //check for charging or recharging
            MultiEffect beforeEffect = (MultiEffect)current.SelectedMove.Effects.Where(e => e.Type == MoveEffectType.Charge && ((MultiEffect)e).When == When.Before).FirstOrDefault();
            MultiEffect afterEffect = (MultiEffect)current.SelectedMove.Effects.Where(e => e.Type == MoveEffectType.Charge && ((MultiEffect)e).When == When.After).FirstOrDefault();

            if (current.QueuedMove == null && beforeEffect != null)
            {
                if (!string.IsNullOrWhiteSpace(beforeEffect.Message))
                    OnSendMessage(beforeEffect.Message, current.Trainer.MonNamePrefix, current.Monster.Name);
                current.QueuedMove = current.SelectedMove;
                if (current.SelectedMove.Effects.Any(e => e.Type == MoveEffectType.SemiInvulnerable))
                    current.IsSemiInvulnerable = true;
                return false;
            }

            if (current.QueuedMove != null && afterEffect != null)
            {
                if (!string.IsNullOrWhiteSpace(afterEffect.Message))
                    OnSendMessage(afterEffect.Message, current.Trainer.MonNamePrefix, current.Monster.Name);
                current.QueuedMove = null;
                return false;
            }

            if (current.SelectedMove.Effects.Any(e => e.Type == MoveEffectType.RestoreHealth) && current.Monster.CurrentHP == current.Monster.Stats.HP)
            {
                OnSendMessage("{0}{1} used {2}!", current.Trainer.MonNamePrefix, current.Monster.Name, current.SelectedMove.Name.ToUpper());
                OnSendMessage("But, it failed.");
                return false;
            }

            return true;
        }

        protected void HandleDisableEffect(ActiveMonster current, ActiveMonster opponent)
        {
            if (opponent.DisabledCount > 0)
            {
                OnSendMessage("But, it failed!");
            }
            else
            {
                Move[] enabled = opponent.Monster.Moves.Zip(opponent.Monster.CurrentPP, (move, pp) => new KeyValuePair<Move, int>(move, pp)).Where(p => p.Value > 0).Select(p => p.Key).ToArray();

                if (enabled.Length <= 0)
                {
                    OnSendMessage("But, it failed!");
                }
                else
                {
                    Move disabledMove = enabled[Rng.Next(0, enabled.Length)];

                    for (int i = 0; i < opponent.Monster.Moves.Length; i++)
                    {
                        if (opponent.Monster.Moves[i] == disabledMove)
                        {
                            opponent.DisabledMoveIndex = i;
                            break;
                        }
                    }

                    MultiEffect disableEffect = (MultiEffect)current.SelectedMove.Effects.Where(e => e.Type == MoveEffectType.Disable).First();

                    opponent.DisabledCount = Rng.Next(disableEffect.Min, disableEffect.Max + 1);

                    OnSendMessage("{0}{1}'s {2} was disabled!", opponent.Trainer.MonNamePrefix, opponent.Monster.Name, disabledMove.Name.ToUpper());
                }
            }

        }

        protected int CalculateDamage(ActiveMonster current, ActiveMonster opponent, bool isCriticalHit)
        {
            //calculate type effectiveness
            decimal typeMultiplier = current.SelectedMove.Type.GetEffectiveness(opponent.Monster.Species);

            bool immuneToType = typeMultiplier == 0m;

            CustomDamageEffect customEffect = (CustomDamageEffect)current.SelectedMove.Effects.Where(e => e.Type == MoveEffectType.CustomDamage).FirstOrDefault();
            if (customEffect != null)
            {
                int customDamage = 0;
                switch (customEffect.Calculation)
                {
                    case "level":
                        customDamage = current.Monster.Level;
                        break;
                    case "constant":
                        customDamage = customEffect.Value;
                        break;
                    case "foe-hp-remaining":
                        customDamage = Math.Max(1, (int)(((decimal)opponent.Monster.CurrentHP) * customEffect.Multiplier));
                        break;
                    case "rng-min-1-max-1.5x-level": //Psywave...
                        customDamage = Rng.Next(1, (int)(1.5m * (decimal)current.Monster.Level) + 1);
                        break;
                }

                if (typeMultiplier == 0m && !current.SelectedMove.Effects.Any(e => e.Type == MoveEffectType.IgnoreTypeImmunity))
                    customDamage = 0;
                else if (!current.SelectedMove.Effects.Any(e => e.Type == MoveEffectType.IgnoreTypeEffectiveness))
                    customDamage = (int)(typeMultiplier * (decimal)customDamage);

                return customDamage;
            }

            //calculate attack and defense
            int att;
            int def;

            if (isCriticalHit)
            {
                att = current.SelectedMove.Type.Category == ElementCategory.Physical ? current.Monster.Stats.Attack : current.Monster.Stats.Special;
                def = current.SelectedMove.Type.Category == ElementCategory.Physical ? opponent.Monster.Stats.Defense : opponent.Monster.Stats.Special;
            }
            else
            {
                att = current.SelectedMove.Type.Category == ElementCategory.Physical ? current.EffectiveStats.Attack : current.EffectiveStats.Special;
                def = current.SelectedMove.Type.Category == ElementCategory.Physical ? opponent.EffectiveStats.Defense : opponent.EffectiveStats.Special;
            }

            if (def == 0)
                def = 1;

            decimal STAB = 1m;
            if (current.SelectedMove.Type == current.Monster.Species.Type1 || current.SelectedMove.Type == current.Monster.Species.Type2)
                STAB = 1.5m;

            decimal criticalMultiplier = isCriticalHit ? 2m : 1m;

            decimal modifier = STAB * typeMultiplier * ((decimal)Rng.Next(217, 256)) / 255m;

            int damage = (int)(((2m * current.Monster.Level * criticalMultiplier / 5m + 2m) / 50m * att / def * current.SelectedMove.Power + 2m) * modifier);
            if (current.SelectedMove.Power == 0)
                damage = 0;
            
            return damage;
        }

        protected bool ExecuteMove(ActiveMonster current, ActiveMonster opponent)
        {
            if (!PreMoveChecks(current, opponent))
                return true;

            MultiEffect lockInEffect = (MultiEffect)current.SelectedMove.Effects.Where(e => e.Type == MoveEffectType.LockInMove).FirstOrDefault();

            if (current.QueuedMove != null && lockInEffect != null && lockInEffect.ConstantDamage)
                OnSendMessage("{0}{1}'s attack continues!", current.Trainer.MonNamePrefix, current.Monster.Name);
            else
                OnSendMessage("{0}{1} used {2}!", current.Trainer.MonNamePrefix, current.Monster.Name, current.SelectedMove.Name.ToUpper());

            //calculate critical hit or not
            int critRatio = (int)(((decimal)current.Monster.Species.BaseStats.Speed) / 2m * ((decimal)current.SelectedMove.CritRatio));
            bool isCriticalHit = Rng.Next(0, 256) < Math.Min(255, critRatio);

            //calculate move hit or miss
            bool moveHit = Rng.Next(0, 256) < (int)(((decimal)current.SelectedMove.Accuracy) * (decimal)current.EffectiveStats.Accuracy / (decimal)opponent.EffectiveStats.Evade);
            if (current.SelectedMove.Effects.Any(e => e.Type == MoveEffectType.PerfectAccuracy))
                moveHit = true;
            else if (opponent.IsSemiInvulnerable && !current.SelectedMove.Effects.Any(e => e.Type == MoveEffectType.IgnoreSemiInvulnerability))
                moveHit = false;
            else if (current.QueuedMove != null && lockInEffect != null && lockInEffect.ConstantDamage)
                moveHit = true;

            current.IsSemiInvulnerable = false;

            bool triedStatusEffect = false;

            bool immuneToType = current.SelectedMove.Type.GetEffectiveness(opponent.Monster.Species) == 0m;
            if (current.SelectedMove.Effects.Any(e => e.Type == MoveEffectType.IgnoreTypeImmunity))
                immuneToType = false;

            //handle self stat stage effects
            foreach (StatEffect eff in current.SelectedMove.Effects.Where(e => e is StatEffect).Cast<StatEffect>().Where(e => string.IsNullOrWhiteSpace(e.Condition)))
            {
                if ((moveHit && !immuneToType) || eff.Who == Who.Both || eff.Who == Who.Self)
                    triedStatusEffect = true;

                HandleStatEffect(current, null, eff, moveHit);
            }

            int damage;
            if (lockInEffect != null && lockInEffect.ConstantDamage && current.QueuedMoveDamage > 0)
                damage = current.QueuedMoveDamage;
            else
                damage = CalculateDamage(current, opponent, isCriticalHit);

            //handle self status condition effects
            foreach (StatusEffect eff in current.SelectedMove.Effects.Where(e => e is StatusEffect))
            {
                if ((moveHit && !immuneToType) || eff.Who == Who.Both || eff.Who == Who.Self)
                    triedStatusEffect = true;

                HandleStatusEffect(current, current.SelectedMove, null, eff, moveHit);
            }

            HealthEffect restoreHealth = (HealthEffect)current.SelectedMove.Effects.Where(e => e.Type == MoveEffectType.RestoreHealth).FirstOrDefault();
            if (restoreHealth != null)
            {
                if (restoreHealth.Of == "max" && restoreHealth.Who == Who.Self)
                {
                    int hpRestored = (int)(restoreHealth.Percent / 100m * (decimal)current.Monster.Stats.HP);
                    if (hpRestored == 0)
                        hpRestored = 1;
                    hpRestored = Math.Min(hpRestored, current.Monster.Stats.HP - current.Monster.CurrentHP);
                    current.Monster.CurrentHP += hpRestored;
                    OnSendMessage("Restored {0} HP to {1}{2}", hpRestored, current.Trainer.MonNamePrefix, current.Monster.Name);
                    OnSendMessage("{0}{1} regained health!", current.Trainer.MonNamePrefix, current.Monster.Name);
                }
                //nothing else to implement
            }

            //handle miss
            if (!moveHit && !(current.SelectedMove.Category == ElementCategory.Status && triedStatusEffect))
            {
                OnSendMessage("{0}{1}'s attack missed!", current.Trainer.MonNamePrefix, current.Monster.Name);
                return true;
            }

            //handle disable effect
            if (moveHit && current.SelectedMove.Effects.Any(e => e.Type == MoveEffectType.Disable))
            {
                triedStatusEffect = true;
                HandleDisableEffect(current, opponent);
            }

            int hitsToTry = 1;

            //handle hit-multiple-times
            MultiEffect multiHit = (MultiEffect)current.SelectedMove.Effects.Where(e => e.Type == MoveEffectType.MultiHit).FirstOrDefault();
            if (multiHit != null)
            {
                if (multiHit.Min == 2 && multiHit.Max == 5) //special case...also most common
                    hitsToTry = new int[] { 2, 2, 2, 3, 3, 3, 4, 5 }[Rng.Next(0, 8)];
                else
                    hitsToTry = Rng.Next(multiHit.Min, multiHit.Max + 1);
            }

            int hitCount = 0;

            //hit
            for (int i = 0; i < hitsToTry; i++)
            {
                hitCount++;
                opponent.Monster.CurrentHP = Math.Max(0, opponent.Monster.CurrentHP - damage);

                if (damage != 0)
                    OnSendMessage("Did {0} damage to {1}{2}", damage, opponent.Trainer.MonNamePrefix, opponent.Monster.Name);

                //apply foe effects
                if (!immuneToType)
                {
                    foreach (StatEffect eff in current.SelectedMove.Effects.Where(e => e is StatEffect).Cast<StatEffect>().Where(e => string.IsNullOrWhiteSpace(e.Condition)))
                    {
                        HandleStatEffect(null, opponent, eff, moveHit);
                    }

                    foreach (StatusEffect eff in current.SelectedMove.Effects.Where(e => e is StatusEffect))
                    {
                        HandleStatusEffect(null, current.SelectedMove, opponent, eff, moveHit);
                    }
                }

                //handle rage building
                if (damage > 0 && opponent.QueuedMove != null)
                {
                    foreach (StatEffect eff in opponent.QueuedMove.Effects.Where(e => e is StatEffect).Cast<StatEffect>().Where(e => e.Condition == "on-damaged"))
                    {
                        HandleStatEffect(opponent, current, eff, moveHit, false);
                    }
                }

                //only display messages on first hit
                if (i == 0 && (damage != 0 || !triedStatusEffect) && !(lockInEffect != null && lockInEffect.ConstantDamage))
                {
                    if (damage != 0 && isCriticalHit)
                        OnSendMessage("Critical hit!");

                    decimal typeMultiplier = current.SelectedMove.Type.GetEffectiveness(opponent.Monster.Species);

                    if (typeMultiplier == 0m && !current.SelectedMove.Effects.Any(e => e.Type == MoveEffectType.IgnoreTypeImmunity))
                        OnSendMessage("It doesn't affect {0}{1}.", opponent.Trainer.MonNamePrefix, opponent.Monster.Name);
                    else if (typeMultiplier > 1m && !current.SelectedMove.Effects.Any(e => e.Type == MoveEffectType.IgnoreTypeEffectiveness))
                        OnSendMessage("It's super effective!");
                    else if (typeMultiplier < 1m && !current.SelectedMove.Effects.Any(e => e.Type == MoveEffectType.IgnoreTypeEffectiveness))
                        OnSendMessage("It's not very effective.");
                }
            
            }

            if(hitsToTry > 1 && hitCount > 0)
                OnSendMessage("Hit {0} time(s)!", hitCount);

            if (moveHit && current.SelectedMove.Effects.Any(e => e.Type == MoveEffectType.CancelEnemyMove))
            {
                opponent.MoveCancelled = true;
                if(opponent.QueuedMove != null && opponent.QueuedMove.Effects.Any(e => e.Type == MoveEffectType.Charge && ((MultiEffect)e).When == When.After))
                    opponent.QueuedMove = null;
            }

            HealthEffect transferHealth = (HealthEffect)current.SelectedMove.Effects.Where(e => e.Type == MoveEffectType.TransferHealth).FirstOrDefault();
            if (transferHealth != null)
            {
                if (transferHealth.Of == "damage")
                {
                    int hpRestored = (int)(transferHealth.Percent / 100m * (decimal)(damage * hitCount));
                    if(hpRestored == 0)
                        hpRestored = 1;
                    hpRestored = Math.Min(hpRestored, current.Monster.Stats.HP - current.Monster.CurrentHP);
                    current.Monster.CurrentHP += hpRestored;
                    OnSendMessage("Restored {0} HP to {1}{2}", hpRestored, current.Trainer.MonNamePrefix, current.Monster.Name);
                    OnSendMessage("Sucked health from {0}{1}!", opponent.Trainer.MonNamePrefix, opponent.Monster.Name);
                }
            }

            //handle defrosting
            if (moveHit && current.SelectedMove.CanCauseStatus(StatusCondition.Burn, Who.Foe) && opponent.Monster.Status == StatusCondition.Freeze)
            {
                OnSendMessage("Fire defrosted {0}{1}!", opponent.Trainer.MonNamePrefix, opponent.Monster.Name);
                opponent.Monster.Status = StatusCondition.None;
            }

            //reset temporary stat changes
            foreach (StatEffect eff in current.SelectedMove.Effects.Where(e => e is StatEffect).Cast<StatEffect>().Where(e => e.Temporary))
            {
                if (eff.Who == Who.Both || eff.Who == Who.Self)
                    current.Recalc(eff.Stat);

                if (eff.Who == Who.Both || eff.Who == Who.Foe)
                    opponent.Recalc(eff.Stat);
            }

            //handle move lock-in
            if (moveHit && lockInEffect != null)
            {
                if (current.QueuedMove == null)
                {
                    //lock-in starting
                    OnSendMessage("{0}{1} can't move!", opponent.Trainer.MonNamePrefix, opponent.Monster.Name);
                    current.QueuedMove = current.SelectedMove;
                    if (lockInEffect.Min == 2 && lockInEffect.Max == 5)
                        current.QueuedMoveLimit = new int[] { 2, 2, 2, 3, 3, 3, 4, 5 }[Rng.Next(0, 8)];
                    else
                        current.QueuedMoveLimit = Rng.Next(lockInEffect.Min, lockInEffect.Max + 1);
                    OnSendMessage("{0}{1} locked in for {2} moves", current.Trainer.MonNamePrefix, current.Monster.Name, current.QueuedMoveLimit);
                }

                current.QueuedMoveLimit--;

                if (current.QueuedMoveLimit <= 0)
                {
                    //lock-in ending
                    current.QueuedMoveLimit = -1;
                    current.QueuedMoveDamage = -1;
                    current.QueuedMove = null;
                }
                else if (lockInEffect.ConstantDamage)
                    current.QueuedMoveDamage = damage;
            }
            else if (lockInEffect == null)
                current.QueuedMove = null;

            //handle hyper beam
            if (current.SelectedMove.Effects.Any(e => e.Type == MoveEffectType.Charge && ((MultiEffect)e).When == When.After))
            {
                current.QueuedMove = current.SelectedMove;
            }


            return true;
        }

    }
}
