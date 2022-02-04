using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Globalization;
using System.Linq;
using System.Text;
using DefaultNamespace;
using BepInEx.Logging;
using Newtonsoft.Json;
using System.IO;
using Amazon;
using Amazon.S3;
using Amazon.S3.Transfer;
using Amazon.S3.Model;

namespace BetterStats.ExtractData.Sammy
{
    [BepInPlugin("com.brokenmass.plugin.DSP.BetterStats", PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class BetterStats : BaseUnityPlugin
    {
        private static Dictionary<int, ProductMetrics> counter = new Dictionary<int, ProductMetrics>();

        internal void Awake()
        {        
            Log = Logger;
        }

        private class ProductMetrics
        {
            public string Product;
            public string Planet;
            public string Star;
            public float production = 0;
            public float consumption = 0;
            public int producers = 0;
            public int consumers = 0;
            public long game_time_elapsed = 0;
            public string unique_game_identifier;
        }

        private static void EnsureId(ref Dictionary<int, ProductMetrics> dict, int id)
        {
            if (!dict.ContainsKey(id))
            {
                string Product = LDB.items.Select(id).name;

                dict.Add(id, new ProductMetrics()
                {
                    Product = Product
                });
            }
        }
     
        // speed of fastest belt(mk3 belt) is 1800 items per minute
        public const float BELT_MAX_ITEMS_PER_MINUTE = 1800;
        public const float TICKS_PER_SEC = 60.0f;
        private const float RAY_RECEIVER_GRAVITON_LENS_CONSUMPTION_RATE_PER_MIN = 0.1f;

        public static void AddPlanetFactoryData(PlanetFactory planetFactory)
        {
            var factorySystem = planetFactory.factorySystem;
            var transport = planetFactory.transport;
            var veinPool = planetFactory.planet.factory.veinPool;
            var miningSpeedScale = (double)GameMain.history.miningSpeedScale;
            var maxProductivityIncrease = ResearchTechHelper.GetMaxProductivityIncrease();
            var maxSpeedIncrease = ResearchTechHelper.GetMaxSpeedIncrease();

            for (int i = 1; i < factorySystem.minerCursor; i++)
            {
                var miner = factorySystem.minerPool[i];
                if (i != miner.id) continue;

                var productId = miner.productId;
                var veinId = (miner.veinCount != 0) ? miner.veins[miner.currentVeinIndex] : 0;

                if (miner.type == EMinerType.Water)
                {
                    productId = planetFactory.planet.waterItemId;
                }
                else if (productId == 0)
                {
                    productId = veinPool[veinId].productId;
                }

                if (productId == 0) continue;


                EnsureId(ref counter, productId);

                float frequency = 60f / (float)((double)miner.period / 600000.0);
                float speed = (float)(0.0001 * (double)miner.speed * miningSpeedScale);

                float production = 0f;
                if (factorySystem.minerPool[i].type == EMinerType.Water)
                {
                    production = frequency * speed;
                }
                if (factorySystem.minerPool[i].type == EMinerType.Oil)
                {
                    production = frequency * speed * (float)((double)veinPool[veinId].amount * (double)VeinData.oilSpeedMultiplier);
                }

                // flag to tell us if it's one of the advanced miners they added in the 20-Jan-2022 release
                var isAdvancedMiner = false;
                if (factorySystem.minerPool[i].type == EMinerType.Vein)
                {
                    production = frequency * speed * miner.veinCount;
                    var minerEntity = factorySystem.factory.entityPool[miner.entityId];
                    isAdvancedMiner = minerEntity.stationId > 0 && minerEntity.minerId > 0;
                }

                // advanced miners aren't limited by belts
                if (!isAdvancedMiner)
                {
                    production = Math.Min(BELT_MAX_ITEMS_PER_MINUTE, production);
                }

                counter[productId].production += production;
                counter[productId].producers++;
            }
            for (int i = 1; i < factorySystem.assemblerCursor; i++)
            {
                var assembler = factorySystem.assemblerPool[i];
                if (assembler.id != i || assembler.recipeId == 0) continue;

                var frequency = 60f / (float)((double)assembler.timeSpend / 600000.0);
                var speed = (float)(0.0001 * Math.Max(assembler.speedOverride, assembler.speed));

                // forceAccMode is true when Production Speedup is selected
                if (assembler.forceAccMode)
                {
                    speed += speed * maxSpeedIncrease;
                }
                else
                {
                    frequency += frequency * maxProductivityIncrease;
                }

                for (int j = 0; j < assembler.requires.Length; j++)
                {
                    var productId = assembler.requires[j];
                    EnsureId(ref counter, productId);

                    counter[productId].consumption += frequency * speed * assembler.requireCounts[j];
                    counter[productId].consumers++;
                }

                for (int j = 0; j < assembler.products.Length; j++)
                {
                    var productId = assembler.products[j];
                    EnsureId(ref counter, productId);

                    counter[productId].production += frequency * speed * assembler.productCounts[j];
                    counter[productId].producers++;
                }
            }
            for (int i = 1; i < factorySystem.fractionateCursor; i++)
            {
                var fractionator = factorySystem.fractionatePool[i];
                if (fractionator.id != i) continue;

                if (fractionator.fluidId != 0)
                {
                    var productId = fractionator.fluidId;
                    EnsureId(ref counter, productId);

                    counter[productId].consumption += 60f * 30f * fractionator.produceProb;
                    counter[productId].consumers++;
                }
                if (fractionator.productId != 0)
                {
                    var productId = fractionator.productId;
                    EnsureId(ref counter, productId);

                    counter[productId].production += 60f * 30f * fractionator.produceProb;
                    counter[productId].producers++;
                }

            }
            for (int i = 1; i < factorySystem.ejectorCursor; i++)
            {
                var ejector = factorySystem.ejectorPool[i];
                if (ejector.id != i) continue;

                EnsureId(ref counter, ejector.bulletId);

                counter[ejector.bulletId].consumption += 60f / (float)(ejector.chargeSpend + ejector.coldSpend) * 600000f;
                counter[ejector.bulletId].consumers++;
            }
            for (int i = 1; i < factorySystem.siloCursor; i++)
            {
                var silo = factorySystem.siloPool[i];
                if (silo.id != i) continue;

                EnsureId(ref counter, silo.bulletId);

                counter[silo.bulletId].consumption += 60f / (float)(silo.chargeSpend + silo.coldSpend) * 600000f;
                counter[silo.bulletId].consumers++;
            }

            for (int i = 1; i < factorySystem.labCursor; i++)
            {
                var lab = factorySystem.labPool[i];
                if (lab.id != i) continue;
                // lab timeSpend is in game ticks, here we are figuring out the same number shown in lab window, example: 2.5 / m
                // when we are in Production Speedup mode `speedOverride` is juiced. Otherwise we need to bump the frequency to account
                // for the extra product produced after `extraTimeSpend` game ticks
                var labSpeed = lab.forceAccMode ? (int)(lab.speed * (1.0 + maxSpeedIncrease) + 0.1) : lab.speed;
                float frequency = (float)(1f / (lab.timeSpend / GameMain.tickPerSec / (60f * labSpeed)));

                if (!lab.forceAccMode)
                {
                    frequency += frequency * maxProductivityIncrease;
                }

                if (lab.matrixMode)
                {
                    for (int j = 0; j < lab.requires.Length; j++)
                    {
                        var productId = lab.requires[j];
                        EnsureId(ref counter, productId);

                        counter[productId].consumption += frequency * lab.requireCounts[j];
                        counter[productId].consumers++;
                    }

                    for (int j = 0; j < lab.products.Length; j++)
                    {
                        var productId = lab.products[j];
                        EnsureId(ref counter, productId);

                        counter[productId].production += frequency * lab.productCounts[j];
                        counter[productId].producers++;
                    }
                }
                else if (lab.researchMode && lab.techId > 0)
                {
                    // In this mode we can't just use lab.timeSpend to figure out how long it takes to consume 1 item (usually a cube)
                    // So, we figure out how many hashes a single cube represents and use the research mode research speed to come up with what is basically a research rate
                    var techProto = LDB.techs.Select(lab.techId);
                    if (techProto == null)
                        continue;
                    TechState techState = GameMain.history.TechState(techProto.ID);
                    for (int index = 0; index < techProto.itemArray.Length; ++index)
                    {
                        var item = techProto.Items[index];
                        var cubesNeeded = techProto.GetHashNeeded(techState.curLevel) * techProto.ItemPoints[index] / 3600L;
                        var researchRate = GameMain.history.techSpeed * 60.0f;
                        var hashesPerCube = (float) techState.hashNeeded / cubesNeeded;
                        var researchFreq = hashesPerCube / researchRate;
                        EnsureId(ref counter, item);
                        counter[item].consumers++;
                        counter[item].consumption += researchFreq * GameMain.history.techSpeed;
                    }
                }
            }
            double gasTotalHeat = planetFactory.planet.gasTotalHeat;
            var collectorsWorkCost = transport.collectorsWorkCost;
            for (int i = 1; i < transport.stationCursor; i++)
            {
                var station = transport.stationPool[i];
                if (station == null || station.id != i || !station.isCollector) continue;

                float collectSpeedRate = (gasTotalHeat - collectorsWorkCost > 0.0) ? ((float)((miningSpeedScale * gasTotalHeat - collectorsWorkCost) / (gasTotalHeat - collectorsWorkCost))) : 1f;

                for (int j = 0; j < station.collectionIds.Length; j++)
                {
                    var productId = station.collectionIds[j];
                    EnsureId(ref counter, productId);

                    counter[productId].production += 60f * TICKS_PER_SEC * station.collectionPerTick[j] * collectSpeedRate;
                    counter[productId].producers++;
                }
            }
            for (int i = 1; i < planetFactory.powerSystem.genCursor; i++)
            {
                var generator = planetFactory.powerSystem.genPool[i];
                if (generator.id != i)
                {
                    continue;
                }
                var isFuelConsumer = generator.fuelHeat > 0 && generator.fuelId > 0 && generator.productId == 0;
                if ((generator.productId == 0 || generator.productHeat == 0) && !isFuelConsumer)
                {
                    continue;
                }

                if (isFuelConsumer)
                {
                    // account for fuel consumption by power generator
                    var productId = generator.fuelId;
                    EnsureId(ref counter, productId);

                    counter[productId].consumption += 60.0f * TICKS_PER_SEC * generator.useFuelPerTick / generator.fuelHeat;
                    counter[productId].consumers++;
                }
                else
                {
                    var productId = generator.productId;
                    EnsureId(ref counter, productId);

                    counter[productId].production += 60.0f * TICKS_PER_SEC * generator.capacityCurrentTick / generator.productHeat;
                    counter[productId].producers++;
                    if (generator.catalystId > 0)
                    {
                        // account for consumption of critical photons by ray receivers
                        EnsureId(ref counter, generator.catalystId);
                        counter[generator.catalystId].consumption += RAY_RECEIVER_GRAVITON_LENS_CONSUMPTION_RATE_PER_MIN;
                        counter[generator.catalystId].consumers++;
                    }
                }
            }
        }
        internal static ManualLogSource Log;
        public long timeCurrent;
        public long timePrev;
        List<ProductMetrics> jsonList = new List<ProductMetrics>();

        void Update()
        {
            timeCurrent = GameMain.instance.timei;
            if (timeCurrent - timePrev >= 3600)
            {
                jsonList.Clear();
                for (int i = 0; i < GameMain.data.factoryCount; i++)
                {
                    counter.Clear();
                    AddPlanetFactoryData(GameMain.data.factories[i]);
                    foreach (var prodId in counter.Keys)
                    {
                        counter[prodId].Star = GameMain.data.factories[i].planet.star.ToString();
                        counter[prodId].Planet = GameMain.data.factories[i].planet.ToString();
                        try
                        {
                            counter[prodId].Product = LDB.items.Select(prodId).name;
                        }
                        catch (Exception e)
                        {
                            counter[prodId].Product = "null";
                        }
                        counter[prodId].game_time_elapsed = timeCurrent;
                        counter[prodId].unique_game_identifier = GameMain.data.gameDesc.galaxySeed.ToString() + " " + GameMain.data.gameDesc.creationTime.ToString();
                        jsonList.Add(counter[prodId]);
                        //Logger.LogInfo(counter[prodId]);
                        //Logger.LogInfo(JsonUtility.ToJson(counter[prodId]));
                        //Logger.LogInfo(GameMain.data.gameDesc.galaxySeed);
                        //Logger.LogInfo(GameMain.data.gameDesc.creationTime);
                    }
                }
                //Logger.LogInfo(jsonList[0]);
                //Logger.LogInfo(JsonUtility.ToJson(jsonList[0]));
                //Logger.LogInfo(JsonUtility.ToJson(jsonList[1]));
                //Logger.LogInfo(JsonUtility.ToJson(jsonList));
                Logger.LogInfo(JsonConvert.SerializeObject(jsonList));
                //File.WriteAllText(@"d:\DSP_json.json", JsonConvert.SerializeObject(jsonList));
                //AmazonS3Uploader amazonS3 = new AmazonS3Uploader();
                //amazonS3.UploadFile();
                Logger.LogInfo("\nIteration_done\n");
                timePrev = timeCurrent;
            }
        }
    }
}
