using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VehiclesLib
{
    public class VehicleSoundSystem
    {
        public ILoadedSound travelSound;
        public ILoadedSound idleSound;

        ICoreClientAPI capi;
        bool soundsActive;

        public void loadSounds(ICoreClientAPI capi, string travelSoundPath, string idleSoundPath)
        {
            this.capi = capi;

            travelSound = capi.World.LoadSound(new SoundParams()
            {
                Location = new AssetLocation(travelSoundPath),
                ShouldLoop = true,
                RelativePosition = false,
                DisposeOnFinish = false,
                Volume = 0
            });

            idleSound = capi.World.LoadSound(new SoundParams()
            {
                Location = new AssetLocation(idleSoundPath),
                ShouldLoop = true,
                RelativePosition = false,
                DisposeOnFinish = false,
                Volume = 0.35f
            });
        }

        public void NowInMotion(float velocity, EntityPos pos)
        {
            idleSound?.SetPosition(pos.XYZ.ToVec3f());
            travelSound?.SetPosition(pos.XYZ.ToVec3f());

            if (!soundsActive)
            {
                idleSound?.Start();
                soundsActive = true;
            }

            if (velocity > 0)
            {
                if (!travelSound?.IsPlaying ?? false) travelSound?.Start();

                var volume = GameMath.Clamp((velocity - 0.025f) * 7, 0, 1);
                travelSound?.FadeTo(volume, 0.5f, null);
            }
            else
            {
                if (travelSound?.IsPlaying ?? false)
                {
                    travelSound?.Stop();
                }
            }
        }

        public void Dispose()
        {
            travelSound?.Dispose();
            idleSound?.Dispose();
        }

        public void NotMounted()
        {
            if (soundsActive)
            {
                idleSound?.Stop();
                travelSound?.SetVolume(0);
                travelSound?.Stop();
            }
            soundsActive = false;
        }
    }
}
