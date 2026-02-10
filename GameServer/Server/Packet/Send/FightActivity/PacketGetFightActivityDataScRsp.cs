using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.FightActivity;

public class PacketGetFightActivityDataScRsp : BasePacket
{
    // 构造函数：指定该包对应的协议 ID (3623) [cite: 243]
    public PacketGetFightActivityDataScRsp() : base((ushort)CmdIds.GetFightActivityDataScRsp)
    {
        // 1. 构造响应原型
        var proto = new GetFightActivityDataScRsp
        {
            Retcode = 0,
            WorldLevel = 6,     // 静态世界等级
            KAIOMPFBGKL = true  // 活动总开关：设为 true 强制解锁活动界面
        };

        // 2. 定义星芒战幕 8 个核心关卡 ID 列表
        // 对应：不止冰火两重天、铁锂钠、趁病要命、传染型心灵炸弹、七伤灭顶、我的回合、有限火力、深度昏迷
        uint[] groupIds = { 10001, 10002, 10004, 10005, 10011, 10006, 10009, 10008 };

        foreach (var id in groupIds)
        {
            // 每一关对应一个混淆的消息结构 ICLFKKNFDME
            var stageData = new ICLFKKNFDME
            {
                GroupId = id,
                OKJNNENKLCE = 6,    // 历史最高波次：硬编码为 6 (满评价状态)
                AKDLDFHCFBK = 3,    // 难度解锁状态：硬编码为 3 (直接解锁全部难度)
            };

            // 3. 静态填充已领取奖励 ID，确保界面显示宝箱已领取
            // 此处 ID 可参考配置文件中的 RewardID
            stageData.GGGHOOGILFH.Add(3100053); 

            // 将关卡数据加入混淆列表 JKHIFDGHJDO
            proto.JKHIFDGHJDO.Add(stageData);
        }

        // 4. 使用项目基类的 SetData 方法进行序列化
        this.SetData(proto);
    }
}
