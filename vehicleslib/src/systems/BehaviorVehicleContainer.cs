using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace VehiclesLib
{
    public class EntityBehaviorVehicleContainer : EntityBehavior
    {

        public InventoryGeneric inv;
        public GuiDialogCreatureContents dlg;

        public EntityBehaviorVehicleContainer(Entity entity) : base(entity)
        {
        }

        public override void OnGameTick(float deltaTime)
        {
            base.OnGameTick(deltaTime);
        }

        private void Inv_SlotModified(int slotid)
        {
            TreeAttribute tree = new TreeAttribute();
            inv.ToTreeAttributes(tree);
            entity.WatchedAttributes["vehicleInv"] = tree;
            entity.WatchedAttributes.MarkPathDirty("vehicleInv");
        }



        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            inv = new InventoryGeneric(typeAttributes["quantitySlots"].AsInt(8), "contents-" + entity.EntityId, entity.Api);
            TreeAttribute tree = entity.WatchedAttributes["vehicleInv"] as TreeAttribute;
            if (tree != null) inv.FromTreeAttributes(tree);
            inv.PutLocked = false;

            if (entity.World.Side == EnumAppSide.Server)
            {
                inv.SlotModified += Inv_SlotModified;
            }

            base.Initialize(properties, typeAttributes);
        }


        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
        {
            bool inRange = (byEntity.World.Side == EnumAppSide.Client && byEntity.Pos.SquareDistanceTo(entity.Pos) <= 5) || (byEntity.World.Side == EnumAppSide.Server && byEntity.Pos.SquareDistanceTo(entity.Pos) <= 14);

            if (!inRange || !byEntity.Controls.Sneak)
            {
                return;
            }

            EntityPlayer entityplr = byEntity as EntityPlayer;
            IPlayer player = entity.World.PlayerByUid(entityplr.PlayerUID);
            player.InventoryManager.OpenInventory(inv);

            if (entity.World.Side == EnumAppSide.Client && dlg == null)
            {
                dlg = new GuiDialogCreatureContents(inv, entity as EntityAgent, entity.Api as ICoreClientAPI, "invcontents");
                if (dlg.TryOpen())
                {
                    (entity.World.Api as ICoreClientAPI).Network.SendPacketClient(inv.Open(player));
                }

                dlg.OnClosed += () =>
                {
                    dlg.Dispose();
                    dlg = null;
                };
            }
        }


        public override void OnReceivedClientPacket(IServerPlayer player, int packetid, byte[] data, ref EnumHandling handled)
        {
            if (packetid < 1000)
            {
                inv.InvNetworkUtil.HandleClientPacket(player, packetid, data);
                handled = EnumHandling.PreventSubsequent;
                return;
            }

            if (packetid == 1012)
            {
                player.InventoryManager.OpenInventory(inv);
            }
        }



        WorldInteraction[] interactions = null;

        public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled)
        {
            interactions = ObjectCacheUtil.GetOrCreate(world.Api, "entityContainerInteractions", () =>
            {
                return new WorldInteraction[] {
                    new WorldInteraction()
                    {
                        ActionLangCode = "blockhelp-open",
                        MouseButton = EnumMouseButton.Right,
                        HotKeyCode = "shift"
                    }
                };
            });

            return interactions;
        }


        public override void GetInfoText(StringBuilder infotext)
        {


            base.GetInfoText(infotext);
        }




        public override string PropertyName()
        {
            return "vehicleopenablecontainer";
        }

    }
}
