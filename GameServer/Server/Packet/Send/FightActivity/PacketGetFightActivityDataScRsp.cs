using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.GameServer.Game.Player;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.FightActivity;

public class PacketGetFightActivityDataScRsp : BasePacket
{
    public PacketGetFightActivityDataScRsp(PlayerInstance player) : base((ushort)CmdIds.GetFightActivityDataScRsp)
    {
        // 从 PlayerData 实时获取 WorldLevel，不再使用静态硬编码 6
        var proto = new GetFightActivityDataScRsp
        {
            Retcode = 0,
            WorldLevel = player.Data.WorldLevel, 
            KAIOMPFBGKL = true 
        };

        // 注入关卡快照
        proto.JKHIFDGHJDO.AddRange(player.FightActivityManager.GetFightActivityStageData());

        this.SetData(proto);
    }
}
