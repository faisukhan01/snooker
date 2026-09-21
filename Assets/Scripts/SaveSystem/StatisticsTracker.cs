// SnookerKit — career statistics tracker (CONTRACTS §2/§3). Subscribes GameEvents in OnEnable and unsubscribes
// in OnDisable (§0.9); persists the "profile" document on disable/pause/quit. Practice sessions flow into the
// same profile by design: pots/points/breaks count, frames do not.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Accumulates lifetime player statistics from gameplay events and persists them via
    /// <see cref="SaveManager"/> under the "profile" key. Added to the AppServices object by
    /// <see cref="AppServices"/>; registered in <see cref="ServiceRegistry"/> for cross-module access.</summary>
    public class StatisticsTracker : MonoBehaviour
    {
        private ProfileData _profile = new ProfileData();

        /// <summary>Running break from the last non-zero BreakChanged; committed to the profile when the break reports 0.</summary>
        private int _runningBreak;

        private void Awake()
        {
            SaveManager saveManager = SaveManager.Instance;
            if (saveManager == null) ServiceRegistry.TryGet<SaveManager>(out saveManager);
            if (saveManager != null)
            {
                ProfileData loaded = saveManager.Load<ProfileData>("profile"); // never throws, never null
                if (loaded != null) _profile = loaded;
            }
            ServiceRegistry.Register<StatisticsTracker>(this);
        }

        private void OnDestroy()
        {
            ServiceRegistry.Deregister<StatisticsTracker>();
        }

        private void OnEnable()
        {
            GameEvents.PottedBall += OnPottedBall;
            GameEvents.BreakChanged += OnBreakChanged;
            GameEvents.FrameEnded += OnFrameEnded;
            GameEvents.MatchEnded += OnMatchEnded;
            GameEvents.FoulCommitted += OnFoulCommitted;
        }

        private void OnDisable()
        {
            GameEvents.PottedBall -= OnPottedBall;
            GameEvents.BreakChanged -= OnBreakChanged;
            GameEvents.FrameEnded -= OnFrameEnded;
            GameEvents.MatchEnded -= OnMatchEnded;
            GameEvents.FoulCommitted -= OnFoulCommitted;
            SaveProfile(); // flush — this may be the last chance before teardown
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveProfile();
        }

        private void OnApplicationQuit()
        {
            SaveProfile();
        }

        /// <summary>Object-ball pots feed ballsPotted/totalPoints in every mode (practice included). Cue pots are
        /// fouls — they arrive as FoulCommitted and must never inflate pot stats.</summary>
        private void OnPottedBall(PottedBallArgs args)
        {
            if (args == null || args.Color == BallColor.Cue) return;
            _profile.ballsPotted++;
            _profile.totalPoints += BallPalette.Points(args.Color);
        }

        /// <summary>Tracks the running break; when it reports 0 the break is complete and is committed to
        /// highestBreak / recentBreaks (newest first, capped). Breaks are pot-derived, so they count in practice too.</summary>
        private void OnBreakChanged(BreakChangedArgs args)
        {
            if (args == null) return;
            if (args.Break > 0)
            {
                _runningBreak = args.Break;
                return;
            }
            if (_runningBreak <= 0) return;
            if (_runningBreak > _profile.highestBreak) _profile.highestBreak = _runningBreak;
            InsertRecentBreak(_runningBreak);
            _runningBreak = 0;
        }

        /// <summary>Frame bookkeeping. Respot-black FrameEnded events are continuations, not completions (§3.7) —
        /// ignored. Practice frames never count (pinned: practice contributes pots/points/breaks only). Every
        /// other completion counts exactly ONE framesPlayed: the profile is a single device-local record, while
        /// the per-player frames-won bookkeeping lives in MatchManager.FramesWon (deliberately not mirrored here —
        /// resolved ambiguity, see worklog 16-f).</summary>
        private void OnFrameEnded(FrameEndedArgs args)
        {
            if (args == null || args.RespotBlack) return;
            if (IsPractice()) return;
            _profile.framesPlayed++;
        }

        /// <summary>Match winner feeds framesWon: in TwoPlayerLocal both players are human so any winner counts;
        /// in MatchVsAI/QuickMatch only a win by player index 0 (the human) counts. Practice has no match winner.</summary>
        private void OnMatchEnded(MatchEndedArgs args)
        {
            if (args == null || IsPractice()) return;
            if (CurrentMode() == GameMode.TwoPlayerLocal)
            {
                _profile.framesWon++;
                return;
            }
            if (args.WinnerIndex == 0) _profile.framesWon++;
        }

        /// <summary>Counts foul events. PlayerIndex on the args is the INCOMING (benefiting) player, not the
        /// offender, so per-offender attribution is impossible from this event alone — the profile stores one
        /// honest total (documented v1 approximation).</summary>
        private void OnFoulCommitted(FoulCommittedArgs args)
        {
            if (args == null) return;
            _profile.foulsCommitted++;
        }

        /// <summary>Inserts newest-first and trims to <see cref="ProfileData.RecentBreaksCap"/>.</summary>
        private void InsertRecentBreak(int breakValue)
        {
            List<int> list = _profile.recentBreaks;
            if (list == null)
            {
                list = new List<int>();
                _profile.recentBreaks = list;
            }
            list.Insert(0, breakValue); // newest first
            while (list.Count > ProfileData.RecentBreaksCap) list.RemoveAt(list.Count - 1);
        }

        /// <summary>Current GameMode, or MatchVsAI when MatchManager is unavailable (conservative default:
        /// with an unknown mode only human player-0 wins count).</summary>
        private GameMode CurrentMode()
        {
            MatchManager matchManager;
            if (ServiceRegistry.TryGet<MatchManager>(out matchManager) && matchManager != null) return matchManager.Mode;
            return GameMode.MatchVsAI;
        }

        private bool IsPractice()
        {
            return CurrentMode() == GameMode.Practice;
        }

        /// <summary>Persists the profile; never throws (SaveManager.Save swallows IO failures).</summary>
        private void SaveProfile()
        {
            SaveManager saveManager = SaveManager.Instance;
            if (saveManager == null) ServiceRegistry.TryGet<SaveManager>(out saveManager);
            if (saveManager == null) return;
            try
            {
                saveManager.Save("profile", _profile);
            }
            catch (Exception)
            {
                // Belt and braces only — SaveManager.Save already never throws.
            }
        }
    }
}
