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
using Vintagestory.GameContent;
using System.Runtime.CompilerServices;

namespace VehiclesLib
{
    public class EntityVehicleSeat : IMountable
    {
        public EntityVehicle EntityVehicle;
        public int SeatNumber;
        public EntityControls controls = new EntityControls();
        public EntityAgent Passenger = null;
        public long PassengerEntityIdForInit;
        public bool controllable;
        public bool lockYaw = false;

        public string suggestedAnimation;

        protected Vec3f eyePos = new Vec3f(0, 1, 0);
        public Vec3f mountOffset;

        public EntityVehicleSeat(EntityVehicle entityVehicle, int seatNumber, Vec3f mountOffset, EnumMountAngleMode angleMode = EnumMountAngleMode.PushYaw)
        {
            controls.OnAction = this.onControls;
            this.EntityVehicle = entityVehicle;
            this.SeatNumber = seatNumber;
            this.mountOffset = mountOffset;
            this.MountAngleMode = angleMode;
        }

        public static IMountable GetMountable(IWorldAccessor world, TreeAttribute tree)
        {
            Entity entityVehicle = world.GetEntityById(tree.GetLong("entityIdVehicle"));
            if (entityVehicle is EntityVehicle eVehicle)
            {
                return eVehicle.Seats[tree.GetInt("seatNumber")];
            }

            return null;
        }

        Vec4f tmp = new Vec4f();
        Vec3f transformedMountOffset = new Vec3f();
        public Vec3f MountOffset
        {
            get
            {
                var pos = EntityVehicle.SidedPos;
                modelmat.Identity();

                modelmat.Rotate(EntityVehicle.xangle, EntityVehicle.yangle + pos.Yaw, EntityVehicle.zangle);

                var rotvec = modelmat.TransformVector(tmp.Set(mountOffset.X, mountOffset.Y, mountOffset.Z, 0));
                return transformedMountOffset.Set(rotvec.X, rotvec.Y, rotvec.Z);
            }
        }

        EntityPos mountPos = new EntityPos();
        Matrixf modelmat = new Matrixf();
        public EntityPos MountPosition
        {
            get
            {
                var pos = EntityVehicle.SidedPos;
                var moffset = MountOffset;

                mountPos.SetPos(pos.X + moffset.X, pos.Y + moffset.Y, pos.Z + moffset.Z);

                mountPos.SetAngles(
                    pos.Roll + EntityVehicle.xangle,
                    pos.Yaw + EntityVehicle.yangle,
                    pos.Pitch + EntityVehicle.zangle
                );

                return mountPos;
            }
        }

        public string SuggestedAnimation
        {
            get { return suggestedAnimation; }
        }

        public EntityControls Controls
        {
            get
            {
                return this.controls;
            }
        }

        public IMountableSupplier MountSupplier => EntityVehicle;
        public EnumMountAngleMode MountAngleMode = EnumMountAngleMode.Push;
        public Vec3f LocalEyePos => eyePos;
        public Entity MountedBy => Passenger;
        public bool CanControl => controllable;

        EnumMountAngleMode IMountable.AngleMode {
            get { return MountAngleMode; }
        }

        public void DidUnmount(EntityAgent entityAgent)
        {
            if (entityAgent.World.Side == EnumAppSide.Server)
            {
                tryTeleportPassengerToShore();
            }

            var pesr = Passenger?.Properties?.Client.Renderer as EntityShapeRenderer;
            if (pesr != null)
            {
                pesr.xangle = 0;
                pesr.yangle = 0;
                pesr.zangle = 0;
            }

            this.Passenger.Pos.Roll = 0;
            this.Passenger = null;
        }

        private void tryTeleportPassengerToShore()
        {
            var world = Passenger.World;
            var ba = Passenger.World.BlockAccessor;
            bool found = false;

            for (int dx = -1; !found && dx <= 1; dx++)
            {
                for (int dz = -1; !found && dz <= 1; dz++)
                {
                    var targetPos = Passenger.ServerPos.XYZ.AsBlockPos.ToVec3d().Add(dx + 0.5, 1.1, dz + 0.5);
                    var block = ba.GetMostSolidBlock(Passenger.ServerPos.AsBlockPos);
                    if (block.SideSolid[BlockFacing.UP.Index] && !world.CollisionTester.IsColliding(ba, Passenger.CollisionBox, targetPos, false))
                    {
                        this.Passenger.TeleportTo(targetPos);
                        found = true;
                        break;
                    }
                }
            }

            for (int dx = -2; !found && dx <= 2; dx++)
            {
                for (int dz = -2; !found && dz <= 2; dz++)
                {
                    if (Math.Abs(dx) != 2 && Math.Abs(dz) != 2) continue;

                    var targetPos = Passenger.ServerPos.XYZ.AsBlockPos.ToVec3d().Add(dx + 0.5, 1.1, dz + 0.5);
                    var block = ba.GetMostSolidBlock(Passenger.ServerPos.AsBlockPos);
                    if (block.SideSolid[BlockFacing.UP.Index] && !world.CollisionTester.IsColliding(ba, Passenger.CollisionBox, targetPos, false))
                    {
                        this.Passenger.TeleportTo(targetPos);
                        found = true;
                        break;
                    }
                }
            }

            for (int dx = -1; !found && dx <= 1; dx++)
            {
                for (int dz = -1; !found && dz <= 1; dz++)
                {
                    var targetPos = Passenger.ServerPos.XYZ.AsBlockPos.ToVec3d().Add(dx + 0.5, 1.1, dz + 0.5);
                    if (!world.CollisionTester.IsColliding(ba, Passenger.CollisionBox, targetPos, false))
                    {
                        this.Passenger.TeleportTo(targetPos);
                        found = true;
                        break;
                    }
                }
            }
        }

        public void DidMount(EntityAgent entityAgent)
        {
            if (this.Passenger != null && this.Passenger != entityAgent)
            {
                this.Passenger.TryUnmount();
                return;
            }

            this.Passenger = entityAgent;
        }

        public void MountableToTreeAttributes(TreeAttribute tree)
        {
            tree.SetString("className", "vehicle");
            tree.SetLong("entityIdVehicle", this.EntityVehicle.EntityId);
            tree.SetInt("seatNumber", SeatNumber);
        }

        internal void onControls(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            if (action == EnumEntityAction.Sneak && on)
            {
                Passenger?.TryUnmount();
                controls.StopAllMovement();
            }
        }

    }

}
