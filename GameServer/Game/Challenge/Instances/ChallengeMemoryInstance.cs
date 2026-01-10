using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.Data.Excel;
using EggLink.DanhengServer.Database.Friend;
using EggLink.DanhengServer.Database;
using EggLink.DanhengServer.Enums.Mission;
using EggLink.DanhengServer.GameServer.Game.Battle;
using EggLink.DanhengServer.GameServer.Game.Challenge.Definitions;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.GameServer.Game.Scene.Entity;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Challenge;
using EggLink.DanhengServer.GameServer.Server.Packet.Send.Lineup;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Proto.ServerSide;
using EggLink.DanhengServer.Util;

namespace EggLink.DanhengServer.GameServer.Game.Challenge.Instances;

public class ChallengeMemoryInstance(PlayerInstance player, ChallengeDataPb data)
    : BaseLegacyChallengeInstance(player, data)
{
    #region Properties

    public override ChallengeConfigExcel Config { get; } =
        GameData.ChallengeConfigData[(int)data.Memory.ChallengeMazeId];

    #endregion

    #region Serialization

    public override CurChallenge ToProto()
    {
        return new CurChallenge
        {
            ChallengeId = Data.Memory.ChallengeMazeId,
            DeadAvatarNum = Data.Memory.DeadAvatarNum,
            ExtraLineupType = (ExtraLineupType)Data.Memory.CurrentExtraLineup,
            Status = (ChallengeStatus)Data.Memory.CurStatus,
            StageInfo = new ChallengeCurBuffInfo(),
            RoundCount = (uint)(Config.ChallengeCountDown - Data.Memory.RoundsLeft)
        };
    }

    #endregion

    #region Getter & Setter

    public void SetCurrentExtraLineup(ExtraLineupType type)
    {
        Data.Memory.CurrentExtraLineup = (ChallengeLineupTypePb)type;
    }

    public override Dictionary<int, List<ChallengeConfigExcel.ChallengeMonsterInfo>> GetStageMonsters()
    {
        return Data.Memory.CurrentStage == 1 ? Config.ChallengeMonsters1 : Config.ChallengeMonsters2;
    }

    public override uint GetStars()
    {
        return Data.Memory.Stars;
    }

    public override int GetCurrentExtraLineupType()
    {
        return (int)Data.Memory.CurrentExtraLineup;
    }

    public override void SetStartPos(Position pos)
    {
        Data.Memory.StartPos = pos.ToVector3Pb();
    }

    public override void SetStartRot(Position rot)
    {
        Data.Memory.StartRot = rot.ToVector3Pb();
    }

    public override void SetSavedMp(int mp)
    {
        Data.Memory.SavedMp = (uint)mp;
    }

    #endregion

    #region Handlers

    public override void OnBattleStart(BattleInstance battle)
    {
        base.OnBattleStart(battle);

        battle.RoundLimit = (int)Data.Memory.RoundsLeft;

        battle.Buffs.Add(new MazeBuff(Config.MazeBuffID, 1, -1)
        {
            WaveFlag = -1
        });
    }

    public override async ValueTask OnBattleEnd(BattleInstance battle, PVEBattleResultCsReq req)
    {
        switch (req.EndStatus)
        {
            case BattleEndStatus.BattleEndWin:
                // Check if any avatar in the lineup has died
                foreach (var avatar in battle.Lineup.AvatarData!.FormalAvatars)
                    if (avatar.CurrentHp <= 0)
                        Data.Memory.DeadAvatarNum++;

                // Get monster count in stage
                long monsters = Player.SceneInstance!.Entities.Values.OfType<EntityMonster>().Count();

                if (monsters == 0) await AdvanceStage();

                // Calculate rounds left
                Data.Memory.RoundsLeft = Math.Min(Math.Max(Data.Memory.RoundsLeft - req.Stt.RoundCnt, 1),
                    Data.Memory.RoundsLeft);

                // Set saved technique points (This will be restored if the player resets the challenge)
                Data.Memory.SavedMp = (uint)Player.LineupManager!.GetCurLineup()!.Mp;
                break;
            case BattleEndStatus.BattleEndQuit:
                // Reset technique points and move back to start position
                var lineup = Player.LineupManager!.GetCurLineup()!;
                lineup.Mp = (int)Data.Memory.SavedMp;
                await Player.MoveTo(Data.Memory.StartPos.ToPosition(), Data.Memory.StartRot.ToPosition());
                await Player.SendPacket(new PacketSyncLineupNotify(lineup));
                break;
            default:
                // Determine challenge result
                // Fail challenge
                Data.Memory.CurStatus = (int)ChallengeStatus.ChallengeFailed;

                // Send challenge result data
                await Player.SendPacket(new PacketChallengeSettleNotify(this));

                break;
        }
    }

    public uint CalculateStars()
    {
        var targets = Config.ChallengeTargetID!;
        var stars = 0u;

        for (var i = 0; i < targets.Count; i++)
        {
            if (!GameData.ChallengeTargetData.ContainsKey(targets[i])) continue;

            var target = GameData.ChallengeTargetData[targets[i]];

            switch (target.ChallengeTargetType)
            {
                case ChallengeTargetExcel.ChallengeType.ROUNDS_LEFT:
                    if (Data.Memory.RoundsLeft >= target.ChallengeTargetParam1) stars += 1u << i;
                    break;
                case ChallengeTargetExcel.ChallengeType.DEAD_AVATAR:
                    if (Data.Memory.DeadAvatarNum == 0) stars += 1u << i;
                    break;
            }
        }

        return Math.Min(stars, 7);
    }

    private async ValueTask AdvanceStage()
    {
        if (Data.Memory.CurrentStage >= Config.StageNum)
        {
            // Last stage
            Data.Memory.CurStatus = (int)ChallengeStatus.ChallengeFinish;
            Data.Memory.Stars = CalculateStars();

            // Save history
            Player.ChallengeManager!.AddHistory((int)Data.Memory.ChallengeMazeId, (int)Data.Memory.Stars, 0);

            // Send challenge result data
            await Player.SendPacket(new PacketChallengeSettleNotify(this));

            // Call MissionManager
            await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.ChallengeFinish, this);

            // save
            Player.ChallengeManager.SaveBattleRecord(this);
		

            // add development
            Player.FriendRecordData!.AddAndRemoveOld(new FriendDevelopmentInfoPb
            {
                DevelopmentType = DevelopmentType.DevelopmentMemoryChallenge,
                Params = { { "ChallengeId", (uint)Config.ID } }
            });
        }
        else
        {
            // Increment and reset stage
            Data.Memory.CurrentStage++;
            // Unload scene group for stage 1
            await Player.SceneInstance!.EntityLoader!.UnloadGroup(Config.MazeGroupID1);

            // Load scene group for stage 2
            await Player.SceneInstance!.EntityLoader!.LoadGroup(Config.MazeGroupID2);

            // Change player line up
            SetCurrentExtraLineup(ExtraLineupType.LineupChallenge2);
            await Player.LineupManager!.SetExtraLineup((ExtraLineupType)GetCurrentExtraLineupType());
            await Player.SendPacket(new PacketChallengeLineupNotify((ExtraLineupType)Data.Memory.CurrentExtraLineup));
            await Player.SceneInstance!.SyncLineup();

            Data.Memory.SavedMp = (uint)Player.LineupManager.GetCurLineup()!.Mp;

            // Move player
            if (Config.MapEntranceID2 != 0 && Config.MapEntranceID2 != Config.MapEntranceID)
            {
                await Player.EnterScene(Config.MapEntranceID2, 0, true);
                Data.Memory.StartPos = Player.Data.Pos!.ToVector3Pb();
                Data.Memory.StartRot = Player.Data.Rot!.ToVector3Pb();
                await Player.SceneInstance!.EntityLoader!.UnloadGroup(Config.MazeGroupID1);
                await Player.SceneInstance!.EntityLoader!.LoadGroup(Config.MazeGroupID2);
            }
            else
            {
                await Player.MoveTo(Data.Memory.StartPos.ToPosition(), Data.Memory.StartRot.ToPosition());
            }

            Player.ChallengeManager!.SaveInstance(this);
        }
    }

    #endregion
}