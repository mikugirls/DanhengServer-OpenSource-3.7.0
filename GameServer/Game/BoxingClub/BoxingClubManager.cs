using EggLink.DanhengServer.Data;
using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Util;

namespace EggLink.DanhengServer.GameServer.Game.BoxingClub;

public class BoxingClubManager(PlayerInstance player) : BasePlayerManager(player)
{
    private static readonly Logger _log = new("BoxingClub");
    public static bool EnableLog = true; 
    
    public BoxingClubInstance? ChallengeInstance { get; set; }

    public List<FCIHIJLOMGA> GetChallengeList()
    {
        var challengeInfos = new List<FCIHIJLOMGA>();
        foreach (var config in GameData.BoxingClubChallengeData.Values)
        {
            if (ChallengeInstance != null && ChallengeInstance.ChallengeId == (uint)config.ChallengeID)
                challengeInfos.Add(ConstructSnapshot(ChallengeInstance));
            else
                challengeInfos.Add(new FCIHIJLOMGA { ChallengeId = (uint)config.ChallengeID, LLFOFPNDAFG = 1 });
        }
        return challengeInfos;
    }

    public FCIHIJLOMGA ProcessMatchRequest(MatchBoxingClubOpponentCsReq req)
    {
        if (ChallengeInstance == null)
            ChallengeInstance = new BoxingClubInstance(Player, req.ChallengeId, req.AvatarList.ToList());

        uint targetEventId = 0;
        uint randomIndex = 1;

        if (GameData.BoxingClubChallengeData.TryGetValue((int)req.ChallengeId, out var config))
        {
            var groupId = config.StageGroupList[ChallengeInstance.CurrentRoundIndex];
            if (GameData.BoxingClubStageGroupData.TryGetValue(groupId, out var groupConfig))
            {
                randomIndex = (uint)new Random().Next(1, (groupConfig.DisplayEventIDList?.Count ?? 0) + 1);
                targetEventId = (uint)groupConfig.DisplayEventIDList![(int)randomIndex - 1];
            }
        }

        ChallengeInstance.CurrentMatchEventId = targetEventId;
        ChallengeInstance.CurrentOpponentIndex = randomIndex;
        return ConstructSnapshot(ChallengeInstance);
    }

    public async ValueTask EnterBoxingClubStage(uint challengeId)
    {
        if (ChallengeInstance != null && ChallengeInstance.ChallengeId == challengeId)
            await ChallengeInstance.EnterStage();
    }

    public FCIHIJLOMGA ProcessGiveUpChallenge(uint challengeId, bool isFullReset)
    {
        if (isFullReset) ChallengeInstance = null;
        return new FCIHIJLOMGA { ChallengeId = challengeId, LLFOFPNDAFG = 1 };
    }

    public FCIHIJLOMGA? ProcessChooseResonance(uint challengeId, uint resonanceId)
    {
        if (ChallengeInstance == null) return null;
        if (resonanceId != 0) ChallengeInstance.SelectedBuffs.Add(resonanceId);
        ChallengeInstance.CurrentRoundIndex++;
        return ConstructSnapshot(ChallengeInstance);
    }

    public FCIHIJLOMGA ConstructSnapshot(BoxingClubInstance inst)
    {
        var snapshot = new FCIHIJLOMGA {
            ChallengeId = inst.ChallengeId,
            HJMGLEMJHKG = inst.CurrentOpponentIndex,
            LLFOFPNDAFG = 1,
            HNPEAPPMGAA = (uint)(inst.CurrentRoundIndex + 1)
        };
        if (inst.CurrentMatchEventId != 0) snapshot.HLIBIJFHHPG.Add(inst.CurrentMatchEventId);
        snapshot.AvatarList.AddRange(inst.SelectedAvatars);
        return snapshot;
    }
}
