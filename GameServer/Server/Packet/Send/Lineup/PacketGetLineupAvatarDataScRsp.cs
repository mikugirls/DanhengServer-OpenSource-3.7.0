using EggLink.DanhengServer.GameServer.Game.Player;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Lineup;
public class PacketGetLineupAvatarDataScRsp : BasePacket
{
    public PacketGetLineupAvatarDataScRsp(PlayerInstance player) : base(CmdIds.GetLineupAvatarDataScRsp)
    {
        var rsp = new GetLineupAvatarDataScRsp();

        // 1. 处理正式角色 (保持原有逻辑)
        player.AvatarManager?.AvatarData?.FormalAvatars?.ForEach(avatar =>
        {
            rsp.AvatarDataList.Add(new LineupAvatarData
            {
                Id = (uint)avatar.BaseAvatarId, // 建议使用 BaseAvatarId
                Hp = (uint)avatar.CurrentHp,
                AvatarType = AvatarType.AvatarFormalType,
                Sp = (uint)avatar.CurrentSp // 建议也带上 SP
            });
        });

        // 2. 【核心修复】处理试用角色
        // 只有把这些试用角色也发给客户端，编队界面才会出现它们的头像
        player.AvatarManager?.AvatarData?.TrialAvatars?.ForEach(trialAvatar =>
        {
            rsp.AvatarDataList.Add(new LineupAvatarData
            {
                // 关键：这里必须填 SpecialAvatarID (如 3041005)
                Id = (uint)trialAvatar.SpecialAvatarId, 
                Hp = (uint)trialAvatar.CurrentHp,
                AvatarType = AvatarType.AvatarTrialType, // 或者根据活动需求设为 AvatarLimitType
                Sp = (uint)trialAvatar.CurrentSp
            });
        });

        SetData(rsp);
    }
}
