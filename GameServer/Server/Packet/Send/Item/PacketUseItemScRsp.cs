using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.Kcp;
using EggLink.DanhengServer.Proto;
using EggLink.DanhengServer.Enums.Item; // 确保引用了子类型枚举

namespace EggLink.DanhengServer.GameServer.Server.Packet.Send.Item;

public class PacketUseItemScRsp : BasePacket
{
    // 【修改】增加一个可选参数 formulaId
    public PacketUseItemScRsp(Retcode retCode, uint itemId, uint count, List<ItemData>? returnItems, uint formulaId = 0) 
        : base(CmdIds.UseItemScRsp)
    {
        var proto = new UseItemScRsp
        {
            Retcode = (uint)retCode,
            UseItemId = itemId,
            UseItemCount = count,
            // 【修复】赋值配方ID，如果不是配方则为0
            FormulaId = formulaId 
        };

        // 处理获得的物品列表 (自选礼包的光锥、开启礼包获得的道具等)
        if (returnItems != null && returnItems.Count > 0)
        {
            proto.ReturnData = new ItemList();
            foreach (var item in returnItems) 
            {
                proto.ReturnData.ItemList_.Add(item.ToProto());
            }
        }

        SetData(proto);
    }
}