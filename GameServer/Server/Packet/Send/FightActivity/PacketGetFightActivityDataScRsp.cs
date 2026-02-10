using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using Google.Protobuf;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.FightActivity;

public class PacketGetFightActivityDataScRsp : Packet
{
    public PacketGetFightActivityDataScRsp() : base(CmdIds.GetFightActivityDataScRsp) // 3623
    {
        var rsp = new GetFightActivityDataScRsp
        {
            Retcode = 0,
            WorldLevel = 6,     // 静态世界等级
            KAIOMPFBGKL = true  // 活动总开关：设为 true 以强制解锁活动界面
        };

        // 星芒战幕 8 个核心关卡 ID 列表（对应 ActivityFightGroupID）
        uint[] groupIds = { 10001, 10002, 10004, 10005, 10011, 10006, 10009, 10008 };

        foreach (var id in groupIds)
        {
            var stageData = new ICLFKKNFDME
            {
                GroupId = id,
                OKJNNENKLCE = 6,    // 历史最高波次：设为 6 (满奖励/通关状态)
                AKDLDFHCFBK = 3,    // 难度解锁：设为 3 (直接解锁全部三个难度)
            };

            // 静态填充已领取奖励 ID，确保界面显示宝箱已开启
            // 此 ID 可根据对应关卡的 RewardID 灵活调整
            stageData.GGGHOOGILFH.Add(3100053); 

            rsp.JKHIFDGHJDO.Add(stageData);
        }

        this.Data = rsp.ToByteArray();
    }
}
