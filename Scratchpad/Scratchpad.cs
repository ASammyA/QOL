using System.Linq;
using UnityEngine;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace DysonSphereProgram.Modding.test_mod
{
  [BepInPlugin(GUID, NAME, VERSION)]
  [BepInProcess("DSPGAME.exe")]
  public class Plugin : BaseUnityPlugin
  {
    public const string GUID = "dev.sammy.dsp.test_mod";
    public const string NAME = "test_mod";
    public const string VERSION = "1.0.0";

    private Harmony _harmony;
    internal static ManualLogSource Log;

    private void Awake()
    {
      Plugin.Log = Logger;
      _harmony = new Harmony(GUID);
      Logger.LogInfo("test_mod Awake() called correctly");
    }

    private void OnDestroy()
    {
      Logger.LogInfo("test_mod OnDestroy() called");
      _harmony?.UnpatchSelf();
      Plugin.Log = null;
    }

    public long timeCurrent;
    public long timePrev;

    void Update()
    {
        timeCurrent = GameMain.instance.timei;
        //Plugin.Log.LogInfo($"outside_the_loop_{timeCurrent}");
        if (timeCurrent-timePrev >= 3600)
        {
            Plugin.Log.LogDebug($"successful_output_{timeCurrent}");
            timePrev = timeCurrent;
            Plugin.Log.LogDebug(GameMain.statistics.production.factoryStatPool[0].productPool[3].total[5]);
            Plugin.Log.LogDebug(GameMain.statistics.production.factoryStatPool[0].productPool[3].total[6]);
            Plugin.Log.LogDebug(DSPGame.GameDesc.galaxySeed);
            Plugin.Log.LogDebug(DSPGame.GameDesc.creationTime);
            Plugin.Log.LogDebug(LDB.items.Select(1002).name);
        } 
    }
  } 
}
