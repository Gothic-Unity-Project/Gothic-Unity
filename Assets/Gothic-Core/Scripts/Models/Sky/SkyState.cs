using UnityEngine;

namespace GUZ.Core.Models.Sky
{
    /// <summary>
    /// This class is mostly a copy of the data from the original game, but with some changes as to make transitions cleaner.
    /// (E.g textures for transitional layers such as dawn and evening and night1, day1)
    /// </summary>
    public class SkyState
    {
        public float Time;
        public Vector3 PolyColor;
        public Vector3 FogColor;
        public Vector3 DomeColor1;
        public Vector3 DomeColor0;
        public float FogDist;
        public int SunOn = 1;
        public int CloudShadowOn;
        public SkyLayerData[] Layer;

        // how long the transition should take
        // 0.05 = 1 hour and 12 minutes
        public float LerpDuration = 0.05f;

        public SkyState()
        {
            Layer = new SkyLayerData[2];
            Layer[0] = new SkyLayerData();
            Layer[1] = new SkyLayerData();
        }

        public void PresetDawn()
        {
            Time = 0.7f; // 4:48 am

            PolyColor = new Vector3(190.0f, 160.0f, 255.0f); // ambient light
            FogColor = new Vector3(80.0f, 60.0f, 105.0f); // fog
            DomeColor0 = new Vector3(80.0f, 60.0f, 105.0f); // dome color
            DomeColor1 = new Vector3(255.0f, 255.0f, 255.0f);

            Layer[0].TEXName = "SKYNIGHT_LAYER0_A0.TGA";
            Layer[1].TEXName = "SKYDAY_LAYER0_A0.TGA";

            Layer[0].TEXAlpha = 128.0f;
            Layer[1].TEXAlpha = 128.0f;

            Layer[0].TEXSpeed.y = 0.0f;
            Layer[0].TEXSpeed.x = 0.0f;

            FogDist = 0.5f;
            SunOn = 1;
        }

        public void PresetDay0()
        {
            Time = 0.75f; // 6:00 am

            PolyColor = new Vector3(255.0f, 250.0f, 235.0f);
            FogColor = new Vector3(120.0f, 140.0f, 180.0f);
            DomeColor0 = FogColor;
            DomeColor1 = new Vector3(255.0f, 255.0f, 255.0f);

            Layer[0].TEXName = "SKYDAY_LAYER0_A0.TGA";
            Layer[1].TEXName = "SKYDAY_LAYER1_A0.TGA";

            Layer[0].TEXAlpha = 255.0f;
            Layer[1].TEXAlpha = 0.0f;

            Layer[1].TEXSpeed *= 0.2f;

            FogDist = 0.2f;
            SunOn = 1;
        }

        public void PresetDay1()
        {
            Time = 0f; // 12:00 pm

            PolyColor = new Vector3(255.0f, 250.0f, 235.0f);
            FogColor = new Vector3(120.0f, 140.0f, 180.0f);
            DomeColor0 = FogColor;
            DomeColor1 = new Vector3(255.0f, 255.0f, 255.0f);

            Layer[0].TEXName = "SKYDAY_LAYER0_A0.TGA";
            Layer[1].TEXName = "SKYDAY_LAYER1_A0.TGA";

            Layer[0].TEXAlpha = 255.0f;
            Layer[1].TEXAlpha = 215.0f;

            FogDist = 0.05f;
            SunOn = 1;
        }

        public void PresetDay2()
        {
            Time = 0.25f; // 6:00 pm

            PolyColor = new Vector3(255.0f, 250.0f, 235.0f);
            FogColor = new Vector3(120.0f, 140.0f, 180.0f);
            DomeColor0 = new Vector3(120.0f, 140.0f, 180.0f);
            DomeColor1 = new Vector3(255.0f, 255.0f, 255.0f);

            Layer[0].TEXName = "SKYDAY_LAYER0_A0.TGA";
            Layer[1].TEXName = "SKYDAY_LAYER1_A0.TGA";

            Layer[0].TEXAlpha = 255.0f;
            Layer[1].TEXAlpha = 0.0f;

            FogDist = 0.05f;
            SunOn = 1;
        }

        public void PresetEvening()
        {
            Time = 0.3f; // 7:12 pm

            PolyColor = new Vector3(255.0f, 185.0f, 170.0f);
            FogColor = new Vector3(170.0f, 70.0f, 50.0f);
            DomeColor0 = new Vector3(170.0f, 70.0f, 50.0f);
            DomeColor1 = new Vector3(255.0f, 255.0f, 255.0f);

            Layer[0].TEXName = "SKYNIGHT_LAYER0_A0.TGA";
            Layer[1].TEXName = "SKYDAY_LAYER0_A0.TGA";

            Layer[0].TEXAlpha = 128.0f;
            Layer[1].TEXAlpha = 128.0f;

            Layer[0].TEXSpeed.x = 0.0f;
            Layer[0].TEXSpeed.y = 0.0f;

            SunOn = 1;
            FogDist = 0.2f;
        }

        public void PresetNight0()
        {
            Time = 0.35f; // 8:24 pm

            PolyColor = new Vector3(105.0f, 105.0f, 195.0f);
            FogColor = new Vector3(20.0f, 20.0f, 60.0f);
            DomeColor0 = FogColor;
            DomeColor1 = new Vector3(255.0f, 55.0f, 155.0f);

            Layer[0].TEXName = "SKYNIGHT_LAYER0_A0.TGA";
            Layer[1].TEXName = "SKYNIGHT_LAYER1_A0.TGA";

            Layer[0].TEXAlpha = 255.0f;
            Layer[1].TEXAlpha = 0.0f;

            Layer[0].TEXScale *= 4.0f;

            Layer[0].TEXSpeed.x = 0.0f;
            Layer[0].TEXSpeed.y = 0.0f;

            FogDist = 0.1f;
            SunOn = 0;
            CloudShadowOn = 0;
        }

        public void PresetNight1()
        {
            Time = 0.5f; // 12:00 am

            PolyColor = new Vector3(40.0f, 60.0f, 210.0f);
            FogColor = new Vector3(5.0f, 5.0f, 20.0f);
            DomeColor0 = FogColor;
            DomeColor1 = new Vector3(55.0f, 55.0f, 155.0f);

            Layer[0].TEXName = "SKYNIGHT_LAYER0_A0.TGA";
            Layer[1].TEXName = "SKYNIGHT_LAYER1_A0.TGA";

            Layer[0].TEXAlpha = 255.0f;
            Layer[1].TEXAlpha = 215.0f;

            Layer[0].TEXSpeed.y = 0.0f;
            Layer[0].TEXSpeed.x = 0.0f;

            FogDist = 0.05f;
            SunOn = 0;
        }

        public void PresetNight2()
        {
            Time = 0.65f; // 3:36 am

            PolyColor = new Vector3(40.0f, 60.0f, 210.0f);
            FogColor = new Vector3(5.0f, 5.0f, 20.0f);
            DomeColor0 = FogColor;
            DomeColor1 = new Vector3(55.0f, 55.0f, 155.0f);

            Layer[0].TEXName = "SKYNIGHT_LAYER0_A0.TGA";
            Layer[1].TEXName = "SKYNIGHT_LAYER1_A0.TGA";

            Layer[0].TEXAlpha = 255.0f;
            Layer[1].TEXAlpha = 0.0f;

            Layer[0].TEXSpeed.y = 0.0f;
            Layer[0].TEXSpeed.x = 0.0f;

            FogDist = 0.2f;
            SunOn = 0;
        }
    }
}
