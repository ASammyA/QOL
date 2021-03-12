using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    public static class ClipboardExtension
    {
        /// <summary>
        /// Puts the string into the Clipboard.
        /// </summary>
        public static void CopyToClipboard(this string str)
        {
            GUIUtility.systemCopyBuffer = str;
        }
    }

    public class PlayerAction_Build_Patch
    {
        public static bool lastFlag;
        public static string lastCursorText;
        public static bool lastCursorWarning;
        public static Vector3 lastPosition = Vector3.zero;
        public static float lastYaw = 0f;
        public static int lastPath = 0;

        public static bool executeBuildUpdatePreviews = true;

        public static int ignoredTicks = 0;
        public static int path = 0;

        private static Color BP_GRID_COLOR = new Color(1f, 1f, 1f, 0.2f);
        private static Color ADD_SELECTION_GIZMO_COLOR = new Color(1f, 1f, 1f, 1f);
        private static Color REMOVE_SELECTION_GIZMO_COLOR = new Color(0.9433962f, 0.1843137f, 0.1646493f, 1f);
        private static CircleGizmo circleGizmo;
        private static int[] _nearObjectIds = new int[4096];

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "CreatePrebuilds")]
        public static bool CreatePrebuilds_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.waitConfirm && VFInput._buildConfirm.onDown && __instance.buildPreviews.Count > 0
                && MultiBuild.IsMultiBuildEnabled() && !__instance.multiLevelCovering)
            {
                if (MultiBuild.startPos == Vector3.zero)
                {
                    MultiBuild.startPos = __instance.groundSnappedPos;
                    lastPosition = Vector3.zero;
                    return false;
                }
                else
                {
                    MultiBuild.startPos = Vector3.zero;
                    return true;
                }
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "BuildMainLogic")]
        public static bool BuildMainLogic_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.handPrefabDesc == null ||
                __instance.handPrefabDesc.minerType != EMinerType.None ||
                __instance.player.planetData.type == EPlanetType.Gas ||
                BlueprintManager.data.copiedBuildings.Count > 1
                )
            {
                MultiBuild.multiBuildPossible = false;
            }
            else
            {
                MultiBuild.multiBuildPossible = true;
            }

            if (MultiBuild.itemSpecificSpacing.Value && __instance.handItem != null && MultiBuild.spacingIndex != __instance.handItem.ID)
            {
                MultiBuild.spacingIndex = __instance.handItem.ID;
                if (!MultiBuild.spacingStore.ContainsKey(MultiBuild.spacingIndex))
                {
                    MultiBuild.spacingStore[MultiBuild.spacingIndex] = 0;
                }
            }

            // As multibuild increase calculation exponentially (collision and rendering must be performed for every entity), we hijack the BuildMainLogic
            // and execute the relevant submethods only when needed
            executeBuildUpdatePreviews = true;
            /* if (MultiBuild.IsMultiBuildRunning())
             {
                 if (lastPosition != __instance.groundSnappedPos)
                 {
                     lastPosition = __instance.groundSnappedPos;
                     executeBuildUpdatePreviews = true;
                 }
                 else
                 {
                     executeBuildUpdatePreviews = false;
                 }
             }
             else
             {
                 lastPosition = Vector3.zero;
             }*/

            // Run the preview methods if we have changed position, if we have received a relevant keyboard input or in any case every MAX_IGNORED_TICKS ticks.
            executeBuildUpdatePreviews = true;

            bool flag;
            if (executeBuildUpdatePreviews)
            {
                __instance.DetermineBuildPreviews();
                flag = __instance.CheckBuildConditions();
                __instance.UpdatePreviews();
                __instance.UpdateGizmos();

                lastCursorText = __instance.cursorText;
                lastCursorWarning = __instance.cursorWarning;
                lastFlag = flag;

                ignoredTicks = 0;
            }
            else
            {
                __instance.cursorText = lastCursorText;
                __instance.cursorWarning = lastCursorWarning;
                flag = lastFlag;
                ignoredTicks++;
            }

            if (flag)
            {
                __instance.CreatePrebuilds();

                if (__instance.waitConfirm && VFInput._buildConfirm.onDown)
                {
                    __instance.ClearBuildPreviews();
                    ignoredTicks = MultiBuild.MAX_IGNORED_TICKS;
                }
            }

            return false;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
        public static bool DetermineBuildPreviews_Prefix(ref PlayerAction_Build __instance)
        {
            var runOriginal = true;

            if (__instance.controller.cmd.mode == 1 && __instance.player.planetData.type != EPlanetType.Gas && __instance.cursorValid)
            {
                if (__instance.handPrefabDesc != null && __instance.handPrefabDesc.minerType != EMinerType.None)
                {
                    return true;
                }
                __instance.waitConfirm = __instance.cursorValid;
                __instance.multiLevelCovering = false;
                if (__instance.handPrefabDesc != null && __instance.handPrefabDesc.multiLevel)
                {
                    int objectProtoId = __instance.GetObjectProtoId(__instance.castObjId);
                    if (objectProtoId == __instance.handItem.ID)
                    {
                        __instance.multiLevelCovering = true;
                    }
                }
                if (__instance.multiLevelCovering && !MultiBuild.IsMultiBuildRunning())
                {
                    return true;
                }
                if(!MultiBuild.IsMultiBuildRunning() && !BlueprintManager.hasData)
                {
                    return true;
                }

                // full hijacking of DetermineBuildPreviews
                runOriginal = false;

                if (VFInput._switchSplitter.onDown)
                {
                    __instance.modelOffset++;
                }

                if (VFInput._rotate.onDown)
                {
                    __instance.yaw += 90f;
                    __instance.yaw = Mathf.Repeat(__instance.yaw, 360f);
                    __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;
                }
                if (VFInput._counterRotate.onDown)
                {
                    __instance.yaw -= 90f;
                    __instance.yaw = Mathf.Repeat(__instance.yaw, 360f);
                    __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;
                }

                __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;
                __instance.previewPose.position = Vector3.zero;
                __instance.previewPose.rotation = Quaternion.identity;

                //__instance.previewPose.position = __instance.cursorTarget;
                //__instance.previewPose.rotation = Maths.SphericalRotation(__instance.previewPose.position, __instance.yaw);

                var inversePreviewRot = Quaternion.Inverse(__instance.previewPose.rotation);

                if (lastPosition == __instance.groundSnappedPos && lastYaw == __instance.yaw && path == lastPath)
                {
                    return false;
                }
                lastPosition = __instance.groundSnappedPos;
                lastYaw = __instance.yaw;
                lastPath = path;

                List<BuildPreview> previews = new List<BuildPreview>();
                var absolutePositions = new List<Vector3>(10);
                if (BlueprintManager.data.copiedBuildings.Count == 1 && MultiBuild.IsMultiBuildRunning())
                {
                    var building = BlueprintManager.data.copiedBuildings.First().Value;

                    int snapPath = path;
                    Vector3[] snaps = new Vector3[1024];

                    var snappedPointCount = __instance.planetAux.SnapLineNonAlloc(MultiBuild.startPos, __instance.groundSnappedPos, ref snapPath, snaps);

                    var desc = building.itemProto.prefabDesc;
                    Collider[] colliders = new Collider[desc.buildColliders.Length];
                    Vector3 previousPos = Vector3.zero;

                    var maxSnaps = Math.Max(1, snappedPointCount - MultiBuild.spacingStore[MultiBuild.spacingIndex]);

                    for (int s = 0; s < maxSnaps; s++)
                    {
                        var pos = snaps[s];
                        var rot = Maths.SphericalRotation(snaps[s], __instance.yaw);

                        if (s > 0)
                        {
                            var sqrDistance = (previousPos - pos).sqrMagnitude;

                            // power towers
                            if (desc.isPowerNode && !desc.isAccumulator && sqrDistance < 12.25f) continue;

                            // wind turbines
                            if (desc.windForcedPower && sqrDistance < 110.25f) continue;

                            // ray receivers
                            if (desc.gammaRayReceiver && sqrDistance < 110.25f) continue;

                            // logistic stations
                            if (desc.isStation && sqrDistance < (desc.isStellarStation ? 841f : 225f)) continue;

                            // ejector
                            if (desc.isEjector && sqrDistance < 110.25f) continue;

                            if (desc.hasBuildCollider)
                            {
                                var foundCollision = false;
                                for (var j = 0; j < desc.buildColliders.Length && !foundCollision; j++)
                                {
                                    var colliderData = desc.buildColliders[j];
                                    colliderData.pos = pos + rot * colliderData.pos;
                                    colliderData.q = rot * colliderData.q;
                                    // check only collision with layer 27 (the layer used by the our own building colliders for the previously 'placed' building)
                                    foundCollision = Physics.CheckBox(colliderData.pos, colliderData.ext, colliderData.q, 134217728, QueryTriggerInteraction.Collide);
                                }

                                if (foundCollision) continue;
                            }
                        }

                        if (s > 0 && MultiBuild.spacingStore[MultiBuild.spacingIndex] > 0)
                        {
                            s += MultiBuild.spacingStore[MultiBuild.spacingIndex];
                            pos = snaps[s];
                            rot = Maths.SphericalRotation(snaps[s], __instance.yaw);
                        }

                        previousPos = pos;
                        absolutePositions.Add(pos);

                        //pose.position - this.previewPose.position =  this.previewPose.rotation * buildPreview.lpos;
                        //pose.rotation = this.previewPose.rotation * buildPreview.lrot;
                        if (desc.hasBuildCollider)
                        {
                            for (var j = 0; j < desc.buildColliders.Length; j++)
                            {
                                // create temporary collider entities for the latest 'positioned' building
                                if (colliders[j] != null)
                                {
                                    ColliderPool.PutCollider(colliders[j]);
                                }

                                var colliderData = desc.buildColliders[j];
                                colliderData.pos = pos + rot * colliderData.pos;
                                colliderData.q = rot * colliderData.q;
                                colliders[j] = ColliderPool.TakeCollider(colliderData);
                                colliders[j].gameObject.layer = 27;
                            }
                        }

                        previews = previews.Concat(BlueprintManager.paste(pos, __instance.yaw, out _)).ToList();
                    }

                    foreach (var collider in colliders)
                    {
                        if (collider != null)
                        {
                            ColliderPool.PutCollider(collider);
                        }
                    }
                }
                else
                {
                    previews = BlueprintManager.paste(__instance.groundSnappedPos, __instance.yaw, out absolutePositions);
                }

                ActivateColliders(ref __instance.nearcdLogic, absolutePositions);

                // synch previews
                for (var i = 0; i < previews.Count; i++)
                {
                    var updated = previews[i];
                    if (i >= __instance.buildPreviews.Count)
                    {
                        __instance.AddBuildPreview(updated);
                        continue;
                    }

                    var original = __instance.buildPreviews[i];

                    if (original.desc != updated.desc || original.item != updated.item)
                    {
                        __instance.RemoveBuildPreview(original);
                        __instance.AddBuildPreview(previews[i]);
                        continue;
                    }

                    updated.previewIndex = original.previewIndex;
                    __instance.buildPreviews[i] = updated;
                }

                if (__instance.buildPreviews.Count > previews.Count)
                {
                    var toRemove = __instance.buildPreviews.Count - previews.Count;

                    for (var i = 0; i < toRemove; i++)
                    {
                        __instance.RemoveBuildPreview(previews.Count);
                    }
                }
            }

            return runOriginal;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "UpdatePreviews")]
        public static bool UpdatePreviews_Prefix(ref PlayerAction_Build __instance)
        {
            if(__instance.upgrading || __instance.destructing)
            {
                return true;
            }
            int graphPoints = 0;
            int pointCount = __instance.connGraph.pointCount;
            __instance.connRenderer.ClearXSigns();
            __instance.connRenderer.ClearUpgradeArrows();
            for (int i = 0; i < __instance.buildPreviews.Count; i++)
            {
                BuildPreview buildPreview = __instance.buildPreviews[i];
                if (buildPreview.needModel)
                {
                    __instance.CreatePreviewModel(buildPreview);
                    int previewIndex = buildPreview.previewIndex;
                    if (previewIndex >= 0)
                    {
                        __instance.previewRenderers[previewIndex].transform.localPosition = __instance.previewPose.position + __instance.previewPose.rotation * buildPreview.lpos;
                        __instance.previewRenderers[previewIndex].transform.localRotation = __instance.previewPose.rotation * buildPreview.lrot;
                        bool isInserter = buildPreview.desc.isInserter;
                        Material material;
                        if (isInserter)
                        {
                            Material original = ((buildPreview.condition != EBuildCondition.Ok) ? Configs.builtin.previewErrorMat_Inserter : Configs.builtin.previewOkMat_Inserter);
                            Material existingMaterial = __instance.previewRenderers[previewIndex].sharedMaterial;

                            if(existingMaterial != null && !existingMaterial.name.StartsWith(original.name))
                            {
                                UnityEngine.Object.Destroy(existingMaterial);
                                existingMaterial = null;
                            }

                            if (existingMaterial == null)
                            {                               
                                material = UnityEngine.Object.Instantiate<Material>(original);
                            } else
                            {
                                material = existingMaterial;
                            }

                            bool t;
                            bool t2;
                            __instance.GetInserterT1T2(buildPreview.objId, out t, out t2);
                            if (buildPreview.outputObjId != 0 && !__instance.ObjectIsBelt(buildPreview.outputObjId) && !__instance.ObjectIsInserter(buildPreview.outputObjId))
                            {
                                t2 = true;
                            }
                            if (buildPreview.inputObjId != 0 && !__instance.ObjectIsBelt(buildPreview.inputObjId) && !__instance.ObjectIsInserter(buildPreview.inputObjId))
                            {
                                t = true;
                            }
                            material.SetVector("_Position1", __instance.Vector3BoolToVector4(Vector3.zero, t));
                            material.SetVector("_Position2", __instance.Vector3BoolToVector4(Quaternion.Inverse(buildPreview.lrot) * (buildPreview.lpos2 - buildPreview.lpos), t2));
                            material.SetVector("_Rotation1", __instance.QuaternionToVector4(Quaternion.identity));
                            material.SetVector("_Rotation2", __instance.QuaternionToVector4(Quaternion.Inverse(buildPreview.lrot) * buildPreview.lrot2));
                            __instance.previewRenderers[previewIndex].enabled = (buildPreview.condition != EBuildCondition.NeedConn);
                        }
                        else
                        {
                            __instance.previewRenderers[previewIndex].enabled = true;
                            Material original =  ((buildPreview.condition != EBuildCondition.Ok) ? Configs.builtin.previewErrorMat : Configs.builtin.previewOkMat);;

                            Material existingMaterial = __instance.previewRenderers[previewIndex].sharedMaterial;

                            if (existingMaterial != null && !existingMaterial.name.StartsWith(original.name))
                            {
                                UnityEngine.Object.Destroy(existingMaterial);
                                existingMaterial = null;
                            }

                            if (existingMaterial == null)
                            {
                                material = UnityEngine.Object.Instantiate<Material>(original);
                            }
                            else
                            {
                                material = existingMaterial;
                            }
                        }
                        __instance.previewRenderers[previewIndex].sharedMaterial = material;
                    }
                }
                else if (buildPreview.previewIndex >= 0)
                {
                    __instance.FreePreviewModel(buildPreview);
                }
                if (buildPreview.isConnNode)
                {
                    uint color = 4U;
                    if (buildPreview.condition != EBuildCondition.Ok)
                    {
                        color = 0U;
                    }
                    if (graphPoints < pointCount)
                    {
                        __instance.connGraph.points[graphPoints] = buildPreview.lpos;
                        __instance.connGraph.colors[graphPoints] = color;
                    }
                    else
                    {
                        __instance.connGraph.AddPoint(buildPreview.lpos, color);
                    }
                    graphPoints++;
                }
            }
            __instance.connGraph.SetPointCount(graphPoints);
            if (graphPoints > 0)
            {
                __instance.showConnGraph = true;
            }
        
            return false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "SetCopyInfo")]
        public static void SetCopyInfo_Postfix(ref PlayerAction_Build __instance, int objectId)
        {
            BlueprintManager.Reset();
            if (objectId < 0)
                return;

            if (BlueprintManager.copyBuilding(objectId) != null)
            {
                __instance.yaw = BlueprintManager.data.referenceYaw;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "CheckBuildConditions")]
        public static void CheckBuildConditions_Postfix(PlayerAction_Build __instance, ref bool __result)
        {
            if (BlueprintManager.pastedEntities.Count > 1 && !__result)
            {
                var allGood = true;
                __instance.cursorWarning = false;
                for (int i = 0; i < __instance.buildPreviews.Count; i++)
                {
                    BuildPreview buildPreview = __instance.buildPreviews[i];

                    if (buildPreview.condition == EBuildCondition.OutOfReach)
                    {
                        buildPreview.condition = EBuildCondition.Ok;
                    }
                    bool isConnected = buildPreview.inputObjId != 0 || buildPreview.outputObjId != 0;
                    if (buildPreview.desc.isInserter && (
                        buildPreview.condition == EBuildCondition.TooFar ||
                        buildPreview.condition == EBuildCondition.TooClose ||
                        (buildPreview.condition == EBuildCondition.Collide && isConnected)
                        ))
                    {
                        buildPreview.condition = EBuildCondition.Ok;
                    }

                    if (buildPreview.condition != EBuildCondition.Ok)
                    {
                        allGood = false;
                        if(!__instance.cursorWarning)
                        {
                            __instance.cursorWarning = true;
                            __instance.cursorText = buildPreview.conditionText;
                        }
                    }
                }

                if (allGood)
                {
                    UICursor.SetCursor(ECursor.Default);
                    __instance.cursorText = "点击鼠标建造".Translate();
                }

                __result = allGood;
            }
        }

        private static bool acceptCommand = true;
        public static bool bpMode = false;
        private static Dictionary<int, BoxGizmo> bpSelection = new Dictionary<int, BoxGizmo>();
        private static Collider[] _tmp_cols = new Collider[1024];

        [HarmonyPostfix, HarmonyPatch(typeof(UIBuildingGrid), "Update")]
        public static void UIBuildingGrid_Update_Postfix(ref UIBuildingGrid __instance)
        {
            if(bpMode)
            {
                __instance.material.SetColor("_TintColor", BP_GRID_COLOR);
            }
        }
        

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "GameTick")]
        public static void GameTick_Postfix(ref PlayerAction_Build __instance)
        {
            if (BlueprintManager.hasData && Input.GetKeyUp(KeyCode.Backslash))
            {
                var data = BlueprintManager.data.export();
                Debug.Log($"blueprint size: {data.Length}");
                data.CopyToClipboard();
            }

            if (acceptCommand && VFInput.shift && VFInput.control)
            {
                ToggleBpMode();
                acceptCommand = false;
            }
            if (!VFInput.shift && !VFInput.control)
            {
                acceptCommand = true;
            }

            if (bpMode)
            {
                var removeMode = acceptCommand && VFInput.control;

                if (circleGizmo != null)
                {
                    circleGizmo.color = removeMode ? REMOVE_SELECTION_GIZMO_COLOR : ADD_SELECTION_GIZMO_COLOR;
                    circleGizmo.position = __instance.groundTestPos;
                    circleGizmo.radius = MultiBuild.selectionRadius;
                }

                if (VFInput._buildConfirm.pressing)
                {
                    circleGizmo.color = removeMode ? REMOVE_SELECTION_GIZMO_COLOR : ADD_SELECTION_GIZMO_COLOR;

                    // target only buildings
                    int mask = 131072;
                    int found = Physics.OverlapBoxNonAlloc(__instance.groundTestPos, new Vector3(MultiBuild.selectionRadius, 1f, MultiBuild.selectionRadius), _tmp_cols, Maths.SphericalRotation(__instance.groundTestPos, 0f), mask, QueryTriggerInteraction.Collide);

                    PlanetPhysics planetPhysics = __instance.player.planetData.physics;
                    for (int i = 0; i < found; i++)
                    {
                        ColliderData colliderData;
                        planetPhysics.GetColliderData(_tmp_cols[i], out colliderData);
                        if(colliderData.objId > 0)
                        {
                            var entityId = colliderData.objId;
                            if (removeMode)
                            {
                                if (bpSelection.ContainsKey(entityId))
                                {
                                    bpSelection[entityId].Close();
                                    bpSelection.Remove(entityId);
                                }
                            }
                            else if (!bpSelection.ContainsKey(entityId))
                            {
                                var entityData = __instance.factory.entityPool[entityId];
                                ItemProto itemProto = LDB.items.Select((int)entityData.protoId);
                                var gizmo = BoxGizmo.Create(entityData.pos, entityData.rot, itemProto.prefabDesc.selectCenter, itemProto.prefabDesc.selectSize);
                                gizmo.multiplier = 1f;
                                gizmo.alphaMultiplier = itemProto.prefabDesc.selectAlpha;
                                gizmo.fadeInScale = gizmo.fadeOutScale = 1.3f;
                                gizmo.fadeInTime = gizmo.fadeOutTime = 0.05f;
                                gizmo.fadeInFalloff = gizmo.fadeOutFalloff = 0.5f;
                                gizmo.color = Color.white;

                                gizmo.Open();

                                bpSelection.Add(entityId, gizmo);
                            }
                        }
                    }
                   
                }
            }
        }

        public static void ToggleBpMode()
        {
            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;
            actionBuild.controller.cmd.type = ECommand.Build;
            acceptCommand = false;
            lastPosition = Vector3.zero;
            if (!bpMode)
            {
                bpMode = true;
                actionBuild.player.SetHandItems(0, 0, 0);

                BlueprintManager.Reset();
                if (circleGizmo == null)
                {
                    circleGizmo = CircleGizmo.Create(6, Vector3.zero, 10);

                    circleGizmo.fadeOutScale = circleGizmo.fadeInScale = 1.8f;
                    circleGizmo.fadeOutTime = circleGizmo.fadeInTime = 0.15f;
                    circleGizmo.autoRefresh = true;
                    circleGizmo.Open();
                }
            }
            else
            {
                EndBpMode(true);
                BlueprintManager.EnterBuildModeAfterBp();
            }
        }

        

        public static void EndBpMode(bool createBp)
        {

            if(!bpMode || (!acceptCommand && !createBp))
            {
                return;
            }

            bpMode = false;
            foreach (var entry in bpSelection)
            {
                if (createBp)
                {
                    BlueprintManager.copyBuilding(entry.Key);
                    BlueprintManager.copyBelt(entry.Key);
                }
                entry.Value.Close();
            }
            bpSelection.Clear();

            if (circleGizmo != null)
            {
                circleGizmo.Close();
                circleGizmo = null;
            }
        }

        public static void ActivateColliders(ref NearColliderLogic nearCdLogic, List<Vector3> positions)
        {
            for (int s = 0; s < positions.Count; s++)
            {
                nearCdLogic.activeColHashCount = 0;
                var center = positions[s];

                nearCdLogic.MarkActivePos(center);

                if (nearCdLogic.activeColHashCount > 0)
                {
                    for (int i = 0; i < nearCdLogic.activeColHashCount; i++)
                    {
                        int num2 = nearCdLogic.activeColHashes[i];
                        ColliderData[] colliderPool = nearCdLogic.colChunks[num2].colliderPool;
                        for (int j = 1; j < nearCdLogic.colChunks[num2].cursor; j++)
                        {
                            if (colliderPool[j].idType != 0)
                            {
                                if ((colliderPool[j].pos - center).sqrMagnitude <= 25f * 4f + colliderPool[j].ext.sqrMagnitude)
                                {
                                    if (colliderPool[j].usage != EColliderUsage.Physics || colliderPool[j].objType != EObjectType.Entity)
                                    {
                                        int num3 = num2 << 20 | j;
                                        if (nearCdLogic.colliderObjs.ContainsKey(num3))
                                        {
                                            nearCdLogic.colliderObjs[num3].live = true;
                                        }
                                        else
                                        {
                                            nearCdLogic.colliderObjs[num3] = new ColliderObject(num3, colliderPool[j]);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
